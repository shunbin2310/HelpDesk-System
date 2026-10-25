using HelpDesk.Application.Authentication;
using HelpDesk.Application.Tickets;
using HelpDesk.Application.Users;
using HelpDesk.Application.Workflows;
using HelpDesk.Domain.Constants;
using HelpDesk.Domain.Entities.Tickets;
using HelpDesk.Domain.Enums;
using HelpDesk.Infrastructure.Persistence;
using HelpDesk.Infrastructure.Workflows;
using HelpDesk.Infrastructure.Attachments;
using HelpDesk.Infrastructure.Delegations;
using Microsoft.EntityFrameworkCore;

namespace HelpDesk.Infrastructure.Tickets;

public sealed class TicketService(HelpDeskDbContext db, ICurrentUserContext current, IWorkflowEngine workflow, TicketAttachmentWriter files, DelegationProcessor delegations) : ITicketService
{
    public async Task<TicketResult<TicketPage>> ListAsync(int page, string? status, bool onlyUnassigned, CancellationToken cancellationToken = default)
    {
        var actor = await WorkflowActorReader.ReadAsync(db, current.UserId, cancellationToken);
        if (actor is null)
            return new(TicketResultStatus.Forbidden, Error: "An active account is required.");
        status = string.IsNullOrWhiteSpace(status) ? null : status.Trim().ToUpperInvariant();
        if (status is not (null or TicketWorkflow.Open or TicketWorkflow.InProgress or TicketWorkflow.Resolved or TicketWorkflow.Closed))
            return new(TicketResultStatus.Invalid, Error: "Choose a valid Ticket status.");
        page = Math.Clamp(page, 1, 1000000);
        var query = Items(Visible(actor));
        if (status is not null)
            query = query.Where(x => x.Status == status);
        if (onlyUnassigned)
            query = query.Where(x => x.AssignedToUserId == null);
        var total = await query.CountAsync(cancellationToken);
        var items = await query.OrderByDescending(x => x.UpdatedAtUtc).ThenByDescending(x => x.Id)
            .Skip((page - 1) * 25).Take(25).ToListAsync(cancellationToken);
        return new(TicketResultStatus.Success, new(items, total, page));
    }

    public async Task<TicketResult<TicketDetail>> GetAsync(long ticketId, CancellationToken cancellationToken = default)
    {
        var actor = await WorkflowActorReader.ReadAsync(db, current.UserId, cancellationToken);
        if (actor is null)
            return Missing();
        var ticket = await Visible(actor).SingleOrDefaultAsync(x => x.Id == ticketId, cancellationToken);
        if (ticket is null)
            return Missing();
        var state = await workflow.GetAsync(ticketId, cancellationToken);
        if (state.Status != WorkflowResultStatus.Success)
            return Missing();
        var item = await Items(Visible(actor).Where(x => x.Id == ticketId)).SingleOrDefaultAsync(cancellationToken);
        if (item is null)
            return Missing();
        var history = await (from entry in db.TicketHistories.AsNoTracking()
                             join oldUser in db.Users on entry.OldAssigneeUserId equals oldUser.Id into oldUsers
                             from oldUser in oldUsers.DefaultIfEmpty()
                             join newUser in db.Users on entry.NewAssigneeUserId equals newUser.Id into newUsers
                             from newUser in newUsers.DefaultIfEmpty()
                             where entry.TicketId == ticketId
                             orderby entry.CreatedAtUtc, entry.Id
                             select new
                             {
                                 entry.Id,
                                 entry.EventType,
                                 entry.ActorNameSnapshot,
                                 OldName = oldUser == null ? null : oldUser.FullName,
                                 NewName = newUser == null ? null : newUser.FullName,
                                 entry.OldPriority,
                                 entry.NewPriority,
                                 entry.CreatedAtUtc,
                                 entry.DelegationId,
                                 entry.Comment
                             }).ToListAsync(cancellationToken);
        return new(TicketResultStatus.Success, new(item, ticket.Description, ticket.RowVersion, state.Value!,
            history.Select(x => new TicketHistoryItem(x.Id, x.EventType.ToString(), x.ActorNameSnapshot, x.OldName, x.NewName,
                x.OldPriority?.ToString(), x.NewPriority?.ToString(), x.CreatedAtUtc, x.DelegationId, x.Comment)).ToArray()));
    }

    public Task<TicketResult<TicketDetail>> CreateAsync(CreateTicketRequest request, CancellationToken cancellationToken = default) =>
        CreateAsync(request, [], cancellationToken);

    public async Task<TicketResult<TicketDetail>> CreateAsync(CreateTicketRequest request, IReadOnlyList<AttachmentUpload> uploads, CancellationToken cancellationToken = default)
    {
        var keys = new List<string>();
        try
        {
            return await WriteAsync(async () =>
            {
                var actor = await WorkflowActorReader.ReadAsync(db, current.UserId, cancellationToken);
                if (actor?.Role != ApplicationRoles.Employee)
                    return Denied();
                if (string.IsNullOrWhiteSpace(request.Title) || request.Title.Length > 200
                    || string.IsNullOrWhiteSpace(request.Description) || request.Description.Length > 4000 || !Enum.IsDefined(request.Category))
                    return Invalid("Title (at most 200 characters), description (at most 4000 characters) and a valid category are required.");
                AttachmentContent.ValidateBatch(uploads);
                var now = DateTimeOffset.UtcNow;
                var ticket = new Ticket
                {
                    TicketNumber = Guid.NewGuid().ToString("N"),
                    Title = request.Title.Trim(),
                    Description = request.Description.Trim(),
                    Category = request.Category,
                    RequestedByUserId = actor.Id,
                    Priority = TicketPriority.Medium,
                    CreatedAtUtc = now,
                    UpdatedAtUtc = now
                };
                db.Tickets.Add(ticket);
                await db.SaveChangesAsync(cancellationToken);
                ticket.TicketNumber = TicketNumbers.Create(ticket.Id, now);
                await db.SaveChangesAsync(cancellationToken);
                var started = await workflow.StartAsync(ticket.Id, cancellationToken);
                if (started.Status != WorkflowResultStatus.Success)
                    return new(started.Status == WorkflowResultStatus.Forbidden ? TicketResultStatus.Forbidden : TicketResultStatus.Conflict, Error: started.Error);
                await files.AddAsync(ticket.Id, actor.Id, uploads, keys, cancellationToken);
                return await GetAsync(ticket.Id, cancellationToken);
            }, cancellationToken);
        }
        finally { await files.RemoveUnreferencedAsync(keys); }
    }

    public async Task<TicketResult<IReadOnlyList<TicketAssignee>>> GetAssigneesAsync(CancellationToken cancellationToken = default)
    {
        var actor = await WorkflowActorReader.ReadAsync(db, current.UserId, cancellationToken);
        if (actor is null || !IsAdmin(actor))
            return new(TicketResultStatus.Forbidden, Error: "Only admins can select assignees.");
        var users = await (from user in db.Users.AsNoTracking()
                           join membership in db.UserRoles on user.Id equals membership.UserId
                           join role in db.Roles on membership.RoleId equals role.Id
                           where user.IsActive && role.Name == ApplicationRoles.SupportITStaff
                           orderby user.FullName, user.Id
                           select new TicketAssignee(user.Id, user.FullName)).ToListAsync(cancellationToken);
        return new(TicketResultStatus.Success, users);
    }

    public Task<TicketResult<TicketDetail>> AssignAsync(long ticketId, AssignTicketRequest request, CancellationToken cancellationToken = default) =>
        UpdateAsync(ticketId, request.RowVersion, (ticket, actor) =>
            delegations.AssignAsync(ticket, request.AssignedToUserId, actor, cancellationToken), cancellationToken, lockAssignments: true);

    public Task<TicketResult<TicketDetail>> SetPriorityAsync(long ticketId, SetTicketPriorityRequest request, CancellationToken cancellationToken = default) =>
        UpdateAsync(ticketId, request.RowVersion, (ticket, actor) =>
        {
            if (!Enum.IsDefined(request.Priority))
                return Task.FromResult<string?>("Choose a valid priority.");
            if (ticket.Priority != request.Priority)
            {
                db.TicketHistories.Add(new TicketHistory
                {
                    TicketId = ticket.Id,
                    ActorUserId = actor.Id,
                    ActorNameSnapshot = actor.FullName,
                    EventType = TicketHistoryEventType.PriorityChanged,
                    OldPriority = ticket.Priority,
                    NewPriority = request.Priority
                });
                ticket.Priority = request.Priority;
                ticket.UpdatedAtUtc = DateTimeOffset.UtcNow;
            }
            return Task.FromResult<string?>(null);
        }, cancellationToken);

    private Task<TicketResult<TicketDetail>> UpdateAsync(long ticketId, byte[] version,
        Func<Ticket, UserAccount, Task<string?>> update, CancellationToken ct, bool lockAssignments = false) => WriteAsync(async () =>
    {
        if (lockAssignments)
            await DelegationLock.AcquireAsync(db, ct);
        var actor = await WorkflowActorReader.ReadAsync(db, current.UserId, ct);
        if (actor is null || !IsAdmin(actor))
            return Denied();
        if (version is not { Length: 8 })
            return Invalid("An 8-byte Ticket row version is required.");
        var ticket = await db.Tickets.FromSqlInterpolated($"SELECT * FROM [Tickets] WITH (UPDLOCK, HOLDLOCK) WHERE [Id] = {ticketId}").SingleOrDefaultAsync(ct);
        if (ticket is null)
            return Missing();
        await db.Entry(ticket).ReloadAsync(ct);
        if (!ticket.RowVersion.SequenceEqual(version))
            return new(TicketResultStatus.Conflict, Error: "This Ticket changed. Refresh and try again.");
        var error = await update(ticket, actor);
        if (error is not null)
            return Invalid(error);
        await db.SaveChangesAsync(ct);
        return await GetAsync(ticketId, ct);
    }, ct);

    private IQueryable<Ticket> Visible(UserAccount actor) => actor.Role switch
    {
        ApplicationRoles.Employee => db.Tickets.AsNoTracking().Where(x => x.RequestedByUserId == actor.Id),
        ApplicationRoles.SupportITStaff => db.Tickets.AsNoTracking().Where(x => x.AssignedToUserId == actor.Id),
        ApplicationRoles.SystemAdmin or ApplicationRoles.SupportAdmin => db.Tickets.AsNoTracking(),
        _ => db.Tickets.Where(_ => false)
    };

    private IQueryable<TicketListItem> Items(IQueryable<Ticket> tickets) =>
        from ticket in tickets
        join user in db.Users on ticket.RequestedByUserId equals user.Id
        join assignee in db.Users on ticket.AssignedToUserId equals assignee.Id into assignees
        from assignee in assignees.DefaultIfEmpty()
        join process in db.WorkflowProcessInstances on ticket.Id equals process.TicketId
        join step in db.WorkflowStepInstances on process.CurrentStepInstanceId equals step.Id
        select new TicketListItem
        {
            Id = ticket.Id,
            TicketNumber = ticket.TicketNumber,
            Title = ticket.Title,
            Category = ticket.Category.ToString(),
            Priority = ticket.Priority.ToString(),
            Status = step.Code,
            RequestedByUserId = ticket.RequestedByUserId,
            RequesterName = user.FullName,
            AssignedToUserId = ticket.AssignedToUserId,
            AssigneeName = assignee == null ? null : assignee.FullName,
            CreatedAtUtc = ticket.CreatedAtUtc,
            UpdatedAtUtc = ticket.UpdatedAtUtc
        };

    private Task<TicketResult<TicketDetail>> WriteAsync(Func<Task<TicketResult<TicketDetail>>> operation, CancellationToken ct) =>
        TicketWrites.RunAsync(db, operation, ct);

    private static bool IsAdmin(UserAccount actor) => actor.Role is ApplicationRoles.SystemAdmin or ApplicationRoles.SupportAdmin;
    private static TicketResult<TicketDetail> Missing() => new(TicketResultStatus.NotFound, Error: "Ticket was not found.");
    private static TicketResult<TicketDetail> Denied() => new(TicketResultStatus.Forbidden, Error: "Your role cannot perform this Ticket operation.");
    private static TicketResult<TicketDetail> Invalid(string error) => new(TicketResultStatus.Invalid, Error: error);
}

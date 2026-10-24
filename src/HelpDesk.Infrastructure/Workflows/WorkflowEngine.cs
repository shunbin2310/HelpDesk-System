using HelpDesk.Application.Authentication;
using HelpDesk.Application.Users;
using HelpDesk.Application.Workflows;
using HelpDesk.Domain.Constants;
using HelpDesk.Domain.Entities.Tickets;
using HelpDesk.Domain.Entities.Workflows;
using HelpDesk.Domain.Enums;
using HelpDesk.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HelpDesk.Infrastructure.Workflows;

public sealed class WorkflowEngine(HelpDeskDbContext db, ICurrentUserContext current) : IWorkflowEngine
{
    public Task<WorkflowResult<WorkflowSnapshot>> StartAsync(long ticketId, CancellationToken cancellationToken = default) =>
        WorkflowTransactions.RunAsync<WorkflowSnapshot>(db, async () =>
        {
            var actor = await WorkflowActorReader.ReadAsync(db, current.UserId, cancellationToken);
            var ticket = await LockTicketAsync(ticketId, cancellationToken);
            if (actor?.Role != ApplicationRoles.Employee || ticket?.RequestedByUserId != actor.Id)
                return Failed(WorkflowResultStatus.Forbidden, "Only the Employee requester can start a new Ticket workflow.");
            if (await db.WorkflowProcessInstances.AnyAsync(x => x.TicketId == ticketId, cancellationToken))
                return Failed(WorkflowResultStatus.Conflict, "This Ticket already has a workflow.");
            var template = await db.WorkflowProcessTemplates.AsNoTracking().SingleOrDefaultAsync(
                x => x.EntityType == TicketWorkflow.EntityType && x.IsPublished && x.IsActive, cancellationToken);
            if (template is null) return Failed(WorkflowResultStatus.Invalid, "No active published Ticket workflow is configured.");
            var steps = await db.WorkflowStepTemplates.AsNoTracking().Where(x => x.ProcessTemplateId == template.Id).ToListAsync(cancellationToken);
            var actions = await db.WorkflowActionTemplates.AsNoTracking().Where(x => x.ProcessTemplateId == template.Id).ToListAsync(cancellationToken);
            var ids = actions.Select(x => x.Id).ToArray();
            var actioners = await db.WorkflowActionerTemplates.AsNoTracking().Where(x => ids.Contains(x.ActionTemplateId)).ToListAsync(cancellationToken);
            var error = TicketWorkflowPolicy.ValidateTemplate(steps, actions, actioners);
            if (error is not null) return Failed(WorkflowResultStatus.Invalid, error);

            var process = new WorkflowProcessInstance
            {
                TicketId = ticketId, ProcessTemplateId = template.Id, TemplateCode = template.Code,
                TemplateName = template.Name, TemplateVersion = template.Version
            };
            db.WorkflowProcessInstances.Add(process);
            await db.SaveChangesAsync(cancellationToken);
            var copiedSteps = steps.ToDictionary(x => x.Id, x => new WorkflowStepInstance
            {
                ProcessInstanceId = process.Id, SourceStepTemplateId = x.Id, Code = x.Code, Name = x.Name,
                DisplayOrder = x.DisplayOrder, IsInitial = x.IsInitial, IsTerminal = x.IsTerminal
            });
            db.WorkflowStepInstances.AddRange(copiedSteps.Values);
            await db.SaveChangesAsync(cancellationToken);
            var copiedActions = actions.ToDictionary(x => x.Id, x => new WorkflowActionInstance
            {
                ProcessInstanceId = process.Id, SourceActionTemplateId = x.Id, Code = x.Code, Name = x.Name,
                FromStepInstanceId = copiedSteps[x.FromStepTemplateId].Id, ToStepInstanceId = copiedSteps[x.ToStepTemplateId].Id,
                RequiresComment = x.RequiresComment
            });
            db.WorkflowActionInstances.AddRange(copiedActions.Values);
            await db.SaveChangesAsync(cancellationToken);
            db.WorkflowActionerInstances.AddRange(actioners.Select(x => new WorkflowActionerInstance
            {
                ActionInstanceId = copiedActions[x.ActionTemplateId].Id, SourceActionerTemplateId = x.Id,
                ActionerType = x.ActionerType, ActionerRole = x.ActionerRole,
                ActionerUserId = x.ActionerType == WorkflowActionerType.Requester ? ticket.RequestedByUserId : x.ActionerUserId
            }));
            process.CurrentStepInstanceId = copiedSteps[steps.Single(x => x.IsInitial).Id].Id;
            db.WorkflowHistory.Add(new WorkflowHistory
            {
                ProcessInstanceId = process.Id, ToStepCode = TicketWorkflow.Open, ActionCode = "START",
                ActionByUserId = actor.Id, ActionByNameSnapshot = actor.FullName, ActionAtUtc = process.StartedAtUtc
            });
            await db.SaveChangesAsync(cancellationToken);
            return new(WorkflowResultStatus.Success, await SnapshotAsync(ticket, process, actor, cancellationToken));
        }, cancellationToken);

    public async Task<WorkflowResult<WorkflowSnapshot>> GetAsync(long ticketId, CancellationToken cancellationToken = default)
    {
        var actor = await WorkflowActorReader.ReadAsync(db, current.UserId, cancellationToken);
        var ticket = await db.Tickets.AsNoTracking().SingleOrDefaultAsync(x => x.Id == ticketId, cancellationToken);
        if (actor is null || ticket is null || !TicketWorkflowPolicy.CanView(ticket, actor.Id, actor.Role))
            return Failed(WorkflowResultStatus.NotFound, "Ticket workflow was not found.");
        var process = await db.WorkflowProcessInstances.AsNoTracking().SingleOrDefaultAsync(x => x.TicketId == ticketId, cancellationToken);
        return process is null ? Failed(WorkflowResultStatus.NotFound, "Ticket workflow was not found.")
            : new(WorkflowResultStatus.Success, await SnapshotAsync(ticket, process, actor, cancellationToken));
    }

    public Task<WorkflowResult<WorkflowSnapshot>> ExecuteAsync(long ticketId, ExecuteWorkflowAction request, CancellationToken cancellationToken = default) =>
        WorkflowTransactions.RunAsync<WorkflowSnapshot>(db, async () =>
        {
            var actor = await WorkflowActorReader.ReadAsync(db, current.UserId, cancellationToken);
            var ticket = await LockTicketAsync(ticketId, cancellationToken);
            if (actor is null || ticket is null || !TicketWorkflowPolicy.CanView(ticket, actor.Id, actor.Role))
                return Failed(WorkflowResultStatus.NotFound, "Ticket workflow was not found.");
            if (string.IsNullOrWhiteSpace(request.ActionCode) || request.ActionCode.Length > 64
                || request.RowVersion is not { Length: 8 } || request.Comment?.Length > 4000)
                return Failed(WorkflowResultStatus.Invalid, "Provide an action code and an 8-byte workflow row version. Comments must be at most 4000 characters.");
            var process = await db.WorkflowProcessInstances.SingleOrDefaultAsync(x => x.TicketId == ticketId, cancellationToken);
            if (process is null) return Failed(WorkflowResultStatus.NotFound, "Ticket workflow was not found.");
            await db.Entry(process).ReloadAsync(cancellationToken);
            if (!process.RowVersion.SequenceEqual(request.RowVersion))
                return Failed(WorkflowResultStatus.Conflict, "The workflow changed. Refresh before submitting another action.");
            if (process.Status == WorkflowInstanceStatus.Completed)
                return Failed(WorkflowResultStatus.Conflict, "The workflow is already closed.");
            var from = await db.WorkflowStepInstances.AsNoTracking().SingleAsync(x => x.Id == process.CurrentStepInstanceId, cancellationToken);
            var code = request.ActionCode.Trim().ToUpperInvariant();
            var action = await db.WorkflowActionInstances.AsNoTracking().SingleOrDefaultAsync(
                x => x.ProcessInstanceId == process.Id && x.FromStepInstanceId == from.Id && x.Code == code, cancellationToken);
            if (action is null) return Failed(WorkflowResultStatus.Conflict, "This action is not available from the current step.");
            var to = await db.WorkflowStepInstances.AsNoTracking().SingleAsync(x => x.Id == action.ToStepInstanceId, cancellationToken);
            if (!TicketWorkflowPolicy.IsTransition(from.Code, action.Code, to.Code))
                return Failed(WorkflowResultStatus.Invalid, "The configured action violates Ticket state rules.");
            var actioners = await db.WorkflowActionerInstances.AsNoTracking().Where(x => x.ActionInstanceId == action.Id).ToListAsync(cancellationToken);
            if (!TicketWorkflowPolicy.CanExecute(ticket, actor.Id, actor.Role, code, actioners))
                return Failed(WorkflowResultStatus.Forbidden, "You cannot perform this Ticket action.");
            var comment = string.IsNullOrWhiteSpace(request.Comment) ? null : request.Comment.Trim();
            if (action.RequiresComment && comment is null) return Failed(WorkflowResultStatus.Invalid, "A comment is required for this action.");
            var now = DateTimeOffset.UtcNow;
            process.CurrentStepInstanceId = to.Id;
            process.Status = to.Code == TicketWorkflow.Closed ? WorkflowInstanceStatus.Completed : WorkflowInstanceStatus.Running;
            process.CompletedAtUtc = process.Status == WorkflowInstanceStatus.Completed ? now : null;
            ticket.UpdatedAtUtc = now;
            db.WorkflowHistory.Add(new WorkflowHistory
            {
                ProcessInstanceId = process.Id, ActionInstanceId = action.Id, FromStepCode = from.Code, ToStepCode = to.Code,
                ActionCode = code, ActionByUserId = actor.Id, ActionByNameSnapshot = actor.FullName, Comment = comment, ActionAtUtc = now
            });
            await db.SaveChangesAsync(cancellationToken);
            return new(WorkflowResultStatus.Success, await SnapshotAsync(ticket, process, actor, cancellationToken));
        }, cancellationToken);

    private async Task<Ticket?> LockTicketAsync(long ticketId, CancellationToken ct)
    {
        // Hold the Ticket row until commit so assignment cannot change between authorization and execution.
        if (db.ChangeTracker.Entries<Ticket>().Any(x => x.Entity.Id == ticketId && x.State != EntityState.Unchanged))
            throw new InvalidOperationException("Save Ticket changes before calling the workflow engine.");
        var ticket = await db.Tickets.FromSqlInterpolated($"SELECT * FROM [Tickets] WITH (UPDLOCK, HOLDLOCK) WHERE [Id] = {ticketId}")
            .SingleOrDefaultAsync(ct);
        if (ticket is not null) await db.Entry(ticket).ReloadAsync(ct);
        return ticket;
    }

    private async Task<WorkflowSnapshot> SnapshotAsync(Ticket ticket, WorkflowProcessInstance process, UserAccount actor, CancellationToken ct)
    {
        var steps = await db.WorkflowStepInstances.AsNoTracking().Where(x => x.ProcessInstanceId == process.Id).ToDictionaryAsync(x => x.Id, ct);
        var step = steps[process.CurrentStepInstanceId!.Value];
        var actions = await db.WorkflowActionInstances.AsNoTracking().Where(x => x.ProcessInstanceId == process.Id && x.FromStepInstanceId == step.Id).OrderBy(x => x.Id).ToListAsync(ct);
        var ids = actions.Select(x => x.Id).ToArray();
        var rules = await db.WorkflowActionerInstances.AsNoTracking().Where(x => ids.Contains(x.ActionInstanceId)).ToListAsync(ct);
        var available = process.Status == WorkflowInstanceStatus.Completed ? [] : actions
            .Where(x => TicketWorkflowPolicy.IsTransition(step.Code, x.Code, steps[x.ToStepInstanceId].Code)
                && TicketWorkflowPolicy.CanExecute(ticket, actor.Id, actor.Role, x.Code, rules.Where(a => a.ActionInstanceId == x.Id)))
            .Select(x => new WorkflowActionOption(x.Code, x.Name, x.RequiresComment)).ToArray();
        var history = await db.WorkflowHistory.AsNoTracking().Where(x => x.ProcessInstanceId == process.Id)
            .OrderBy(x => x.ActionAtUtc).ThenBy(x => x.Id).Select(x => new WorkflowHistoryItem(x.FromStepCode, x.ToStepCode,
                x.ActionCode, x.ActionByUserId, x.ActionByNameSnapshot, x.Comment, x.ActionAtUtc)).ToListAsync(ct);
        return new(process.Id, ticket.Id, process.TemplateCode, process.TemplateVersion, step.Code,
            process.Status.ToString(), process.RowVersion, available, history);
    }

    private static WorkflowResult<WorkflowSnapshot> Failed(WorkflowResultStatus status, string error) => new(status, Error: error);
}

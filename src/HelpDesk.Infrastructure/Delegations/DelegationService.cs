using HelpDesk.Application.Authentication;
using HelpDesk.Application.Delegations;
using HelpDesk.Application.Tickets;
using HelpDesk.Domain.Constants;
using HelpDesk.Domain.Entities.Delegations;
using HelpDesk.Domain.Enums;
using HelpDesk.Infrastructure.Persistence;
using HelpDesk.Infrastructure.Tickets;
using HelpDesk.Infrastructure.Workflows;
using Microsoft.EntityFrameworkCore;

namespace HelpDesk.Infrastructure.Delegations;

public sealed class DelegationService(HelpDeskDbContext db, ICurrentUserContext current, TimeProvider clock,
    DelegationProcessor processor) : IDelegationService
{
    public async Task<TicketResult<DelegationPage>> ListAsync(int page, CancellationToken cancellationToken = default)
    {
        if (!await IsAdminAsync(cancellationToken))
            return Denied<DelegationPage>();
        page = Math.Clamp(page, 1, 1000000);
        var ids = await db.Delegations.AsNoTracking().OrderByDescending(x => x.CreatedAtUtc).ThenByDescending(x => x.Id)
            .Skip((page - 1) * 25).Take(25).Select(x => x.Id).ToListAsync(cancellationToken);
        var items = new List<DelegationItem>();
        foreach (var id in ids)
            items.Add(await ItemAsync(id, cancellationToken));
        return new(TicketResultStatus.Success, new(items, await db.Delegations.CountAsync(cancellationToken), page));
    }

    public async Task<TicketResult<DelegationDetail>> GetAsync(long id, int page, CancellationToken cancellationToken = default)
    {
        if (!await IsAdminAsync(cancellationToken))
            return Denied<DelegationDetail>();
        if (!await db.Delegations.AnyAsync(x => x.Id == id, cancellationToken))
            return new(TicketResultStatus.NotFound, Error: "Delegation was not found.");
        page = Math.Clamp(page, 1, 1000000);
        var query = from entry in db.DelegationTickets.AsNoTracking()
                    join ticket in db.Tickets on entry.TicketId equals ticket.Id
                    join user in db.Users on ticket.AssignedToUserId equals user.Id into users
                    from user in users.DefaultIfEmpty()
                    join process in db.WorkflowProcessInstances on ticket.Id equals process.TicketId
                    join step in db.WorkflowStepInstances on process.CurrentStepInstanceId equals step.Id
                    where entry.DelegationId == id
                    orderby entry.DelegatedAtUtc, entry.Id
                    select new DelegationTicketItem(ticket.Id, ticket.TicketNumber, ticket.Title, step.Code,
                        user == null ? null : user.FullName, entry.EntryReason.ToString(), entry.ReturnOutcome.ToString(), entry.DelegatedAtUtc, entry.ProcessedAtUtc);
        return new(TicketResultStatus.Success, new(await ItemAsync(id, cancellationToken),
            await query.Skip((page - 1) * 25).Take(25).ToListAsync(cancellationToken), await query.CountAsync(cancellationToken), page));
    }

    public Task<TicketResult<DelegationItem>> CreateAsync(CreateDelegation request, CancellationToken cancellationToken = default) =>
        TicketWrites.RunAsync<DelegationItem>(db, async () =>
        {
            await DelegationLock.AcquireAsync(db, cancellationToken);
            var actor = await WorkflowActorReader.ReadAsync(db, current.UserId, cancellationToken);
            if (actor?.Role is not (ApplicationRoles.SystemAdmin or ApplicationRoles.SupportAdmin))
                return Denied<DelegationItem>();
            var now = clock.GetUtcNow();
            var malaysiaOffset = TimeSpan.FromHours(8);
            var today = DateOnly.FromDateTime(now.ToOffset(malaysiaOffset).DateTime);
            if (request.StartDate < today)
                return new(TicketResultStatus.Invalid, Error: "Start date cannot be before today in Malaysia (UTC+8).");
            if (request.EndDate < request.StartDate || request.EndDate == DateOnly.MaxValue)
                return new(TicketResultStatus.Invalid, Error: "End date must be on or after the start date and before 9999-12-31.");
            if (request.SourceUserId == request.TargetUserId || request.Reason?.Length > 1000)
                return new(TicketResultStatus.Invalid, Error: "Choose two different IT Staff. Reason must be at most 1000 characters.");
            var starts = request.StartDate == today ? now
                : new DateTimeOffset(request.StartDate.ToDateTime(TimeOnly.MinValue), malaysiaOffset).ToUniversalTime();
            var ends = new DateTimeOffset(request.EndDate.AddDays(1).ToDateTime(TimeOnly.MinValue), malaysiaOffset).ToUniversalTime();
            if (!await processor.IsStaffAsync(request.SourceUserId, cancellationToken) || !await processor.IsStaffAsync(request.TargetUserId, cancellationToken))
                return new(TicketResultStatus.Invalid, Error: "Both participants must be active Support IT Staff.");
            if (await db.Delegations.AnyAsync(x => x.CompletedAtUtc == null && x.StartsAtUtc < ends && (x.StoppedAtUtc ?? x.EndsAtUtc) > starts
                && (x.SourceUserId == request.SourceUserId || x.TargetUserId == request.SourceUserId
                    || x.SourceUserId == request.TargetUserId || x.TargetUserId == request.TargetUserId), cancellationToken))
                return new(TicketResultStatus.Conflict, Error: "A participant already has a delegation in this period. Overlapping and chained delegations are not supported.");
            var delegation = new Delegation
            {
                SourceUserId = request.SourceUserId,
                TargetUserId = request.TargetUserId,
                StartsAtUtc = starts,
                EndsAtUtc = ends,
                Reason = string.IsNullOrWhiteSpace(request.Reason) ? null : request.Reason.Trim(),
                CreatedByUserId = actor.Id,
                CreatedAtUtc = now,
                UpdatedAtUtc = now
            };
            db.Delegations.Add(delegation);
            await db.SaveChangesAsync(cancellationToken);
            await processor.ApplyAsync(delegation, now, cancellationToken);
            return new(TicketResultStatus.Success, await ItemAsync(delegation.Id, cancellationToken));
        }, cancellationToken);

    public Task<TicketResult<DelegationItem>> CancelAsync(long id, StopDelegation request, CancellationToken cancellationToken = default) =>
        StopAsync(id, request, cancelScheduled: true, cancellationToken);

    public Task<TicketResult<DelegationItem>> EndEarlyAsync(long id, StopDelegation request, CancellationToken cancellationToken = default) =>
        StopAsync(id, request, cancelScheduled: false, cancellationToken);

    private Task<TicketResult<DelegationItem>> StopAsync(long id, StopDelegation request, bool cancelScheduled, CancellationToken ct) =>
        TicketWrites.RunAsync<DelegationItem>(db, async () =>
        {
            await DelegationLock.AcquireAsync(db, ct);
            var actor = await WorkflowActorReader.ReadAsync(db, current.UserId, ct);
            if (actor?.Role is not (ApplicationRoles.SystemAdmin or ApplicationRoles.SupportAdmin))
                return Denied<DelegationItem>();
            if (request.RowVersion is not { Length: 8 })
                return new(TicketResultStatus.Invalid, Error: "An 8-byte delegation row version is required.");
            var delegation = await db.Delegations.SingleOrDefaultAsync(x => x.Id == id, ct);
            if (delegation is null)
                return new(TicketResultStatus.NotFound, Error: "Delegation was not found.");
            await db.Entry(delegation).ReloadAsync(ct);
            if (delegation.StoppedAtUtc.HasValue)
            {
                if (cancelScheduled == (delegation.StoppedAtUtc < delegation.StartsAtUtc))
                    return new(TicketResultStatus.Success, await ItemAsync(id, ct));
                return new(TicketResultStatus.Conflict, Error: "This delegation has already been stopped. Refresh to see the result.");
            }
            if (!delegation.RowVersion.SequenceEqual(request.RowVersion))
                return new(TicketResultStatus.Conflict, Error: "This delegation changed. Refresh before trying again.");
            var now = clock.GetUtcNow();
            if (delegation.CompletedAtUtc.HasValue || now >= delegation.EndsAtUtc
                || (cancelScheduled ? now >= delegation.StartsAtUtc : now < delegation.StartsAtUtc))
                return new(TicketResultStatus.Conflict, Error: cancelScheduled
                    ? "Only a delegation that has not started can be cancelled. Refresh to see its current state."
                    : "Only a delegation within its active period can end early. Refresh to see its current state.");
            if (cancelScheduled && (delegation.ActivatedAtUtc.HasValue || await db.DelegationTickets.AnyAsync(x => x.DelegationId == id, ct)))
                return new(TicketResultStatus.Conflict, Error: "Tickets have already been handed over. End the delegation early instead.");
            delegation.StoppedAtUtc = now;
            delegation.StoppedByUserId = actor.Id;
            delegation.UpdatedAtUtc = now;
            if (cancelScheduled)
                delegation.CompletedAtUtc = now;
            await db.SaveChangesAsync(ct);
            if (!cancelScheduled)
                await processor.ApplyAsync(delegation, now, ct);
            return new(TicketResultStatus.Success, await ItemAsync(id, ct));
        }, ct);

    private async Task<DelegationItem> ItemAsync(long id, CancellationToken ct)
    {
        var row = await (from delegation in db.Delegations.AsNoTracking()
                         join source in db.Users on delegation.SourceUserId equals source.Id
                         join target in db.Users on delegation.TargetUserId equals target.Id
                         join stoppedBy in db.Users on delegation.StoppedByUserId equals stoppedBy.Id into stoppedUsers
                         from stoppedBy in stoppedUsers.DefaultIfEmpty()
                         where delegation.Id == id
                         select new
                         {
                             Delegation = delegation,
                             Source = source.FullName,
                             Target = target.FullName,
                             StoppedByName = stoppedBy == null ? null : stoppedBy.FullName
                         }).SingleAsync(ct);
        var d = row.Delegation;
        var now = clock.GetUtcNow();
        var counts = await db.DelegationTickets.Where(x => x.DelegationId == id).GroupBy(x => x.ReturnOutcome)
            .Select(x => new { Outcome = x.Key, Count = x.Count() }).ToDictionaryAsync(x => x.Outcome, x => x.Count, ct);
        var pending = counts.GetValueOrDefault(DelegationReturnOutcome.AwaitingEnd);
        var effectiveEnd = d.StoppedAtUtc ?? d.EndsAtUtc;
        var status = d.CompletedAtUtc.HasValue
            ? d.StoppedAtUtc.HasValue ? d.StoppedAtUtc < d.StartsAtUtc ? "Cancelled" : "EndedEarly" : "Completed"
            : now < d.StartsAtUtc ? "Scheduled" : now >= effectiveEnd ? "Returning" : d.ActivatedAtUtc.HasValue ? "Active" : "AwaitingActivation";
        string? issue = null;
        if (!d.CompletedAtUtc.HasValue)
        {
            if (!await processor.IsStaffAsync(d.SourceUserId, ct) && (d.ActivatedAtUtc is null || (now >= effectiveEnd && pending > 0)))
                issue = "The original staff account is inactive or no longer IT Staff. Restore it to resume processing, or manually reassign affected tickets.";
            else if (now < effectiveEnd && !await processor.IsStaffAsync(d.TargetUserId, ct))
                issue = "The delegate is inactive or no longer IT Staff. New handovers are paused; restore the account or assign tickets to another staff member.";
            else if (now < effectiveEnd && d.ActivatedAtUtc is null && await processor.HasUnfinishedPredecessorAsync(d, ct))
                issue = "An earlier delegation involving these staff members is still awaiting return.";
            if (issue is not null)
                status = "NeedsAttention";
        }
        return new(id, d.SourceUserId, row.Source, d.TargetUserId, row.Target, d.StartsAtUtc, d.EndsAtUtc, d.Reason, status, issue,
            counts.Values.Sum(), pending, counts.GetValueOrDefault(DelegationReturnOutcome.Returned),
            counts.GetValueOrDefault(DelegationReturnOutcome.SkippedClosed), counts.GetValueOrDefault(DelegationReturnOutcome.SkippedReassigned),
            d.StoppedAtUtc, row.StoppedByName,
            d.StoppedAtUtc is null && d.CompletedAtUtc is null && now < d.StartsAtUtc,
            d.StoppedAtUtc is null && d.CompletedAtUtc is null && now >= d.StartsAtUtc && now < d.EndsAtUtc, d.RowVersion);
    }

    private async Task<bool> IsAdminAsync(CancellationToken ct) =>
        (await WorkflowActorReader.ReadAsync(db, current.UserId, ct))?.Role is ApplicationRoles.SystemAdmin or ApplicationRoles.SupportAdmin;
    private static TicketResult<T> Denied<T>() => new(TicketResultStatus.Forbidden, Error: "Only Support Admin and System Admin can manage delegations.");
}

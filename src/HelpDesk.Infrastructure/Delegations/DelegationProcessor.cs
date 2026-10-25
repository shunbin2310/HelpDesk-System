using HelpDesk.Application.Tickets;
using HelpDesk.Application.Users;
using HelpDesk.Domain.Constants;
using HelpDesk.Domain.Entities.Delegations;
using HelpDesk.Domain.Entities.Tickets;
using HelpDesk.Domain.Enums;
using HelpDesk.Infrastructure.Persistence;
using HelpDesk.Infrastructure.Tickets;
using HelpDesk.Infrastructure.Workflows;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HelpDesk.Infrastructure.Delegations;

public sealed class DelegationProcessor(HelpDeskDbContext db, TimeProvider clock, ILogger<DelegationProcessor> logger)
{
    public async Task ProcessDueAsync(CancellationToken ct = default)
    {
        var now = clock.GetUtcNow();
        var ids = await db.Delegations.AsNoTracking().Where(x => x.CompletedAtUtc == null && (x.StartsAtUtc <= now || x.StoppedAtUtc != null))
            .OrderBy(x => x.EndsAtUtc).ThenBy(x => x.Id).Select(x => x.Id).ToListAsync(ct);
        foreach (var id in ids)
        {
            try
            {
                await ProcessAsync(id, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception exception) { logger.LogWarning("Delegation {DelegationId} will retry ({ErrorType}).", id, exception.GetType().Name); }
        }
    }

    public async Task ProcessAsync(long id, CancellationToken ct = default) =>
        _ = await TicketWrites.RunAsync<int>(db, async () =>
        {
            await DelegationLock.AcquireAsync(db, ct);
            var delegation = await db.Delegations.SingleOrDefaultAsync(x => x.Id == id, ct);
            if (delegation is not null)
            {
                await db.Entry(delegation).ReloadAsync(ct);
                await ApplyAsync(delegation, clock.GetUtcNow(), ct);
            }
            return new(TicketResultStatus.Success, 0);
        }, ct);

    internal async Task ApplyAsync(Delegation delegation, DateTimeOffset now, CancellationToken ct)
    {
        if (delegation.CompletedAtUtc.HasValue || (delegation.StartsAtUtc > now && delegation.StoppedAtUtc is null))
            return;
        if (delegation.StoppedAtUtc.HasValue || delegation.EndsAtUtc <= now)
        {
            var entries = await db.DelegationTickets.Where(x => x.DelegationId == delegation.Id
                && x.ReturnOutcome == DelegationReturnOutcome.AwaitingEnd).OrderBy(x => x.TicketId).ToListAsync(ct);
            var sourceAvailable = await IsStaffAsync(delegation.SourceUserId, ct);
            foreach (var entry in entries)
            {
                await db.Entry(entry).ReloadAsync(ct);
                if (entry.ReturnOutcome != DelegationReturnOutcome.AwaitingEnd)
                    continue;
                var ticket = await LockTicketAsync(entry.TicketId, ct);
                var step = await StepAsync(ticket.Id, ct);
                if (step == TicketWorkflow.Closed)
                    entry.ReturnOutcome = DelegationReturnOutcome.SkippedClosed;
                else if (ticket.AssignedToUserId != entry.DelegatedToUserId || ticket.AssignmentVersion != entry.DelegatedAssignmentVersion)
                    entry.ReturnOutcome = DelegationReturnOutcome.SkippedReassigned;
                else if (sourceAvailable && step is TicketWorkflow.Open or TicketWorkflow.InProgress or TicketWorkflow.Resolved)
                {
                    History(ticket, delegation, TicketHistoryEventType.DelegationReturned, delegation.SourceUserId, null, "System",
                        delegation.StoppedAtUtc.HasValue ? "Delegation ended early; returned to the original IT Staff."
                            : "Delegation ended; returned to the original IT Staff.", now);
                    entry.ReturnOutcome = DelegationReturnOutcome.Returned;
                }
                if (entry.ReturnOutcome != DelegationReturnOutcome.AwaitingEnd)
                    entry.ProcessedAtUtc = now;
            }
            if (entries.All(x => x.ReturnOutcome != DelegationReturnOutcome.AwaitingEnd))
                delegation.CompletedAtUtc = now;
            delegation.UpdatedAtUtc = now;
        }
        else if (delegation.ActivatedAtUtc is null)
        {
            if (!await IsStaffAsync(delegation.SourceUserId, ct) || !await IsStaffAsync(delegation.TargetUserId, ct)
                || await HasUnfinishedPredecessorAsync(delegation, ct))
                return;
            var ids = await (from ticket in db.Tickets
                             join process in db.WorkflowProcessInstances on ticket.Id equals process.TicketId
                             join step in db.WorkflowStepInstances on process.CurrentStepInstanceId equals step.Id
                             where ticket.AssignedToUserId == delegation.SourceUserId && (step.Code == TicketWorkflow.Open || step.Code == TicketWorkflow.InProgress)
                             orderby ticket.Id
                             select ticket.Id).ToListAsync(ct);
            foreach (var id in ids)
            {
                var ticket = await LockTicketAsync(id, ct);
                var step = await StepAsync(id, ct);
                if (ticket.AssignedToUserId == delegation.SourceUserId && step is TicketWorkflow.Open or TicketWorkflow.InProgress)
                    await DelegateAsync(ticket, delegation, DelegationEntryReason.ExistingTicket, null, "System", now, ct);
            }
            delegation.ActivatedAtUtc = now;
            delegation.UpdatedAtUtc = now;
        }
        await db.SaveChangesAsync(ct);
    }

    internal async Task<string?> AssignAsync(Ticket ticket, int intendedUserId, UserAccount actor, CancellationToken ct)
    {
        if (!await IsStaffAsync(intendedUserId, ct))
            return "Choose an active Support IT Staff account.";
        var now = clock.GetUtcNow();
        var delegation = await db.Delegations.SingleOrDefaultAsync(x => x.SourceUserId == intendedUserId
            && x.CompletedAtUtc == null && x.StoppedAtUtc == null && x.StartsAtUtc <= now && x.EndsAtUtc > now, ct);
        if (delegation is not null && await StepAsync(ticket.Id, ct) != TicketWorkflow.Closed)
        {
            if (!await IsStaffAsync(delegation.TargetUserId, ct))
                return "The delegate is unavailable. Restore their active IT Staff account or choose another assignee.";
            if (await HasUnfinishedPredecessorAsync(delegation, ct))
                return "An earlier delegation is awaiting return. Review Delegations before assigning this staff member.";
            await DelegateAsync(ticket, delegation, DelegationEntryReason.NewAssignment, actor.Id, actor.FullName, now, ct);
            return null;
        }
        if (ticket.AssignedToUserId != intendedUserId)
            History(ticket, null, ticket.AssignedToUserId.HasValue ? TicketHistoryEventType.Reassigned : TicketHistoryEventType.Assigned,
                intendedUserId, actor.Id, actor.FullName, null, now);
        return null;
    }

    private async Task DelegateAsync(Ticket ticket, Delegation delegation, DelegationEntryReason reason,
        int? actorId, string actorName, DateTimeOffset now, CancellationToken ct)
    {
        var entry = await db.DelegationTickets.SingleOrDefaultAsync(x => x.DelegationId == delegation.Id && x.TicketId == ticket.Id, ct);
        if (entry is not null)
            await db.Entry(entry).ReloadAsync(ct);
        if (ticket.AssignedToUserId == delegation.TargetUserId && entry?.ReturnOutcome == DelegationReturnOutcome.AwaitingEnd
            && entry.DelegatedAssignmentVersion == ticket.AssignmentVersion)
            return;
        var sourceName = await db.Users.Where(x => x.Id == delegation.SourceUserId).Select(x => x.FullName).SingleAsync(ct);
        History(ticket, delegation, TicketHistoryEventType.Delegated, delegation.TargetUserId, actorId, actorName,
            $"Temporary cover for {sourceName}.", now);
        if (entry is null)
        {
            entry = new DelegationTicket { DelegationId = delegation.Id, TicketId = ticket.Id };
            db.DelegationTickets.Add(entry);
        }
        entry.EntryReason = reason;
        entry.OriginalAssigneeUserId = delegation.SourceUserId;
        entry.DelegatedToUserId = delegation.TargetUserId;
        entry.DelegatedAssignmentVersion = ticket.AssignmentVersion;
        entry.DelegatedAtUtc = now;
        entry.ReturnOutcome = DelegationReturnOutcome.AwaitingEnd;
        entry.ProcessedAtUtc = null;
    }

    internal Task<bool> HasUnfinishedPredecessorAsync(Delegation delegation, CancellationToken ct) => db.Delegations.AnyAsync(x =>
        x.Id != delegation.Id && x.CompletedAtUtc == null && x.StartsAtUtc < delegation.StartsAtUtc
        && (x.ActivatedAtUtc != null || db.DelegationTickets.Any(t => t.DelegationId == x.Id && t.ReturnOutcome == DelegationReturnOutcome.AwaitingEnd))
        && (x.SourceUserId == delegation.SourceUserId || x.TargetUserId == delegation.SourceUserId
            || x.SourceUserId == delegation.TargetUserId || x.TargetUserId == delegation.TargetUserId), ct);

    internal async Task<bool> IsStaffAsync(int id, CancellationToken ct) =>
        (await WorkflowActorReader.ReadAsync(db, id, ct)) is { IsActive: true, Role: ApplicationRoles.SupportITStaff };

    private async Task<Ticket> LockTicketAsync(long id, CancellationToken ct)
    {
        var ticket = await db.Tickets.FromSqlInterpolated($"SELECT * FROM [Tickets] WITH (UPDLOCK, HOLDLOCK) WHERE [Id] = {id}").SingleAsync(ct);
        await db.Entry(ticket).ReloadAsync(ct);
        return ticket;
    }

    private Task<string?> StepAsync(long id, CancellationToken ct) => (from process in db.WorkflowProcessInstances
                                                                       join step in db.WorkflowStepInstances on process.CurrentStepInstanceId equals step.Id
                                                                       where process.TicketId == id
                                                                       select step.Code).SingleOrDefaultAsync(ct);

    private void History(Ticket ticket, Delegation? delegation, TicketHistoryEventType type, int targetId,
        int? actorId, string actorName, string? comment, DateTimeOffset now)
    {
        db.TicketHistories.Add(new TicketHistory
        {
            TicketId = ticket.Id,
            EventType = type,
            ActorUserId = actorId,
            ActorNameSnapshot = actorName,
            OldAssigneeUserId = ticket.AssignedToUserId,
            NewAssigneeUserId = targetId,
            DelegationId = delegation?.Id,
            Comment = comment,
            CreatedAtUtc = now
        });
        ticket.AssignedToUserId = targetId;
        ticket.AssignmentVersion++;
        ticket.UpdatedAtUtc = now;
    }
}

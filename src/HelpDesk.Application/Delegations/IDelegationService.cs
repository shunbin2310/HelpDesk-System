using HelpDesk.Application.Tickets;

namespace HelpDesk.Application.Delegations;

public sealed record CreateDelegation(int SourceUserId, int TargetUserId, DateOnly StartDate, DateOnly EndDate, string? Reason);
public sealed record StopDelegation(byte[] RowVersion);
public sealed record DelegationItem(long Id, int SourceUserId, string SourceName, int TargetUserId, string TargetName,
    DateTimeOffset StartsAtUtc, DateTimeOffset EndsAtUtc, string? Reason, string Status, string? Issue,
    int TotalTickets, int PendingTickets, int ReturnedTickets, int SkippedClosedTickets, int SkippedReassignedTickets,
    DateTimeOffset? StoppedAtUtc, string? StoppedByName, bool CanCancel, bool CanEndEarly, byte[] RowVersion);
public sealed record DelegationPage(IReadOnlyList<DelegationItem> Items, int Total, int Page);
public sealed record DelegationTicketItem(long TicketId, string TicketNumber, string Title, string Status,
    string? CurrentAssigneeName, string EntryReason, string ReturnOutcome, DateTimeOffset DelegatedAtUtc, DateTimeOffset? ProcessedAtUtc);
public sealed record DelegationDetail(DelegationItem Delegation, IReadOnlyList<DelegationTicketItem> Tickets, int Total, int Page);

public interface IDelegationService
{
    Task<TicketResult<DelegationPage>> ListAsync(int page, CancellationToken cancellationToken = default);
    Task<TicketResult<DelegationDetail>> GetAsync(long id, int page, CancellationToken cancellationToken = default);
    Task<TicketResult<DelegationItem>> CreateAsync(CreateDelegation request, CancellationToken cancellationToken = default);
    Task<TicketResult<DelegationItem>> CancelAsync(long id, StopDelegation request, CancellationToken cancellationToken = default);
    Task<TicketResult<DelegationItem>> EndEarlyAsync(long id, StopDelegation request, CancellationToken cancellationToken = default);
}

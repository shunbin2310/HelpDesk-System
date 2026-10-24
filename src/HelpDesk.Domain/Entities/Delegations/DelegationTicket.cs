using HelpDesk.Domain.Common;
using HelpDesk.Domain.Enums;

namespace HelpDesk.Domain.Entities.Delegations;

public sealed class DelegationTicket : Entity
{
    public long DelegationId { get; set; }
    public long TicketId { get; set; }
    public DelegationEntryReason EntryReason { get; set; }
    public int OriginalAssigneeUserId { get; set; }
    public int DelegatedToUserId { get; set; }
    public long DelegatedAssignmentVersion { get; set; }
    public DateTimeOffset DelegatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DelegationReturnOutcome ReturnOutcome { get; set; } = DelegationReturnOutcome.AwaitingEnd;
    public DateTimeOffset? ProcessedAtUtc { get; set; }
}

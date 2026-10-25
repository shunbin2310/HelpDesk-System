using HelpDesk.Domain.Common;
using HelpDesk.Domain.Enums;

namespace HelpDesk.Domain.Entities.Tickets;

public sealed class TicketHistory : Entity
{
    public long TicketId
    {
        get; set;
    }
    public TicketHistoryEventType EventType
    {
        get; set;
    }
    public int? ActorUserId
    {
        get; set;
    }
    public string ActorNameSnapshot { get; set; } = string.Empty;
    public int? OldAssigneeUserId
    {
        get; set;
    }
    public int? NewAssigneeUserId
    {
        get; set;
    }
    public TicketPriority? OldPriority
    {
        get; set;
    }
    public TicketPriority? NewPriority
    {
        get; set;
    }
    public long? DelegationId
    {
        get; set;
    }
    public string? Comment
    {
        get; set;
    }
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}

using HelpDesk.Domain.Common;
using HelpDesk.Domain.Enums;

namespace HelpDesk.Domain.Entities.Tickets;

public sealed class Ticket : VersionedEntity
{
    public string TicketNumber { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public TicketCategory Category { get; set; }
    public TicketPriority Priority { get; set; } = TicketPriority.Medium;
    public int RequestedByUserId { get; set; }
    public int? AssignedToUserId { get; set; }
    public long AssignmentVersion { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}

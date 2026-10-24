using HelpDesk.Domain.Common;
using HelpDesk.Domain.Enums;

namespace HelpDesk.Domain.Entities.Tickets;

public sealed class TicketMessage : Entity
{
    public long TicketId { get; set; }
    public int AuthorUserId { get; set; }
    public TicketMessageType MessageType { get; set; }
    public string Body { get; set; } = string.Empty;
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}

using HelpDesk.Domain.Common;
using HelpDesk.Domain.Enums;

namespace HelpDesk.Domain.Entities.Tickets;

public sealed class Attachment : Entity
{
    public long TicketId
    {
        get; set;
    }
    public long? TicketMessageId
    {
        get; set;
    }
    public int UploadedByUserId
    {
        get; set;
    }
    public string OriginalFileName { get; set; } = string.Empty;
    public string StorageKey { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public long SizeBytes
    {
        get; set;
    }
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}

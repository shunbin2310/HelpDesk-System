namespace HelpDesk.Application.Tickets;

public sealed record AttachmentUpload(string FileName, long Length, Func<Stream> OpenRead);
public sealed record StoredAttachment(string StorageKey, string FileName, string ContentType, long SizeBytes);
public sealed record AttachmentItem(long Id, string FileName, string ContentType, long SizeBytes);
public sealed record AttachmentDownload(Stream Content, string FileName, string ContentType);

public interface ITicketAttachmentService
{
    Task<TicketResult<IReadOnlyList<AttachmentItem>>> ListAsync(long ticketId, CancellationToken cancellationToken = default);
    Task<TicketResult<AttachmentDownload>> DownloadAsync(long ticketId, long attachmentId, CancellationToken cancellationToken = default);
}

public interface IAttachmentStore
{
    Task<StoredAttachment> SaveAsync(AttachmentUpload upload, CancellationToken cancellationToken = default);
    Stream OpenRead(string storageKey);
    void Delete(string storageKey);
    IEnumerable<string> KeysOlderThan(DateTimeOffset cutoff);
}

public sealed class AttachmentValidationException(string message) : Exception(message);

using HelpDesk.Application.Tickets;
using HelpDesk.Domain.Entities.Tickets;
using HelpDesk.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HelpDesk.Infrastructure.Attachments;

public sealed class TicketAttachmentWriter(HelpDeskDbContext db, IAttachmentStore store, ILogger<TicketAttachmentWriter> logger)
{
    public async Task AddAsync(long ticketId, int userId, IReadOnlyList<AttachmentUpload> uploads,
        ICollection<string> createdKeys, CancellationToken ct)
    {
        AttachmentContent.ValidateBatch(uploads);
        foreach (var upload in uploads)
        {
            var file = await store.SaveAsync(upload, ct);
            createdKeys.Add(file.StorageKey);
            db.Attachments.Add(new Attachment
            {
                TicketId = ticketId,
                UploadedByUserId = userId,
                OriginalFileName = file.FileName,
                StorageKey = file.StorageKey,
                ContentType = file.ContentType,
                SizeBytes = file.SizeBytes
            });
        }
        await db.SaveChangesAsync(ct);
    }

    public async Task RemoveUnreferencedAsync(IEnumerable<string> keys, CancellationToken ct = default)
    {
        var candidates = keys.ToArray();
        if (candidates.Length == 0)
            return;
        try
        {
            // Check persisted rows even after a commit error: never delete a possibly committed attachment.
            foreach (var batch in candidates.Chunk(200))
            {
                var referenced = await db.Attachments.AsNoTracking().Where(x => batch.Contains(x.StorageKey)).Select(x => x.StorageKey).ToListAsync(ct);
                foreach (var key in batch.Except(referenced))
                    store.Delete(key);
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning("Unable to reconcile attachment files ({ErrorType}); cleanup will retry.", exception.GetType().Name);
        }
    }
}

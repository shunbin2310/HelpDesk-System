using HelpDesk.Application.Tickets;
using HelpDesk.Domain.Constants;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HelpDesk.Infrastructure.Attachments;

public sealed class FileAttachmentStore(IOptions<AttachmentStorageOptions> options, ILogger<FileAttachmentStore> logger) : IAttachmentStore
{
    private readonly string root = Path.GetFullPath(options.Value.RootPath);

    public async Task<StoredAttachment> SaveAsync(AttachmentUpload upload, CancellationToken cancellationToken = default)
    {
        var type = AttachmentContent.ValidateName(upload.FileName, upload.Length);
        Directory.CreateDirectory(root);
        var key = Guid.NewGuid().ToString("N");
        var path = FilePath(key);
        var created = false;
        try
        {
            await using var source = upload.OpenRead();
            await using var destination = new FileStream(path, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, 81920, useAsync: true);
            created = true;
            var buffer = new byte[81920];
            long total = 0;
            int read;
            while ((read = await source.ReadAsync(buffer, cancellationToken)) != 0)
            {
                total += read;
                if (total > AttachmentLimits.MaxFileBytes || total > upload.Length)
                    throw new AttachmentValidationException("Each attachment must be no larger than 10 MB and match its declared size.");
                await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            }
            if (total != upload.Length)
                throw new AttachmentValidationException("The attachment upload was incomplete.");
            await destination.FlushAsync(cancellationToken);
            // shortcut: signatures are not malware scanning; add a local scanner before accepting untrusted external uploads.
            AttachmentContent.Validate(destination, upload.FileName);
            return new(key, upload.FileName, type, total);
        }
        catch
        {
            try
            {
                if (created)
                    Delete(key);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                logger.LogWarning("Attachment cleanup will be retried by reconciliation.");
            }
            throw;
        }
    }

    public Stream OpenRead(string storageKey) => new FileStream(FilePath(storageKey), FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true);
    public void Delete(string storageKey) => File.Delete(FilePath(storageKey));
    public IEnumerable<string> KeysOlderThan(DateTimeOffset cutoff) => !Directory.Exists(root) ? [] : Directory.EnumerateFiles(root, "*.blob")
        .Where(path => IsKey(Path.GetFileNameWithoutExtension(path)) && File.GetLastWriteTimeUtc(path) < cutoff.UtcDateTime).Select(path => Path.GetFileNameWithoutExtension(path));

    private string FilePath(string key) => IsKey(key) ? Path.Combine(root, key + ".blob") : throw new InvalidDataException("Invalid attachment storage key.");
    private static bool IsKey(string key) => key.Length == 32 && key.All(x => char.IsAsciiHexDigit(x));
}

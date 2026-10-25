using System.IO.Compression;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using HelpDesk.Application.Tickets;
using HelpDesk.Domain.Constants;

namespace HelpDesk.Infrastructure.Attachments;

public static class AttachmentContent
{
    private static readonly IReadOnlyDictionary<string, string> Types = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        [".png"] = "image/png", [".jpg"] = "image/jpeg", [".jpeg"] = "image/jpeg", [".gif"] = "image/gif",
        [".webp"] = "image/webp", [".bmp"] = "image/bmp", [".pdf"] = "application/pdf",
        [".doc"] = "application/msword", [".docx"] = "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        [".xls"] = "application/vnd.ms-excel", [".xlsx"] = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        [".txt"] = "text/plain", [".log"] = "text/plain"
    };

    public static string ValidateName(string name, long length)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > 255 || name.IndexOfAny(['/', '\\', ':']) >= 0
            || name.Any(char.IsControl) || name.EndsWith('.') || name.EndsWith(' '))
            throw new AttachmentValidationException("Use a file name without paths or control characters, up to 255 characters.");
        if (length is <= 0 or > AttachmentLimits.MaxFileBytes)
            throw new AttachmentValidationException("Each attachment must be nonempty and no larger than 10 MB.");
        if (!Types.TryGetValue(Path.GetExtension(name), out var type))
            throw new AttachmentValidationException("Supported files: PNG, JPG, JPEG, GIF, WEBP, BMP, PDF, DOC, DOCX, XLS, XLSX, TXT and LOG.");
        return type;
    }

    public static void ValidateBatch(IReadOnlyList<AttachmentUpload> uploads)
    {
        if (uploads.Count > AttachmentLimits.MaxFiles) throw new AttachmentValidationException("Upload at most 5 attachments per submission.");
        foreach (var file in uploads) ValidateName(file.FileName, file.Length);
    }

    public static void Validate(Stream stream, string fileName)
    {
        Span<byte> header = stackalloc byte[16];
        stream.Position = 0; var count = stream.Read(header); header = header[..count];
        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        var valid = extension switch
        {
            ".png" => header.StartsWith(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }),
            ".jpg" or ".jpeg" => header.StartsWith(new byte[] { 255, 216, 255 }),
            ".gif" => header.StartsWith("GIF87a"u8) || header.StartsWith("GIF89a"u8),
            ".bmp" => count >= 14 && header.StartsWith("BM"u8),
            ".webp" => count >= 12 && header.StartsWith("RIFF"u8) && header[8..].StartsWith("WEBP"u8),
            ".pdf" => header.StartsWith("%PDF-"u8),
            ".doc" or ".xls" => header.StartsWith(new byte[] { 208, 207, 17, 224, 161, 177, 26, 225 }),
            ".docx" or ".xlsx" => OfficePackage(stream, extension),
            ".txt" or ".log" => TextFile(stream),
            _ => false
        };
        if (!valid) throw new AttachmentValidationException("The attachment content does not match its file type, or the file is malformed.");
    }

    private static bool OfficePackage(Stream stream, string extension)
    {
        try
        {
            stream.Position = 0;
            using var zip = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
            if (zip.Entries.Count > 2000) return false;
            long expanded = 0;
            foreach (var entry in zip.Entries)
            {
                if (entry.Length > 100 * 1024 * 1024 - expanded) return false;
                expanded += entry.Length;
                if (entry.FullName.Contains("..", StringComparison.Ordinal)) return false;
            }
            var types = zip.GetEntry("[Content_Types].xml");
            var part = extension == ".docx" ? "word/document.xml" : "xl/workbook.xml";
            if (types is null || types.Length > 65536 || zip.GetEntry(part) is null) return false;
            using var xml = XmlReader.Create(types.Open(), new XmlReaderSettings
                { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 65536 });
            var document = XDocument.Load(xml);
            var expected = extension == ".docx" ? "application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml"
                : "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml";
            return document.Descendants().Any(x => x.Name.LocalName == "Override"
                && (string?)x.Attribute("PartName") == "/" + part && (string?)x.Attribute("ContentType") == expected)
                && !document.Descendants().Any(x => ((string?)x.Attribute("ContentType"))?.Contains("macroEnabled", StringComparison.OrdinalIgnoreCase) == true);
        }
        catch (Exception exception) when (exception is InvalidDataException or XmlException or IOException) { return false; }
    }

    private static bool TextFile(Stream stream)
    {
        try
        {
            stream.Position = 0;
            using var reader = new StreamReader(stream, new UTF8Encoding(false, true), detectEncodingFromByteOrderMarks: true, leaveOpen: true);
            var buffer = new char[4096]; int count;
            while ((count = reader.Read(buffer)) > 0)
                if (buffer.AsSpan(0, count).ContainsAnyInRange('\0', '\b') || buffer.AsSpan(0, count).ContainsAnyInRange('\x0e', '\x1f')
                    || buffer.AsSpan(0, count).Contains('\x0b') || buffer.AsSpan(0, count).Contains('\x0c')) return false;
            return true;
        }
        catch (DecoderFallbackException) { return false; }
    }
}

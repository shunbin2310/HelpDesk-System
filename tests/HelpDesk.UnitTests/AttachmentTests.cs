using System.IO.Compression;
using System.Text;
using HelpDesk.Application.Tickets;
using HelpDesk.Domain.Constants;
using HelpDesk.Infrastructure.Attachments;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace HelpDesk.UnitTests;

public sealed class AttachmentTests
{
    [Theory]
    [InlineData("../x.pdf")]
    [InlineData("C:\\x.txt")]
    [InlineData("x.txt:evil")]
    [InlineData("x.svg")]
    [InlineData("x.exe")]
    [InlineData("x.docm")]
    [InlineData("x.xlsx ")]
    [InlineData("x\n.txt")]
    public void UnsafeNamesAndUnsupportedTypesAreRejected(string name) =>
        Assert.Throws<AttachmentValidationException>(() => AttachmentContent.ValidateName(name, 10));

    [Fact]
    public void SizeAndBatchBoundariesAreEnforced()
    {
        Assert.Equal("text/plain", AttachmentContent.ValidateName("LOG.TXT", AttachmentLimits.MaxFileBytes));
        foreach (var size in new long[] { 0, -1, AttachmentLimits.MaxFileBytes + 1 })
            Assert.Throws<AttachmentValidationException>(() => AttachmentContent.ValidateName("x.txt", size));
        Assert.Throws<AttachmentValidationException>(() => AttachmentContent.ValidateBatch(
            Enumerable.Range(0, 6).Select(_ => new AttachmentUpload("x.txt", 1, () => Stream.Null)).ToArray()));
    }

    [Theory]
    [InlineData("x.pdf", "%PDF-1.7\n")]
    [InlineData("x.gif", "GIF89a")]
    [InlineData("x.log", "Hello\tworld\r\n")]
    public void ExpectedSignaturesAndTextAreAccepted(string name, string content)
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(content));
        AttachmentContent.Validate(stream, name);
    }

    [Theory]
    [InlineData("x.pdf", "fake PDF")]
    [InlineData("x.png", "<html>bad</html>")]
    [InlineData("x.txt", "a\0b")]
    [InlineData("x.docx", "not a ZIP")]
    public void IncorrectContentIsRejected(string name, string content)
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(content));
        Assert.Throws<AttachmentValidationException>(() => AttachmentContent.Validate(stream, name));
    }

    [Fact]
    public void OfficePackagesMustMatchTheirTypeAndCannotContainMacrosOrDtds()
    {
        foreach (var extension in new[] { ".docx", ".xlsx" })
        {
            using var package = Office(extension);
            AttachmentContent.Validate(package, "x" + extension);
            Assert.Throws<AttachmentValidationException>(() => AttachmentContent.Validate(package, "x" + (extension == ".docx" ? ".xlsx" : ".docx")));
        }
        using var macro = Office(".docx", macro: true);
        Assert.Throws<AttachmentValidationException>(() => AttachmentContent.Validate(macro, "x.docx"));
        using var dtd = Office(".docx", dtd: true);
        Assert.Throws<AttachmentValidationException>(() => AttachmentContent.Validate(dtd, "x.docx"));
    }

    [Fact]
    public async Task StoreUsesPrivateGeneratedKeysAndCleansIncompleteOrInvalidUploads()
    {
        var root = Path.Combine(Path.GetTempPath(), "HelpDeskStoreTest_" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new FileAttachmentStore(Options.Create(new AttachmentStorageOptions { RootPath = root }), NullLogger<FileAttachmentStore>.Instance);
            var saved = await store.SaveAsync(new("report.txt", 5, () => new MemoryStream("hello"u8.ToArray())));
            Assert.Matches("^[a-f0-9]{32}$", saved.StorageKey);
            using (var reader = new StreamReader(store.OpenRead(saved.StorageKey)))
                Assert.Equal("hello", await reader.ReadToEndAsync());
            Assert.Single(Directory.GetFiles(root));
            foreach (var upload in new[] { new AttachmentUpload("fake.pdf", 5, () => new MemoryStream("hello"u8.ToArray())),
                new AttachmentUpload("short.txt", 10, () => new MemoryStream("hello"u8.ToArray())),
                new AttachmentUpload("long.txt", 2, () => new MemoryStream("hello"u8.ToArray())) })
                await Assert.ThrowsAsync<AttachmentValidationException>(() => store.SaveAsync(upload));
            Assert.Single(Directory.GetFiles(root));
            Assert.Throws<InvalidDataException>(() => store.OpenRead("../x"));
            Assert.Empty(store.KeysOlderThan(DateTimeOffset.UtcNow.AddDays(-1)));
            File.SetLastWriteTimeUtc(Path.Combine(root, saved.StorageKey + ".blob"), DateTime.UtcNow.AddDays(-2));
            Assert.Equal(saved.StorageKey, Assert.Single(store.KeysOlderThan(DateTimeOffset.UtcNow.AddDays(-1))));
            store.Delete(saved.StorageKey);
            store.Delete(saved.StorageKey);
            Assert.Empty(Directory.GetFiles(root));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    private static MemoryStream Office(string extension, bool macro = false, bool dtd = false)
    {
        var stream = new MemoryStream();
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, true))
        {
            var part = extension == ".docx" ? "word/document.xml" : "xl/workbook.xml";
            var type = extension == ".docx" ? "wordprocessingml.document" : "spreadsheetml.sheet";
            using (var xml = new StreamWriter(zip.CreateEntry("[Content_Types].xml").Open()))
                xml.Write((dtd ? "<!DOCTYPE Types [<!ENTITY x 'bad'>]>" : "") + $"<Types><Override PartName='/{part}' ContentType='application/vnd.openxmlformats-officedocument.{type}.main+xml'/>"
                    + (macro ? "<Override ContentType='application/vnd.ms-word.document.macroEnabled.main+xml'/>" : "") + "</Types>");
            using var content = new StreamWriter(zip.CreateEntry(part).Open());
            content.Write("<document/>");
        }
        stream.Position = 0;
        return stream;
    }
}

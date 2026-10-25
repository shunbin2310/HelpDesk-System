namespace HelpDesk.Infrastructure.Attachments;

public sealed class AttachmentStorageOptions
{
    public string RootPath { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "HelpDesk-System", "Attachments");
}

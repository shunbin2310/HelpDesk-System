namespace HelpDesk.Domain.Constants;

public static class AttachmentLimits
{
    public const int MaxFiles = 5;
    public const long MaxFileBytes = 10 * 1024 * 1024;
    public const long MaxRequestBytes = MaxFiles * MaxFileBytes + 1024 * 1024;
}

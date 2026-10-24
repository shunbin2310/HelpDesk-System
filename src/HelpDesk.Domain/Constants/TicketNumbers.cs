using System.Globalization;

namespace HelpDesk.Domain.Constants;

public static class TicketNumbers
{
    private static readonly TimeZoneInfo Malaysia = TimeZoneInfo.FindSystemTimeZoneById("Asia/Kuala_Lumpur");

    public static string Create(long id, DateTimeOffset createdAtUtc)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(id);
        var date = TimeZoneInfo.ConvertTime(createdAtUtc, Malaysia).ToString("yyyyMMdd", CultureInfo.InvariantCulture);
        return $"HD-{date}-{id.ToString("D6", CultureInfo.InvariantCulture)}";
    }
}

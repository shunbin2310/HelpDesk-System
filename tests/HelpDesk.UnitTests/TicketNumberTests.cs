using HelpDesk.Domain.Constants;

namespace HelpDesk.UnitTests;

public sealed class TicketNumberTests
{
    [Theory]
    [InlineData("2026-10-23T15:59:59Z", "HD-20261023-000001")]
    [InlineData("2026-10-23T16:00:00Z", "HD-20261024-000001")]
    [InlineData("2026-12-31T16:00:00Z", "HD-20270101-000001")]
    public void DateUsesMalaysiaTimeAndSequenceUsesTheGlobalId(string utc, string expected) =>
        Assert.Equal(expected, TicketNumbers.Create(1, DateTimeOffset.Parse(utc)));

    [Fact]
    public void LargestIdentityStillFitsTheExistingDatabaseColumn() =>
        Assert.True(TicketNumbers.Create(long.MaxValue, DateTimeOffset.UtcNow).Length <= 32);

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void InvalidIdsAreRejected(long id) => Assert.Throws<ArgumentOutOfRangeException>(() => TicketNumbers.Create(id, DateTimeOffset.UtcNow));
}

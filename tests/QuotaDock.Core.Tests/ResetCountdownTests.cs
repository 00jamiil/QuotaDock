using QuotaDock.Core.Presentation;

namespace QuotaDock.Core.Tests;

public sealed class ResetCountdownTests
{
    [Fact]
    public void Format_UsesCountdownForNearReset()
    {
        var now = new DateTimeOffset(2026, 7, 23, 12, 0, 0, TimeSpan.Zero);

        Assert.Equal("Resets in 1h 24m", ResetCountdown.Format(now.AddMinutes(84), now));
    }

    [Fact]
    public void Format_ReportsResetNowForPastTimestamp()
    {
        var now = new DateTimeOffset(2026, 7, 23, 12, 0, 0, TimeSpan.Zero);

        Assert.Equal("Reset due", ResetCountdown.Format(now.AddSeconds(-1), now));
    }
}

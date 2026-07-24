using QuotaDock.Core.Domain;
using QuotaDock.Core.Refresh;

namespace QuotaDock.Core.Tests;

public sealed class RefreshPolicyTests
{
    [Theory]
    [InlineData(DataSourceKind.OfficialApi, 5)]
    [InlineData(DataSourceKind.LocalCli, 5)]
    [InlineData(DataSourceKind.DashboardReader, 15)]
    public void IntervalFor_UsesSourceSpecificDefaults(DataSourceKind source, int minutes)
    {
        Assert.Equal(TimeSpan.FromMinutes(minutes), RefreshPolicy.IntervalFor(source));
    }

    [Fact]
    public void CanRefreshManually_EnforcesThirtySecondCooldown()
    {
        var last = new DateTimeOffset(2026, 7, 23, 12, 0, 0, TimeSpan.Zero);

        Assert.False(RefreshPolicy.CanRefreshManually(last, last.AddSeconds(29)));
        Assert.True(RefreshPolicy.CanRefreshManually(last, last.AddSeconds(30)));
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(2, 2)]
    [InlineData(3, 5)]
    [InlineData(4, 15)]
    [InlineData(99, 15)]
    public void BackoffFor_CapsTransientFailures(int failureCount, int minutes)
    {
        Assert.Equal(TimeSpan.FromMinutes(minutes), RefreshPolicy.BackoffFor(failureCount));
    }
}


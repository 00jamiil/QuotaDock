using QuotaDock.Core.Domain;
using QuotaDock.Core.Presentation;
using QuotaDock.Core.Refresh;

namespace QuotaDock.Core.Tests;

public sealed class AdaptiveRefreshPlannerTests
{
    private static AdaptiveRefreshContext Context(
        RefreshMode mode = RefreshMode.Adaptive,
        int failures = 0,
        TimeSpan? retryAfter = null,
        TimeSpan? soonestReset = null,
        PaceStatus worstPace = PaceStatus.OnTrack,
        bool agentActive = false,
        DataSourceKind source = DataSourceKind.OfficialApi) =>
        new(source, mode, failures, retryAfter, soonestReset, worstPace, agentActive);

    [Theory]
    [InlineData(RefreshMode.Fixed1m, 1)]
    [InlineData(RefreshMode.Fixed2m, 2)]
    [InlineData(RefreshMode.Fixed5m, 5)]
    [InlineData(RefreshMode.Fixed15m, 15)]
    [InlineData(RefreshMode.Fixed30m, 30)]
    public void NextInterval_HonorsFixedModes(RefreshMode mode, int minutes)
    {
        Assert.Equal(TimeSpan.FromMinutes(minutes), AdaptiveRefreshPlanner.NextInterval(Context(mode)));
    }

    [Fact]
    public void NextInterval_ManualNeverAutoRefreshes()
    {
        Assert.Equal(TimeSpan.MaxValue, AdaptiveRefreshPlanner.NextInterval(Context(RefreshMode.Manual)));
    }

    [Fact]
    public void NextInterval_FailureBackoffOverridesEverything()
    {
        var next = AdaptiveRefreshPlanner.NextInterval(Context(RefreshMode.Fixed1m, failures: 3));
        Assert.Equal(TimeSpan.FromMinutes(5), next);
    }

    [Fact]
    public void NextInterval_RetryAfterWinsWhenLongerThanBackoff()
    {
        var next = AdaptiveRefreshPlanner.NextInterval(
            Context(RefreshMode.Fixed1m, failures: 1, retryAfter: TimeSpan.FromMinutes(9)));
        Assert.Equal(TimeSpan.FromMinutes(9), next);
    }

    [Fact]
    public void NextInterval_AdaptiveBaselineIsSourceInterval()
    {
        Assert.Equal(TimeSpan.FromMinutes(5), AdaptiveRefreshPlanner.NextInterval(Context()));
    }

    [Fact]
    public void NextInterval_AdaptiveTightensToFloorWhenPaceExceeds()
    {
        Assert.Equal(AdaptiveRefreshPlanner.AdaptiveFloor,
            AdaptiveRefreshPlanner.NextInterval(Context(worstPace: PaceStatus.Exceeds)));
    }

    [Fact]
    public void NextInterval_AdaptiveTightensWhenAgentActive()
    {
        Assert.Equal(AdaptiveRefreshPlanner.AdaptiveFloor,
            AdaptiveRefreshPlanner.NextInterval(Context(agentActive: true)));
    }

    [Fact]
    public void NextInterval_AdaptiveTightensNearReset()
    {
        var next = AdaptiveRefreshPlanner.NextInterval(Context(soonestReset: TimeSpan.FromMinutes(1)));
        Assert.Equal(AdaptiveRefreshPlanner.AdaptiveFloor, next);
    }

    [Fact]
    public void NextInterval_AdaptiveDashboardIdleStaysWithinCeiling()
    {
        var next = AdaptiveRefreshPlanner.NextInterval(Context(source: DataSourceKind.DashboardReader));
        Assert.True(next <= AdaptiveRefreshPlanner.AdaptiveIdleCeiling);
        Assert.Equal(TimeSpan.FromMinutes(15), next);
    }
}

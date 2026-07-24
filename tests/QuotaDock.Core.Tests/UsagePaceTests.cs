using QuotaDock.Core.Domain;
using QuotaDock.Core.Presentation;

namespace QuotaDock.Core.Tests;

public sealed class UsagePaceTests
{
    private static readonly DateTimeOffset Start = new(2026, 7, 23, 12, 0, 0, TimeSpan.Zero);

    private static UsageMetric Used(decimal current, decimal limit, DateTimeOffset resetsAt) =>
        UsageMetric.Create("m", "Metric", MetricKind.Tokens, MetricDirection.Used,
            current, limit, "tokens", MetricScope.Session, resetsAt);

    [Fact]
    public void Calculate_ProjectsSteadyBurnAsOnTrack()
    {
        // Used 25 of 100 in the first hour of a 4-hour window -> projects 100 at reset.
        var metric = Used(25m, 100m, Start.AddHours(4));
        var result = UsagePace.Calculate(metric, Start, Start.AddHours(1));

        Assert.Equal(PaceStatus.Exceeds, result.Status);
        Assert.Equal(25m, result.UsedPerHour);
        Assert.Equal(100m, result.ProjectedAtReset);
    }

    [Fact]
    public void Calculate_LowBurnIsOnTrack()
    {
        // Used 5 of 100 in the first hour of a 4-hour window -> projects 20.
        var metric = Used(5m, 100m, Start.AddHours(4));
        var result = UsagePace.Calculate(metric, Start, Start.AddHours(1));

        Assert.Equal(PaceStatus.OnTrack, result.Status);
        Assert.Equal(20m, result.ProjectedAtReset);
    }

    [Fact]
    public void Calculate_NearLimitProjectionIsWatch()
    {
        // Projects to 92 of 100 -> within the 90% watch band but under the limit.
        var metric = Used(23m, 100m, Start.AddHours(4));
        var result = UsagePace.Calculate(metric, Start, Start.AddHours(1));

        Assert.Equal(PaceStatus.Watch, result.Status);
    }

    [Fact]
    public void Calculate_ConvertsRemainingDirectionToUsed()
    {
        var metric = UsageMetric.Create("m", "Metric", MetricKind.QuotaPercentage,
            MetricDirection.Remaining, 60m, 100m, "%", MetricScope.Session, Start.AddHours(4));
        var result = UsagePace.Calculate(metric, Start, Start.AddHours(1));

        // 40 used in 1h over a 4h window -> projects 160 -> exceeds.
        Assert.Equal(PaceStatus.Exceeds, result.Status);
        Assert.Equal(40m, result.UsedPerHour);
    }

    [Fact]
    public void Calculate_ReturnsUnknownWithoutLimitOrReset()
    {
        var noLimit = UsageMetric.Create("m", "Metric", MetricKind.Tokens, MetricDirection.Used,
            10m, null, "tokens", MetricScope.Monthly, Start.AddHours(4));
        var noReset = UsageMetric.Create("m", "Metric", MetricKind.Tokens, MetricDirection.Used,
            10m, 100m, "tokens", MetricScope.Monthly, null);

        Assert.Equal(PaceStatus.Unknown, UsagePace.Calculate(noLimit, Start, Start.AddHours(1)).Status);
        Assert.Equal(PaceStatus.Unknown, UsagePace.Calculate(noReset, Start, Start.AddHours(1)).Status);
    }

    [Fact]
    public void Calculate_ReturnsUnknownWhenNowOutsideWindow()
    {
        var metric = Used(25m, 100m, Start.AddHours(4));

        Assert.Equal(PaceStatus.Unknown, UsagePace.Calculate(metric, Start, Start).Status);
        Assert.Equal(PaceStatus.Unknown, UsagePace.Calculate(metric, Start, Start.AddHours(5)).Status);
    }
}

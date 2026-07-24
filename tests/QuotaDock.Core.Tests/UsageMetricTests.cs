using QuotaDock.Core.Domain;

namespace QuotaDock.Core.Tests;

public sealed class UsageMetricTests
{
    [Fact]
    public void Create_ComputesBoundedProgressAndRemainingValue()
    {
        var metric = UsageMetric.Create(
            "codex-session",
            "Codex session",
            MetricKind.QuotaPercentage,
            MetricDirection.Used,
            current: 32m,
            limit: 100m,
            unit: "%",
            MetricScope.Session,
            resetsAt: null);

        Assert.Equal(0.32m, metric.ProgressFraction);
        Assert.Equal(68m, metric.RemainingValue);
    }

    [Fact]
    public void Create_LeavesProgressUndefinedWithoutARealLimit()
    {
        var metric = UsageMetric.Create(
            "openai-spend",
            "Month-to-date spend",
            MetricKind.Currency,
            MetricDirection.Used,
            current: 18.42m,
            limit: null,
            unit: "USD",
            MetricScope.Monthly,
            resetsAt: null);

        Assert.Null(metric.ProgressFraction);
        Assert.Null(metric.RemainingValue);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Create_RejectsNonPositiveLimits(decimal invalidLimit)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => UsageMetric.Create(
            "metric",
            "Metric",
            MetricKind.Tokens,
            MetricDirection.Used,
            1m,
            invalidLimit,
            "tokens",
            MetricScope.Monthly,
            null));
    }
}


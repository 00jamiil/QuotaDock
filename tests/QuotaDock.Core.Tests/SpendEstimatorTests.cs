using QuotaDock.Core.Domain;
using QuotaDock.Core.Presentation;

namespace QuotaDock.Core.Tests;

public sealed class SpendEstimatorTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 24, 12, 0, 0, TimeSpan.Zero);

    private static UsageSnapshot Snapshot(
        string connectionId,
        DateTimeOffset at,
        params UsageMetric[] metrics) =>
        new(connectionId, ProviderKind.OpenAI, "Work", DataSourceKind.OfficialApi,
            at, ConnectionHealth.Fresh, metrics, null);

    private static UsageMetric Currency(decimal amount, string unit) =>
        UsageMetric.Create("spend", "Spend", MetricKind.Currency, MetricDirection.Used,
            amount, null, unit, MetricScope.Monthly, null);

    private static UsageMetric Tokens(decimal amount) =>
        UsageMetric.Create("tokens", "Tokens", MetricKind.Tokens, MetricDirection.Used,
            amount, null, "tokens", MetricScope.Monthly, null);

    [Fact]
    public void Summarize_GroupsByCurrencyAndIgnoresNonCurrency()
    {
        var snapshots = new[]
        {
            Snapshot("a", Now.AddDays(-1), Currency(10m, "USD"), Tokens(9999m)),
            Snapshot("b", Now.AddDays(-2), Currency(5m, "EUR"))
        };

        var summary = SpendEstimator.Summarize(snapshots, Now);

        Assert.Equal(2, summary.LastSevenDays.Count);
        Assert.Equal(10m, summary.LastSevenDays.Single(t => t.Currency == "USD").Amount);
        Assert.Equal(5m, summary.LastSevenDays.Single(t => t.Currency == "EUR").Amount);
    }

    [Fact]
    public void Summarize_KeepsLatestPerMetricToAvoidDoubleCounting()
    {
        // Two month-to-date snapshots for the same connection+metric; only the
        // most recent value should count, not their sum.
        var snapshots = new[]
        {
            Snapshot("a", Now.AddDays(-3), Currency(12m, "USD")),
            Snapshot("a", Now.AddDays(-1), Currency(20m, "USD"))
        };

        var summary = SpendEstimator.Summarize(snapshots, Now);

        Assert.Equal(20m, summary.LastSevenDays.Single().Amount);
    }

    [Fact]
    public void Summarize_SevenDayWindowExcludesOlderThanSeven()
    {
        var snapshots = new[]
        {
            Snapshot("a", Now.AddDays(-2), Currency(7m, "USD")),
            Snapshot("b", Now.AddDays(-20), Currency(30m, "USD"))
        };

        var summary = SpendEstimator.Summarize(snapshots, Now);

        Assert.Equal(7m, summary.LastSevenDays.Single().Amount);
        Assert.Equal(37m, summary.LastThirtyDays.Single().Amount);
    }

    [Fact]
    public void Summarize_EmptyWhenNoCurrencyMetrics()
    {
        var summary = SpendEstimator.Summarize(
            new[] { Snapshot("a", Now, Tokens(100m)) }, Now);

        Assert.False(summary.HasData);
    }
}

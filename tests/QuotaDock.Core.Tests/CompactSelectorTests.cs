using QuotaDock.Core.Domain;
using QuotaDock.Core.Presentation;

namespace QuotaDock.Core.Tests;

public sealed class CompactSelectorTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 24, 12, 0, 0, TimeSpan.Zero);

    private static UsageSnapshot Snapshot(string connectionId, params UsageMetric[] metrics) =>
        new(connectionId, ProviderKind.OpenAI, "Work", DataSourceKind.OfficialApi,
            Now, ConnectionHealth.Fresh, metrics, null);

    private static UsageMetric Used(string id, decimal current, decimal limit, DateTimeOffset? reset = null) =>
        UsageMetric.Create(id, id, MetricKind.QuotaPercentage, MetricDirection.Used,
            current, limit, "%", MetricScope.Session, reset);

    [Fact]
    public void Select_PicksHighestUsedFractionAsHero()
    {
        var snapshots = new[]
        {
            Snapshot("a", Used("low", 20m, 100m)),
            Snapshot("b", Used("high", 80m, 100m))
        };

        var view = CompactSelector.Select(snapshots, null, Now);

        Assert.Equal("high", view.Hero!.Metric.Id);
        Assert.Single(view.Switcher);
    }

    [Fact]
    public void Select_BreaksTiesBySoonestReset()
    {
        var snapshots = new[]
        {
            Snapshot("a", Used("later", 50m, 100m, Now.AddHours(5))),
            Snapshot("b", Used("sooner", 50m, 100m, Now.AddHours(1)))
        };

        var view = CompactSelector.Select(snapshots, null, Now);

        Assert.Equal("sooner", view.Hero!.Metric.Id);
    }

    [Fact]
    public void Select_RanksOnlyPinnedWhenPinsProvided()
    {
        var snapshots = new[]
        {
            Snapshot("a", Used("unpinned-high", 95m, 100m)),
            Snapshot("b", Used("pinned-low", 10m, 100m))
        };

        var view = CompactSelector.Select(snapshots, new[] { "b:pinned-low" }, Now);

        Assert.Equal("pinned-low", view.Hero!.Metric.Id);
        Assert.Empty(view.Switcher);
    }

    [Fact]
    public void Select_ReturnsEmptyForNoMetrics()
    {
        var view = CompactSelector.Select(Array.Empty<UsageSnapshot>(), null, Now);
        Assert.Null(view.Hero);
    }
}

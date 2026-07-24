using QuotaDock.Core.Domain;

namespace QuotaDock.Core.Presentation;

/// <summary>
/// A metric paired with the snapshot it came from, ready for ranking or display.
/// </summary>
public sealed record MetricRef(UsageSnapshot Snapshot, UsageMetric Metric)
{
    public string Key => $"{Snapshot.ConnectionId}:{Metric.Id}";
}

/// <summary>
/// The collapsed widget view: the single most-constrained metric plus an ordered
/// switcher of the rest.
/// </summary>
public sealed record CompactView(MetricRef? Hero, IReadOnlyList<MetricRef> Switcher)
{
    public static CompactView Empty { get; } = new(null, []);
}

/// <summary>
/// Picks the single most-constrained metric for compact/merge mode. Ranking uses
/// the used-fraction of the limit (only meaningful for limited metrics), with the
/// soonest reset as a tie-breaker. Metrics without a limit are still offered in
/// the switcher but never fabricated into a fraction.
/// </summary>
public static class CompactSelector
{
    public static CompactView Select(
        IReadOnlyList<UsageSnapshot> snapshots,
        IReadOnlyCollection<string>? pinnedKeys,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(snapshots);

        var all = snapshots
            .SelectMany(snapshot => snapshot.Metrics.Select(metric => new MetricRef(snapshot, metric)))
            .ToList();
        if (all.Count == 0)
        {
            return CompactView.Empty;
        }

        // When the user has pinned metrics, rank only those; otherwise rank all.
        var pinned = pinnedKeys is { Count: > 0 }
            ? all.Where(item => pinnedKeys.Contains(item.Key)).ToList()
            : all;
        var pool = pinned.Count > 0 ? pinned : all;

        var ordered = pool
            .OrderByDescending(item => UsedFraction(item.Metric))
            .ThenBy(item => ResetSortKey(item.Metric, now))
            .ThenBy(item => item.Snapshot.Provider)
            .ThenBy(item => item.Metric.Label, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new CompactView(ordered[0], ordered.Skip(1).ToArray());
    }

    private static decimal UsedFraction(UsageMetric metric)
    {
        if (metric.Limit is not { } limit || limit <= 0m)
        {
            return -1m;
        }

        var used = metric.Direction == MetricDirection.Used
            ? metric.Current
            : decimal.Max(0m, limit - metric.Current);
        return decimal.Clamp(used / limit, 0m, 1m);
    }

    private static double ResetSortKey(UsageMetric metric, DateTimeOffset now)
    {
        if (metric.ResetsAt is not { } reset)
        {
            return double.MaxValue;
        }

        var remaining = (reset - now).TotalSeconds;
        return remaining < 0 ? 0 : remaining;
    }
}

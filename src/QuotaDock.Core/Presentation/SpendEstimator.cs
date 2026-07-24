using QuotaDock.Core.Domain;

namespace QuotaDock.Core.Presentation;

/// <summary>
/// A rolling spend total for one currency over one window. Currencies are never
/// mixed and never converted.
/// </summary>
public sealed record SpendTotal(string Currency, decimal Amount);

/// <summary>
/// Local 7/30-day spend estimates grouped by native currency. Only currency
/// metrics contribute; providers without cost history simply produce nothing.
/// </summary>
public sealed record SpendSummary(
    IReadOnlyList<SpendTotal> LastSevenDays,
    IReadOnlyList<SpendTotal> LastThirtyDays)
{
    public static SpendSummary Empty { get; } = new([], []);

    public bool HasData => LastSevenDays.Count > 0 || LastThirtyDays.Count > 0;
}

/// <summary>
/// Derives local spend estimates from stored snapshots. It sums the latest
/// currency reading per connection+metric inside each window, so repeated
/// month-to-date snapshots are not double counted. Unlike units are never merged.
/// </summary>
public static class SpendEstimator
{
    public static SpendSummary Summarize(
        IReadOnlyList<UsageSnapshot> snapshots,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(snapshots);

        return new SpendSummary(
            Window(snapshots, now, TimeSpan.FromDays(7)),
            Window(snapshots, now, TimeSpan.FromDays(30)));
    }

    private static IReadOnlyList<SpendTotal> Window(
        IReadOnlyList<UsageSnapshot> snapshots,
        DateTimeOffset now,
        TimeSpan window)
    {
        var cutoff = now - window;

        // Keep the most recent currency reading per (connection, metric) so that
        // month-to-date cumulative metrics are counted once, not per snapshot.
        var latestPerMetric = new Dictionary<string, (DateTimeOffset At, string Currency, decimal Amount)>(
            StringComparer.Ordinal);

        foreach (var snapshot in snapshots)
        {
            if (snapshot.CapturedAt < cutoff)
            {
                continue;
            }

            foreach (var metric in snapshot.Metrics)
            {
                if (metric.Kind != MetricKind.Currency)
                {
                    continue;
                }

                var key = $"{snapshot.ConnectionId}:{metric.Id}";
                if (!latestPerMetric.TryGetValue(key, out var existing) || snapshot.CapturedAt >= existing.At)
                {
                    latestPerMetric[key] = (snapshot.CapturedAt, metric.Unit, metric.Current);
                }
            }
        }

        return latestPerMetric.Values
            .GroupBy(entry => entry.Currency, StringComparer.OrdinalIgnoreCase)
            .Select(group => new SpendTotal(group.Key, group.Sum(entry => entry.Amount)))
            .OrderBy(total => total.Currency, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }
}

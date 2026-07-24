using QuotaDock.Core.Domain;

namespace QuotaDock.Core.Presentation;

public enum PaceStatus
{
    Unknown,
    OnTrack,
    Watch,
    Exceeds
}

/// <summary>
/// Immutable result of a burn-rate projection for a single metric window.
/// Values are only meaningful when <see cref="Status"/> is not
/// <see cref="PaceStatus.Unknown"/>.
/// </summary>
public sealed record PaceResult(
    PaceStatus Status,
    decimal? UsedPerHour,
    decimal? ProjectedAtReset,
    decimal? Limit)
{
    public static PaceResult Unknown { get; } = new(PaceStatus.Unknown, null, null, null);
}

/// <summary>
/// Pure burn-rate calculator. Given a metric with a known limit and window, it
/// projects the value at reset and classifies the pace. It never guesses: when
/// inputs are insufficient it returns <see cref="PaceResult.Unknown"/>.
/// </summary>
public static class UsagePace
{
    // Projected use within this fraction of the limit (but under it) is a warning.
    private const decimal WatchFraction = 0.9m;

    public static PaceResult Calculate(
        UsageMetric metric,
        DateTimeOffset windowStart,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(metric);

        if (metric.Limit is not { } limit || limit <= 0m || metric.ResetsAt is not { } resetsAt)
        {
            return PaceResult.Unknown;
        }

        // The window must be a sane, ordered interval that currently contains "now".
        if (windowStart >= resetsAt || now <= windowStart || now >= resetsAt)
        {
            return PaceResult.Unknown;
        }

        var used = metric.Direction == MetricDirection.Used
            ? metric.Current
            : decimal.Max(0m, limit - metric.Current);

        var elapsedHours = (decimal)(now - windowStart).TotalHours;
        if (elapsedHours <= 0m)
        {
            return PaceResult.Unknown;
        }

        var usedPerHour = used / elapsedHours;
        var remainingHours = (decimal)(resetsAt - now).TotalHours;
        var projected = used + (usedPerHour * remainingHours);

        var status = projected >= limit
            ? PaceStatus.Exceeds
            : projected >= limit * WatchFraction
                ? PaceStatus.Watch
                : PaceStatus.OnTrack;

        return new PaceResult(status, usedPerHour, projected, limit);
    }

    public static string Describe(PaceStatus status) => status switch
    {
        PaceStatus.OnTrack => "On track",
        PaceStatus.Watch => "Watch",
        PaceStatus.Exceeds => "Over pace",
        _ => "No pace"
    };
}

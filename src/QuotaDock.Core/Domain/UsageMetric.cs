namespace QuotaDock.Core.Domain;

public sealed record UsageMetric
{
    public string Id { get; }
    public string Label { get; }
    public MetricKind Kind { get; }
    public MetricDirection Direction { get; }
    public decimal Current { get; }
    public decimal? Limit { get; }
    public string Unit { get; }
    public MetricScope Scope { get; }
    public DateTimeOffset? ResetsAt { get; }
    public IReadOnlyDictionary<string, string> Dimensions { get; }

    public decimal? ProgressFraction => Limit is > 0m
        ? decimal.Clamp(Current / Limit.Value, 0m, 1m)
        : null;

    public decimal? RemainingValue => Limit is > 0m
        ? Direction == MetricDirection.Remaining
            ? Current
            : decimal.Max(0m, Limit.Value - Current)
        : null;

    public UsageMetric(
        string id,
        string label,
        MetricKind kind,
        MetricDirection direction,
        decimal current,
        decimal? limit,
        string unit,
        MetricScope scope,
        DateTimeOffset? resetsAt,
        IReadOnlyDictionary<string, string>? dimensions = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(label);
        ArgumentException.ThrowIfNullOrWhiteSpace(unit);
        if (current < 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(current), "Metric values cannot be negative.");
        }

        if (limit is <= 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(limit), "Metric limits must be greater than zero.");
        }

        Id = id;
        Label = label;
        Kind = kind;
        Direction = direction;
        Current = current;
        Limit = limit;
        Unit = unit;
        Scope = scope;
        ResetsAt = resetsAt;
        Dimensions = dimensions ?? new Dictionary<string, string>(StringComparer.Ordinal);
    }

    public static UsageMetric Create(
        string id,
        string label,
        MetricKind kind,
        MetricDirection direction,
        decimal current,
        decimal? limit,
        string unit,
        MetricScope scope,
        DateTimeOffset? resetsAt,
        IReadOnlyDictionary<string, string>? dimensions = null) =>
        new(id, label, kind, direction, current, limit, unit, scope, resetsAt, dimensions);
}


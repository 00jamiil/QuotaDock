using System.Text.Json;
using QuotaDock.Connectors.Api;
using QuotaDock.Core.Domain;

namespace QuotaDock.Connectors.Anthropic;

/// <summary>
/// Normalizes the Claude account usage window payload into QuotaDock metrics.
/// The payload exposes rolling-window utilization (a 5-hour session window and a
/// 7-day window, sometimes split per model family) plus reset timestamps. Values
/// are reported by Anthropic as percent utilized, which QuotaDock converts to an
/// explicit "remaining" quota percentage. Unknown shapes fail closed with
/// <see cref="ConnectionHealth.FormatChanged"/> so no usage is fabricated.
/// </summary>
public static class ClaudeUsageParser
{
    public static ConnectorFetchResult Parse(
        string connectionId,
        string accountLabel,
        string payload,
        DateTimeOffset capturedAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(accountLabel);

        try
        {
            using var document = JsonDocument.Parse(payload);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return FormatChanged();
            }

            var metrics = new List<UsageMetric>();
            TryAddWindow(metrics, root, "five_hour", "claude-session", "Session", MetricScope.Session, capturedAt);
            TryAddWindow(metrics, root, "seven_day", "claude-weekly", "Weekly (all models)", MetricScope.Weekly, capturedAt);
            TryAddWindow(metrics, root, "seven_day_opus", "claude-weekly-opus", "Weekly (Opus)", MetricScope.Weekly, capturedAt);

            if (metrics.Count == 0)
            {
                return FormatChanged();
            }

            return ConnectorFetchResult.Success(new UsageSnapshot(
                connectionId,
                ProviderKind.Anthropic,
                accountLabel,
                DataSourceKind.LocalCli,
                capturedAt,
                ConnectionHealth.Fresh,
                metrics,
                null));
        }
        catch (Exception exception) when (
            exception is JsonException or InvalidOperationException or FormatException or ArgumentException)
        {
            return FormatChanged();
        }
    }

    private static void TryAddWindow(
        ICollection<UsageMetric> metrics,
        JsonElement root,
        string propertyName,
        string id,
        string label,
        MetricScope scope,
        DateTimeOffset capturedAt)
    {
        if (!root.TryGetProperty(propertyName, out var window) || window.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        var utilization = ReadUtilization(window);
        if (utilization is null)
        {
            return;
        }

        var remaining = decimal.Clamp(100m - utilization.Value, 0m, 100m);
        var resetsAt = ReadResetsAt(window, capturedAt);
        metrics.Add(UsageMetric.Create(
            id,
            label,
            MetricKind.QuotaPercentage,
            MetricDirection.Remaining,
            remaining,
            100m,
            "%",
            scope,
            resetsAt));
    }

    private static decimal? ReadUtilization(JsonElement window)
    {
        // Anthropic reports utilization as a percentage in [0, 100]. We do NOT
        // guess a 0-1 fraction scale: the payload carries no discriminator, so
        // treating a bare "1" as either 1% or 100% would fabricate quota. We take
        // the documented percent scale and clamp defensively.
        if (window.TryGetProperty("utilization", out var utilizationElement) &&
            utilizationElement.ValueKind == JsonValueKind.Number &&
            utilizationElement.TryGetDecimal(out var utilization))
        {
            return decimal.Clamp(utilization, 0m, 100m);
        }

        return null;
    }

    private static DateTimeOffset? ReadResetsAt(JsonElement window, DateTimeOffset capturedAt)
    {
        if (!window.TryGetProperty("resets_at", out var resetElement))
        {
            return null;
        }

        try
        {
            switch (resetElement.ValueKind)
            {
                case JsonValueKind.String when DateTimeOffset.TryParse(
                    resetElement.GetString(),
                    System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.RoundtripKind,
                    out var parsed):
                    return parsed;
                case JsonValueKind.Number when resetElement.TryGetInt64(out var epoch):
                    // Accept both epoch-seconds and epoch-milliseconds without
                    // throwing on an out-of-range conversion.
                    return epoch >= 100_000_000_000L
                        ? DateTimeOffset.FromUnixTimeMilliseconds(epoch)
                        : DateTimeOffset.FromUnixTimeSeconds(epoch);
                default:
                    return null;
            }
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }
    }

    private static ConnectorFetchResult FormatChanged() =>
        ConnectorFetchResult.Failure(
            ConnectionHealth.FormatChanged,
            "The Claude usage window format is not supported by this QuotaDock version.");
}

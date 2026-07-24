using System.Text.Json;
using QuotaDock.Connectors.Api;
using QuotaDock.Core.Domain;

namespace QuotaDock.Connectors.Xai;

/// <summary>
/// Normalizes the xAI account usage payload into QuotaDock metrics. Grok Build
/// exposes credit-based usage (used/remaining credits plus rolling windows).
/// Unknown shapes fail closed with <see cref="ConnectionHealth.FormatChanged"/>
/// so no usage is ever fabricated.
/// </summary>
/// <remarks>
/// The exact live payload shape is not publicly documented; this parser accepts
/// a couple of conventional credit/window layouts and otherwise reports a format
/// change. Verify against the live xAI response and extend as needed.
/// </remarks>
public static class GrokUsageParser
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

            // Credit balance (remaining credits on the plan).
            if (root.TryGetProperty("credits", out var credits) && credits.ValueKind == JsonValueKind.Object)
            {
                var remaining = ApiConnectorSupport.Decimal(credits, "remaining");
                var total = ApiConnectorSupport.Decimal(credits, "total");
                if (remaining > 0m || total > 0m)
                {
                    metrics.Add(UsageMetric.Create(
                        "grok-credits",
                        "Credits remaining",
                        MetricKind.Credits,
                        MetricDirection.Remaining,
                        remaining,
                        total > 0m ? total : null,
                        "credits",
                        MetricScope.Account,
                        ReadResetsAt(credits)));
                }
            }

            // Rolling usage windows (session / weekly), reported as percent used.
            TryAddWindow(metrics, root, "session", "grok-session", "Session", MetricScope.Session);
            TryAddWindow(metrics, root, "weekly", "grok-weekly", "Weekly", MetricScope.Weekly);

            if (metrics.Count == 0)
            {
                return FormatChanged();
            }

            return ConnectorFetchResult.Success(new UsageSnapshot(
                connectionId,
                ProviderKind.Xai,
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
        MetricScope scope)
    {
        if (!root.TryGetProperty(propertyName, out var window) || window.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        var used = ApiConnectorSupport.Decimal(window, "usedPercent");
        if (used is < 0m or > 100m)
        {
            return;
        }

        var remaining = decimal.Clamp(100m - used, 0m, 100m);
        metrics.Add(UsageMetric.Create(
            id,
            label,
            MetricKind.QuotaPercentage,
            MetricDirection.Remaining,
            remaining,
            100m,
            "%",
            scope,
            ReadResetsAt(window)));
    }

    private static DateTimeOffset? ReadResetsAt(JsonElement element)
    {
        if (!element.TryGetProperty("resetsAt", out var resetElement))
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
            "The Grok usage format is not supported by this QuotaDock version.");
}

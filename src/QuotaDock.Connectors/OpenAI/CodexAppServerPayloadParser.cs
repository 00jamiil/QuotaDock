using System.Globalization;
using System.Text.Json;
using QuotaDock.Connectors.Api;
using QuotaDock.Core.Domain;

namespace QuotaDock.Connectors.OpenAI;

public static class CodexAppServerPayloadParser
{
    public static ConnectorFetchResult Parse(
        string connectionId,
        string accountLabel,
        string rateLimitsPayload,
        string usagePayload,
        DateTimeOffset capturedAt)
    {
        try
        {
            using var rateDocument = JsonDocument.Parse(rateLimitsPayload);
            using var usageDocument = JsonDocument.Parse(usagePayload);
            if (!TryGetResult(rateDocument.RootElement, out var rateResult) ||
                !rateResult.TryGetProperty("rateLimits", out var limits) ||
                limits.ValueKind != JsonValueKind.Object)
            {
                return FormatChanged();
            }

            var metrics = new List<UsageMetric>();
            AddWindow(metrics, limits, "primary", "codex-session", "Session", MetricScope.Session);
            AddWindow(metrics, limits, "secondary", "codex-weekly", "Weekly", MetricScope.Weekly);

            if (limits.TryGetProperty("credits", out var credits) && credits.ValueKind == JsonValueKind.Object)
            {
                var balanceText = ApiConnectorSupport.String(credits, "balance");
                if (decimal.TryParse(balanceText, NumberStyles.Number, CultureInfo.InvariantCulture, out var balance) && balance >= 0m)
                {
                    metrics.Add(UsageMetric.Create(
                        "codex-credits",
                        "Credits remaining",
                        MetricKind.Credits,
                        MetricDirection.Remaining,
                        balance,
                        null,
                        "credits",
                        MetricScope.Account,
                        null));
                }
            }

            if (TryGetResult(usageDocument.RootElement, out var usageResult) &&
                usageResult.TryGetProperty("dailyUsageBuckets", out var buckets) &&
                buckets.ValueKind == JsonValueKind.Array)
            {
                var monthlyTokens = 0m;
                foreach (var bucket in buckets.EnumerateArray())
                {
                    var dateText = ApiConnectorSupport.String(bucket, "startDate");
                    if (DateOnly.TryParse(dateText, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) &&
                        date.Year == capturedAt.Year &&
                        date.Month == capturedAt.Month)
                    {
                        monthlyTokens += ApiConnectorSupport.Decimal(bucket, "tokens");
                    }
                }

                if (monthlyTokens > 0m)
                {
                    metrics.Add(UsageMetric.Create(
                        "codex-month-tokens",
                        "Tokens this month",
                        MetricKind.Tokens,
                        MetricDirection.Used,
                        monthlyTokens,
                        null,
                        "tokens",
                        MetricScope.Monthly,
                        new DateTimeOffset(capturedAt.Year, capturedAt.Month, 1, 0, 0, 0, TimeSpan.Zero).AddMonths(1)));
                }
            }

            if (metrics.Count == 0)
            {
                return FormatChanged();
            }

            var plan = ApiConnectorSupport.String(limits, "planType");
            var displayAccount = string.IsNullOrWhiteSpace(plan)
                ? accountLabel
                : $"{accountLabel} · {ToTitle(plan)}";

            return ConnectorFetchResult.Success(new UsageSnapshot(
                connectionId,
                ProviderKind.OpenAI,
                displayAccount,
                DataSourceKind.LocalCli,
                capturedAt,
                ConnectionHealth.Fresh,
                metrics,
                null));
        }
        catch (Exception exception) when (exception is JsonException or ArgumentException or OverflowException)
        {
            return FormatChanged();
        }
    }

    private static void AddWindow(
        ICollection<UsageMetric> metrics,
        JsonElement limits,
        string propertyName,
        string id,
        string label,
        MetricScope scope)
    {
        if (!limits.TryGetProperty(propertyName, out var window) || window.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        var used = ApiConnectorSupport.Decimal(window, "usedPercent");
        if (used is < 0m or > 100m)
        {
            return;
        }

        DateTimeOffset? resetsAt = null;
        if (window.TryGetProperty("resetsAt", out var resetValue) && resetValue.TryGetInt64(out var resetSeconds))
        {
            resetsAt = DateTimeOffset.FromUnixTimeSeconds(resetSeconds);
        }

        metrics.Add(UsageMetric.Create(
            id,
            label,
            MetricKind.QuotaPercentage,
            MetricDirection.Remaining,
            100m - used,
            100m,
            "%",
            scope,
            resetsAt));
    }

    private static bool TryGetResult(JsonElement root, out JsonElement result)
    {
        if (root.TryGetProperty("error", out _))
        {
            result = default;
            return false;
        }

        return root.TryGetProperty("result", out result) && result.ValueKind == JsonValueKind.Object;
    }

    private static string ToTitle(string value)
    {
        var words = value.Split(['_', '-'], StringSplitOptions.RemoveEmptyEntries);
        return string.Join(' ', words.Select(word =>
            string.Concat(char.ToUpperInvariant(word[0]), word[1..])));
    }

    private static ConnectorFetchResult FormatChanged() =>
        ConnectorFetchResult.Failure(
            ConnectionHealth.FormatChanged,
            "This Codex CLI version returned an unsupported account usage payload.");
}

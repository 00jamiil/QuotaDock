using System.Text.Json;
using QuotaDock.Core.Domain;

namespace QuotaDock.Connectors.Anthropic;

/// <summary>
/// Reads Claude Code's local month-to-date token/cost log
/// (<c>~/.claude/metrics/costs.jsonl</c>). Each line is the latest cumulative
/// snapshot for a session; QuotaDock keeps only the last row per session and
/// sums the current calendar month. This is on-device data the official tool
/// already wrote; QuotaDock only reads it and stores normalized totals.
/// </summary>
public static class ClaudeCostLogReader
{
    public static string? DefaultCostLogPath()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (string.IsNullOrWhiteSpace(home))
        {
            return null;
        }

        return Path.Combine(home, ".claude", "metrics", "costs.jsonl");
    }

    public static IReadOnlyList<UsageMetric> ReadMonthToDate(string? path, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return [];
        }

        try
        {
            var latestPerSession = new Dictionary<string, (DateTimeOffset Ts, decimal In, decimal Out, decimal Cost)>(
                StringComparer.Ordinal);

            foreach (var line in File.ReadLines(path))
            {
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                try
                {
                    using var document = JsonDocument.Parse(line);
                    var root = document.RootElement;
                    if (root.ValueKind != JsonValueKind.Object ||
                        !root.TryGetProperty("timestamp", out var tsElement) ||
                        !DateTimeOffset.TryParse(
                            tsElement.GetString(),
                            System.Globalization.CultureInfo.InvariantCulture,
                            System.Globalization.DateTimeStyles.RoundtripKind,
                            out var ts))
                    {
                        continue;
                    }

                    // Cumulative snapshots must be deduplicated per session so the
                    // last row wins. Fall back to the transcript path when a
                    // session id is absent; only if neither exists do we treat the
                    // row as its own line (keyed by content) to avoid summing the
                    // same cumulative session multiple times.
                    string sessionId;
                    if (root.TryGetProperty("session_id", out var sid) && sid.ValueKind == JsonValueKind.String &&
                        sid.GetString() is { Length: > 0 } sidValue)
                    {
                        sessionId = sidValue;
                    }
                    else if (root.TryGetProperty("transcript_path", out var tp) &&
                             tp.ValueKind == JsonValueKind.String && tp.GetString() is { Length: > 0 } tpValue)
                    {
                        sessionId = "transcript:" + tpValue;
                    }
                    else
                    {
                        sessionId = "line:" + line.GetHashCode().ToString(System.Globalization.CultureInfo.InvariantCulture);
                    }

                    var entry = (
                        ts,
                        ReadNumber(root, "input_tokens"),
                        ReadNumber(root, "output_tokens"),
                        ReadNumber(root, "estimated_cost_usd"));

                    if (!latestPerSession.TryGetValue(sessionId, out var existing) || ts >= existing.Ts)
                    {
                        latestPerSession[sessionId] = entry;
                    }
                }
                catch (Exception exception) when (
                    exception is JsonException or InvalidOperationException or FormatException)
                {
                    // Skip malformed lines; never fabricate values from them.
                }
            }

            decimal inputTokens = 0m, outputTokens = 0m, cost = 0m;
            foreach (var entry in latestPerSession.Values)
            {
                if (entry.Ts.Year == now.Year && entry.Ts.Month == now.Month)
                {
                    inputTokens += entry.In;
                    outputTokens += entry.Out;
                    cost += entry.Cost;
                }
            }

            if (inputTokens == 0m && outputTokens == 0m && cost == 0m)
            {
                return [];
            }

            var metrics = new List<UsageMetric>
            {
                UsageMetric.Create("claude-mtd-input", "Input tokens (month)", MetricKind.Tokens,
                    MetricDirection.Used, inputTokens, null, "tokens", MetricScope.Monthly, null),
                UsageMetric.Create("claude-mtd-output", "Output tokens (month)", MetricKind.Tokens,
                    MetricDirection.Used, outputTokens, null, "tokens", MetricScope.Monthly, null)
            };
            if (cost > 0m)
            {
                metrics.Add(UsageMetric.Create("claude-mtd-cost", "Estimated cost (month)", MetricKind.Currency,
                    MetricDirection.Used, cost, null, "USD", MetricScope.Monthly, null));
            }

            return metrics;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    private static decimal ReadNumber(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) &&
        value.ValueKind == JsonValueKind.Number &&
        value.TryGetDecimal(out var number)
            ? number
            : 0m;
}

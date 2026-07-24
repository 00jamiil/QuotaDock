using System.Globalization;
using System.Text.RegularExpressions;
using QuotaDock.Core.Domain;

namespace QuotaDock.Connectors.Anthropic;

public static partial class ClaudeDashboardTextParser
{
    public static ConnectorFetchResult Parse(
        string connectionId,
        string accountLabel,
        string visibleText,
        DateTimeOffset capturedAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(accountLabel);
        if (string.IsNullOrWhiteSpace(visibleText))
        {
            return FormatChanged();
        }

        var metrics = new List<UsageMetric>();
        AddMetric(metrics, Session(), "claude-session", "Session", MetricScope.Session, visibleText);
        AddMetric(metrics, Weekly(), "claude-weekly", "Weekly", MetricScope.Weekly, visibleText);
        if (metrics.Count == 0)
        {
            return FormatChanged();
        }

        return ConnectorFetchResult.Success(new UsageSnapshot(
            connectionId,
            ProviderKind.Anthropic,
            accountLabel,
            DataSourceKind.DashboardReader,
            capturedAt,
            ConnectionHealth.Fresh,
            metrics,
            null));
    }

    private static void AddMetric(
        ICollection<UsageMetric> metrics,
        Regex expression,
        string id,
        string label,
        MetricScope scope,
        string visibleText)
    {
        var match = expression.Match(visibleText);
        if (!match.Success || !decimal.TryParse(
                match.Groups[1].Value,
                NumberStyles.Number,
                CultureInfo.InvariantCulture,
                out var percentage) || percentage is < 0m or > 100m)
        {
            return;
        }

        var directionText = match.Groups[2].Value;
        var remaining = string.Equals(directionText, "remaining", StringComparison.OrdinalIgnoreCase)
            ? percentage
            : 100m - percentage;

        DateTimeOffset? resetsAt = null;
        var tailLength = Math.Min(240, visibleText.Length - match.Index - match.Length);
        if (tailLength > 0)
        {
            var tail = visibleText.Substring(match.Index + match.Length, tailLength);
            var resetMatch = ResetTime().Match(tail);
            if (resetMatch.Success && DateTimeOffset.TryParse(
                    resetMatch.Groups[1].Value,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind,
                    out var reset))
            {
                resetsAt = reset;
            }
        }

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

    private static ConnectorFetchResult FormatChanged() =>
        ConnectorFetchResult.Failure(
            ConnectionHealth.FormatChanged,
            "Claude usage values were not found on the visible usage page.");

    [GeneratedRegex("(?is)(?:current\\s+session|5[- ]hour(?:\\s+limit)?)\\s*.{0,180}?([0-9]+(?:\\.[0-9]+)?)\\s*%\\s*(used|remaining)", RegexOptions.CultureInvariant)]
    private static partial Regex Session();

    [GeneratedRegex("(?is)(?:weekly\\s+limits?|all\\s+models)\\s*.{0,180}?([0-9]+(?:\\.[0-9]+)?)\\s*%\\s*(used|remaining)", RegexOptions.CultureInvariant)]
    private static partial Regex Weekly();

    [GeneratedRegex("(?i)resets?\\s+(?:at|on)\\s+(\\S+)", RegexOptions.CultureInvariant)]
    private static partial Regex ResetTime();
}

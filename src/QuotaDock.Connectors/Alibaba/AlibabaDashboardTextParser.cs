using System.Globalization;
using System.Text.RegularExpressions;
using QuotaDock.Core.Domain;

namespace QuotaDock.Connectors.Alibaba;

public static partial class AlibabaDashboardTextParser
{
    public static ConnectorFetchResult Parse(
        string connectionId,
        string visibleText,
        DateTimeOffset capturedAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionId);
        if (string.IsNullOrWhiteSpace(visibleText))
        {
            return FormatChanged();
        }

        var quota = DecimalCapture(Quota(), visibleText);
        var used = DecimalCapture(Used(), visibleText);
        var remaining = DecimalCapture(Remaining(), visibleText);
        if (quota is null or <= 0m || used is null or < 0m || remaining is null or < 0m)
        {
            return FormatChanged();
        }

        var plan = Plan().Match(visibleText).Groups[1].Value.Trim();
        if (string.IsNullOrWhiteSpace(plan))
        {
            plan = "Token Plan";
        }

        var accountMatch = AccountEmail().Match(visibleText);
        var account = accountMatch.Success ? accountMatch.Groups[1].Value : "Alibaba account";

        DateTimeOffset? resetsAt = null;
        var resetMatch = ResetTime().Match(visibleText);
        if (resetMatch.Success && DateTimeOffset.TryParse(
                resetMatch.Groups[1].Value,
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind,
                out var reset))
        {
            resetsAt = reset;
        }

        var metrics = new List<UsageMetric>
        {
            UsageMetric.Create("alibaba-credits-remaining", "Credits remaining", MetricKind.Credits,
                MetricDirection.Remaining, remaining.Value, quota.Value, "credits", MetricScope.Monthly, resetsAt),
            UsageMetric.Create("alibaba-credits-used", "Credits used", MetricKind.Credits,
                MetricDirection.Used, used.Value, quota.Value, "credits", MetricScope.Monthly, resetsAt)
        };
        var modelSectionIndex = visibleText.IndexOf("Available models", StringComparison.OrdinalIgnoreCase);
        if (modelSectionIndex >= 0)
        {
            var modelSection = visibleText[modelSectionIndex..];
            foreach (Match modelMatch in ModelCredits().Matches(modelSection))
            {
                var name = modelMatch.Groups[1].Value.Trim();
                var credits = DecimalCaptureValue(modelMatch.Groups[2].Value);
                if (credits is null or < 0m)
                {
                    continue;
                }

                var id = Regex.Replace(name.ToLowerInvariant(), "[^a-z0-9]+", "-").Trim('-');
                metrics.Add(UsageMetric.Create(
                    $"alibaba-model-{id}",
                    name,
                    MetricKind.Credits,
                    MetricDirection.Used,
                    credits.Value,
                    null,
                    "credits",
                    MetricScope.Model,
                    resetsAt,
                    new Dictionary<string, string>(StringComparer.Ordinal) { ["model"] = name }));
            }
        }

        return ConnectorFetchResult.Success(new UsageSnapshot(
            connectionId,
            ProviderKind.Alibaba,
            $"{account} · {plan}",
            DataSourceKind.DashboardReader,
            capturedAt,
            ConnectionHealth.Fresh,
            metrics,
            null));
    }

    private static decimal? DecimalCapture(Regex expression, string value)
    {
        var match = expression.Match(value);
        if (!match.Success)
        {
            return null;
        }

        return DecimalCaptureValue(match.Groups[1].Value);
    }

    private static decimal? DecimalCaptureValue(string value)
    {
        var normalized = value.Replace(",", string.Empty, StringComparison.Ordinal);
        return decimal.TryParse(normalized, NumberStyles.Number, CultureInfo.InvariantCulture, out var number)
            ? number
            : null;
    }

    private static ConnectorFetchResult FormatChanged() =>
        ConnectorFetchResult.Failure(
            ConnectionHealth.FormatChanged,
            "Alibaba usage values were not found on the visible Token Plan page.");

    [GeneratedRegex("(?is)(?:total\\s+quota|quota)\\s*[:\\r\\n ]+\\s*([\\d,]+(?:\\.\\d+)?)\\s*credits", RegexOptions.CultureInvariant)]
    private static partial Regex Quota();

    [GeneratedRegex("(?is)(?:credits\\s+used|used|consumed)\\s*[:\\r\\n ]+\\s*([\\d,]+(?:\\.\\d+)?)\\s*credits", RegexOptions.CultureInvariant)]
    private static partial Regex Used();

    [GeneratedRegex("(?is)(?:credits\\s+remaining|remaining|available)\\s*[:\\r\\n ]+\\s*([\\d,]+(?:\\.\\d+)?)\\s*credits", RegexOptions.CultureInvariant)]
    private static partial Regex Remaining();

    [GeneratedRegex("(?im)^\\s*plan\\s*:\\s*([a-z][a-z ]{0,30})\\s*$", RegexOptions.CultureInvariant)]
    private static partial Regex Plan();

    [GeneratedRegex("(?i)(?:account\\s*:\\s*)?([a-z0-9._%+-]+@[a-z0-9.-]+\\.[a-z]{2,})", RegexOptions.CultureInvariant)]
    private static partial Regex AccountEmail();

    [GeneratedRegex("(?i)reset(?:\\s+time)?\\s*:\\s*(\\S+)", RegexOptions.CultureInvariant)]
    private static partial Regex ResetTime();

    [GeneratedRegex("(?im)^\\s*([a-z][a-z0-9._-]{2,64})\\s*:\\s*([\\d,]+(?:\\.\\d+)?)\\s*credits\\s*$", RegexOptions.CultureInvariant)]
    private static partial Regex ModelCredits();
}

using System.Globalization;
using System.Text.Json;
using QuotaDock.Connectors.Api;
using QuotaDock.Core.Domain;

namespace QuotaDock.Connectors.Alibaba;

public static class AlibabaDashboardPayloadParser
{
    private const string SupportedSignature = "token-plan-team-v1";

    public static ConnectorFetchResult Parse(
        string connectionId,
        string payload,
        DateTimeOffset capturedAt)
    {
        try
        {
            using var document = JsonDocument.Parse(payload);
            var root = document.RootElement;
            if (!string.Equals(
                    ApiConnectorSupport.String(root, "signature"),
                    SupportedSignature,
                    StringComparison.Ordinal))
            {
                return ConnectorFetchResult.Failure(
                    ConnectionHealth.FormatChanged,
                    "Alibaba changed the Token Plan usage page format.");
            }

            var account = ApiConnectorSupport.String(root, "account") ?? "Alibaba account";
            var plan = ApiConnectorSupport.String(root, "plan") ?? "Token Plan";
            var quota = ApiConnectorSupport.Decimal(root, "quota");
            var used = ApiConnectorSupport.Decimal(root, "used");
            var remaining = ApiConnectorSupport.Decimal(root, "remaining");
            if (quota <= 0m || used < 0m || remaining < 0m)
            {
                return ConnectorFetchResult.Failure(
                    ConnectionHealth.FormatChanged,
                    "Alibaba returned incomplete Token Plan quota data.");
            }

            DateTimeOffset? resetsAt = null;
            var resetText = ApiConnectorSupport.String(root, "resetsAt");
            if (!string.IsNullOrWhiteSpace(resetText) &&
                DateTimeOffset.TryParse(resetText, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsedReset))
            {
                resetsAt = parsedReset;
            }

            var metrics = new List<UsageMetric>
            {
                UsageMetric.Create("alibaba-credits-remaining", "Credits remaining", MetricKind.Credits,
                    MetricDirection.Remaining, remaining, quota, "credits", MetricScope.Monthly, resetsAt),
                UsageMetric.Create("alibaba-credits-used", "Credits used", MetricKind.Credits,
                    MetricDirection.Used, used, quota, "credits", MetricScope.Monthly, resetsAt)
            };

            if (root.TryGetProperty("models", out var models) && models.ValueKind == JsonValueKind.Array)
            {
                var index = 0;
                foreach (var model in models.EnumerateArray())
                {
                    var name = ApiConnectorSupport.String(model, "name");
                    var credits = ApiConnectorSupport.Decimal(model, "credits");
                    if (string.IsNullOrWhiteSpace(name) || credits < 0m)
                    {
                        continue;
                    }

                    metrics.Add(UsageMetric.Create(
                        $"alibaba-model-{index++}",
                        name,
                        MetricKind.Credits,
                        MetricDirection.Used,
                        credits,
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
        catch (JsonException)
        {
            return ConnectorFetchResult.Failure(
                ConnectionHealth.FormatChanged,
                "Alibaba returned an unrecognized Token Plan usage payload.");
        }
    }
}


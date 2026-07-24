using System.Globalization;
using System.Text.Json;
using QuotaDock.Connectors.Api;
using QuotaDock.Core.Domain;

namespace QuotaDock.Connectors.Personal;

public static class PersonalUsagePayloadParser
{
    private const string SupportedSchema = "quotadock.personal-usage.v1";

    public static ConnectorFetchResult Parse(
        ProviderKind provider,
        string connectionId,
        DataSourceKind source,
        string payload,
        DateTimeOffset capturedAt)
    {
        try
        {
            using var document = JsonDocument.Parse(payload);
            var root = document.RootElement;
            if (!string.Equals(ApiConnectorSupport.String(root, "schema"), SupportedSchema, StringComparison.Ordinal))
            {
                return FormatChanged(provider);
            }

            if (!root.TryGetProperty("metrics", out var metricsElement) || metricsElement.ValueKind != JsonValueKind.Array)
            {
                return FormatChanged(provider);
            }

            var metrics = new List<UsageMetric>();
            foreach (var element in metricsElement.EnumerateArray())
            {
                var id = ApiConnectorSupport.String(element, "id");
                var label = ApiConnectorSupport.String(element, "label");
                var unit = ApiConnectorSupport.String(element, "unit");
                if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(label) || string.IsNullOrWhiteSpace(unit))
                {
                    return FormatChanged(provider);
                }

                var kind = ParseEnum<MetricKind>(ApiConnectorSupport.String(element, "kind"));
                var direction = ParseEnum<MetricDirection>(ApiConnectorSupport.String(element, "direction"));
                var scope = ParseEnum<MetricScope>(ApiConnectorSupport.String(element, "scope"));
                if (kind is null || direction is null || scope is null)
                {
                    return FormatChanged(provider);
                }

                decimal? limit = element.TryGetProperty("limit", out var limitElement) &&
                                 limitElement.ValueKind != JsonValueKind.Null
                    ? ApiConnectorSupport.Decimal(element, "limit")
                    : null;
                DateTimeOffset? resetsAt = null;
                var resetText = ApiConnectorSupport.String(element, "resetsAt");
                if (!string.IsNullOrWhiteSpace(resetText) &&
                    DateTimeOffset.TryParse(resetText, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var reset))
                {
                    resetsAt = reset;
                }

                metrics.Add(UsageMetric.Create(
                    id,
                    label,
                    kind.Value,
                    direction.Value,
                    ApiConnectorSupport.Decimal(element, "value"),
                    limit,
                    unit,
                    scope.Value,
                    resetsAt));
            }

            return ConnectorFetchResult.Success(new UsageSnapshot(
                connectionId,
                provider,
                ApiConnectorSupport.String(root, "account") ?? "Personal",
                source,
                capturedAt,
                ConnectionHealth.Fresh,
                metrics,
                null));
        }
        catch (Exception exception) when (exception is JsonException or ArgumentException)
        {
            return FormatChanged(provider);
        }
    }

    private static TEnum? ParseEnum<TEnum>(string? value) where TEnum : struct, Enum
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return Enum.TryParse<TEnum>(value, ignoreCase: true, out var parsed) ? parsed : null;
    }

    private static ConnectorFetchResult FormatChanged(ProviderKind provider) =>
        ConnectorFetchResult.Failure(
            ConnectionHealth.FormatChanged,
            $"The {provider} personal usage format is not supported by this QuotaDock version.");
}

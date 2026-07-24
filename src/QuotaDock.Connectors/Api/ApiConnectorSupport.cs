using System.Globalization;
using System.Text.Json;

namespace QuotaDock.Connectors.Api;

internal static class ApiConnectorSupport
{
    public static decimal Decimal(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var value))
        {
            return 0m;
        }

        return value.ValueKind switch
        {
            JsonValueKind.Number when value.TryGetDecimal(out var number) => number,
            JsonValueKind.String when decimal.TryParse(
                value.GetString(), NumberStyles.Number, CultureInfo.InvariantCulture, out var textNumber) => textNumber,
            _ => 0m
        };
    }

    public static string? String(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
}

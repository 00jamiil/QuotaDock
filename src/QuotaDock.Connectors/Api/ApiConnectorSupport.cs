using System.Globalization;
using System.Net;
using System.Text.Json;
using QuotaDock.Core.Domain;

namespace QuotaDock.Connectors.Api;

internal sealed class ConnectorApiException(
    ConnectionHealth health,
    string userMessage,
    TimeSpan? retryAfter = null,
    Exception? innerException = null) : Exception(userMessage, innerException)
{
    public ConnectionHealth Health { get; } = health;
    public TimeSpan? RetryAfter { get; } = retryAfter;
}

internal static class ApiConnectorSupport
{
    public static async Task<JsonDocument> SendForJsonAsync(
        HttpClient client,
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        using (request)
        using (var response = await client.SendAsync(
                   request,
                   HttpCompletionOption.ResponseHeadersRead,
                   cancellationToken).ConfigureAwait(false))
        {
            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            {
                throw new ConnectorApiException(
                    ConnectionHealth.AuthenticationRequired,
                    "The provider rejected this admin credential.");
            }

            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                var retryAfter = response.Headers.RetryAfter?.Delta;
                if (retryAfter is null && response.Headers.RetryAfter?.Date is { } retryDate)
                {
                    retryAfter = retryDate - DateTimeOffset.UtcNow;
                }

                throw new ConnectorApiException(
                    ConnectionHealth.RateLimited,
                    "The provider asked QuotaDock to slow down.",
                    retryAfter.HasValue && retryAfter.Value > TimeSpan.Zero ? retryAfter : null);
            }

            if (!response.IsSuccessStatusCode)
            {
                throw new ConnectorApiException(
                    ConnectionHealth.Unavailable,
                    $"The provider usage service returned HTTP {(int)response.StatusCode}.");
            }

            try
            {
                await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
                return await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
            }
            catch (JsonException exception)
            {
                throw new ConnectorApiException(
                    ConnectionHealth.FormatChanged,
                    "The provider returned an unrecognized usage payload.",
                    innerException: exception);
            }
        }
    }

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

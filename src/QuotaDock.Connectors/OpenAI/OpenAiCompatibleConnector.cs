using System.Net.Http.Headers;
using System.Text.Json;
using QuotaDock.Connectors.Api;
using QuotaDock.Core.Abstractions;
using QuotaDock.Core.Domain;

namespace QuotaDock.Connectors.OpenAI;

public sealed class OpenAiCompatibleConnector(
    HttpClient httpClient,
    ISecretVault secretVault,
    TimeProvider timeProvider) : IUsageConnector
{
    public const string BaseUrlSetting = "base_url";
    public const string ModelSetting = "model";
    public const string UsageUrlSetting = "usage_url";

    private const int MaximumPages = 100;

    public ConnectorDefinition Definition { get; } = new(
        "openai-compatible",
        ProviderKind.OpenAI,
        "OpenAI-compatible provider",
        DataSourceKind.OfficialApi,
        ConnectorCapabilities.Tokens | ConnectorCapabilities.Requests,
        RequiresSecret: false);

    public async Task<ConnectorConnection> ConnectAsync(
        ConnectionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Source != DataSourceKind.OfficialApi)
        {
            throw new ArgumentException("This connector requires an API source.", nameof(request));
        }

        var baseUri = ReadAndValidateBaseUri(request.Settings);
        var model = ReadModel(request.Settings);
        var usageUri = ReadAndValidateUsageUri(request.Settings, baseUri);
        var id = $"openai-compatible-{Guid.NewGuid():N}";
        string? secretReference = null;
        if (!string.IsNullOrWhiteSpace(request.Secret))
        {
            secretReference = $"connector-{id}";
            await secretVault.SaveAsync(secretReference, request.Secret, cancellationToken).ConfigureAwait(false);
        }

        var settings = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [BaseUrlSetting] = baseUri.AbsoluteUri,
            [ModelSetting] = model,
            [UsageUrlSetting] = usageUri?.AbsoluteUri ?? string.Empty
        };
        return new ConnectorConnection(
            id,
            ProviderKind.OpenAI,
            request.AccountLabel,
            DataSourceKind.OfficialApi,
            secretReference,
            settings);
    }

    public async Task<ConnectionValidationResult> ValidateAsync(
        ConnectorConnection connection,
        CancellationToken cancellationToken = default)
    {
        var result = await FetchAsync(connection, cancellationToken).ConfigureAwait(false);
        return result.IsSuccess
            ? ConnectionValidationResult.Valid()
            : ConnectionValidationResult.Invalid(result.Message ?? "The compatible provider could not be validated.");
    }

    public async Task<ConnectorFetchResult> FetchAsync(
        ConnectorConnection connection,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);
        try
        {
            var settings = connection.Settings ??
                           throw new ArgumentException("Compatible-provider settings are missing.", nameof(connection));
            var baseUri = ReadAndValidateBaseUri(settings);
            var model = ReadModel(settings);
            var usageUri = ReadAndValidateUsageUri(settings, baseUri);
            var secret = await ReadSecretAsync(connection, cancellationToken).ConfigureAwait(false);

            if (!await ModelExistsAsync(baseUri, model, secret, cancellationToken).ConfigureAwait(false))
            {
                return ConnectorFetchResult.Failure(
                    ConnectionHealth.Unavailable,
                    $"Model '{model}' was not returned by the provider's models endpoint.");
            }

            if (usageUri is null)
            {
                return ConnectorFetchResult.Success(new UsageSnapshot(
                    connection.Id,
                    ProviderKind.OpenAI,
                    connection.AccountLabel,
                    DataSourceKind.OfficialApi,
                    timeProvider.GetUtcNow(),
                    ConnectionHealth.Fresh,
                    [],
                    "Model available; aggregate usage endpoint is not configured."));
            }

            var totals = await ReadUsageAsync(usageUri, secret, cancellationToken).ConfigureAwait(false);
            return ConnectorFetchResult.Success(new UsageSnapshot(
                connection.Id,
                ProviderKind.OpenAI,
                connection.AccountLabel,
                DataSourceKind.OfficialApi,
                timeProvider.GetUtcNow(),
                ConnectionHealth.Fresh,
                [
                    UsageMetric.Create(
                        "compatible-input-tokens", "Input tokens", MetricKind.Tokens,
                        MetricDirection.Used, totals.InputTokens, null, "tokens", MetricScope.Monthly, null),
                    UsageMetric.Create(
                        "compatible-output-tokens", "Output tokens", MetricKind.Tokens,
                        MetricDirection.Used, totals.OutputTokens, null, "tokens", MetricScope.Monthly, null),
                    UsageMetric.Create(
                        "compatible-requests", "Requests", MetricKind.Requests,
                        MetricDirection.Used, totals.Requests, null, "requests", MetricScope.Monthly, null)
                ],
                null));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (ConnectorApiException exception)
        {
            return ConnectorFetchResult.Failure(exception.Health, exception.Message, exception.RetryAfter);
        }
        catch (HttpRequestException)
        {
            return ConnectorFetchResult.Failure(
                ConnectionHealth.Unavailable,
                "The compatible provider could not be reached.");
        }
        catch (JsonException)
        {
            return ConnectorFetchResult.Failure(
                ConnectionHealth.FormatChanged,
                "The compatible provider returned an unrecognized response.");
        }
        catch (ArgumentException exception)
        {
            return ConnectorFetchResult.Failure(ConnectionHealth.FormatChanged, exception.Message);
        }
    }

    public async Task DisconnectAsync(
        ConnectorConnection connection,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);
        if (!string.IsNullOrWhiteSpace(connection.SecretReference))
        {
            await secretVault.RemoveAsync(connection.SecretReference, cancellationToken).ConfigureAwait(false);
        }
    }

    public static HttpClientHandler CreateSecureHandler() => new()
    {
        AllowAutoRedirect = false
    };

    private async Task<bool> ModelExistsAsync(
        Uri baseUri,
        string model,
        string? secret,
        CancellationToken cancellationToken)
    {
        var modelsUri = ModelsUri(baseUri);
        using var document = await SendAsync(modelsUri, secret, cancellationToken).ConfigureAwait(false);
        if (!document.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
        {
            throw new ConnectorApiException(
                ConnectionHealth.FormatChanged,
                "The provider's models endpoint returned an unrecognized response.");
        }

        foreach (var item in data.EnumerateArray())
        {
            if (item.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.String &&
                string.Equals(id.GetString(), model, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private async Task<UsageTotals> ReadUsageAsync(
        Uri firstPage,
        string? secret,
        CancellationToken cancellationToken)
    {
        var totals = new UsageTotals();
        Uri? current = firstPage;
        for (var page = 0; current is not null && page < MaximumPages; page++)
        {
            using var document = await SendAsync(current, secret, cancellationToken).ConfigureAwait(false);
            var root = document.RootElement;
            if (!root.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
            {
                throw FormatChanged();
            }

            foreach (var bucket in data.EnumerateArray())
            {
                if (!bucket.TryGetProperty("results", out var results) || results.ValueKind != JsonValueKind.Array)
                {
                    throw FormatChanged();
                }

                foreach (var result in results.EnumerateArray())
                {
                    if (!HasUsageField(result))
                    {
                        throw FormatChanged();
                    }

                    totals.InputTokens += ApiConnectorSupport.Decimal(result, "input_tokens");
                    totals.OutputTokens += ApiConnectorSupport.Decimal(result, "output_tokens");
                    totals.Requests += ApiConnectorSupport.Decimal(result, "num_model_requests");
                }
            }

            var hasMore = root.TryGetProperty("has_more", out var hasMoreValue) &&
                          hasMoreValue.ValueKind is JsonValueKind.True;
            if (!hasMore)
            {
                current = null;
                continue;
            }

            var nextPage = ApiConnectorSupport.String(root, "next_page");
            if (string.IsNullOrWhiteSpace(nextPage))
            {
                throw FormatChanged();
            }

            current = WithPage(firstPage, nextPage);
        }

        if (current is not null)
        {
            throw new ConnectorApiException(
                ConnectionHealth.Unavailable,
                "The usage endpoint returned too many pagination pages.");
        }

        return totals;
    }

    private Task<JsonDocument> SendAsync(
        Uri uri,
        string? secret,
        CancellationToken cancellationToken)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, uri);
        if (!string.IsNullOrWhiteSpace(secret))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", secret);
        }

        return ApiConnectorSupport.SendForJsonAsync(httpClient, request, cancellationToken);
    }

    private async ValueTask<string?> ReadSecretAsync(
        ConnectorConnection connection,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(connection.SecretReference))
        {
            return null;
        }

        return await secretVault.RetrieveAsync(connection.SecretReference, cancellationToken).ConfigureAwait(false)
               ?? throw new ConnectorApiException(
                   ConnectionHealth.AuthenticationRequired,
                   "The API key for this compatible provider is missing.");
    }

    private static Uri ReadAndValidateBaseUri(IReadOnlyDictionary<string, string> settings)
    {
        if (!settings.TryGetValue(BaseUrlSetting, out var value) || string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("A compatible-provider base URL is required.", nameof(settings));
        }

        var uri = ValidateEndpoint(value, "base URL");
        if (!string.IsNullOrEmpty(uri.Query))
        {
            throw new ArgumentException("The compatible-provider base URL cannot contain a query string.", nameof(settings));
        }

        return EnsureTrailingSlash(uri);
    }

    private static string ReadModel(IReadOnlyDictionary<string, string> settings)
    {
        if (!settings.TryGetValue(ModelSetting, out var value) || string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("A model ID is required.", nameof(settings));
        }

        return value.Trim();
    }

    private static Uri? ReadAndValidateUsageUri(
        IReadOnlyDictionary<string, string> settings,
        Uri baseUri)
    {
        if (!settings.TryGetValue(UsageUrlSetting, out var value) || string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var usageUri = ValidateEndpoint(value, "aggregate usage URL");
        if (ContainsSecretQueryParameter(usageUri))
        {
            throw new ArgumentException(
                "The aggregate usage URL cannot contain secret query parameters. Store the API key in Windows Credential Manager instead.",
                nameof(settings));
        }

        if (!SameOrigin(baseUri, usageUri))
        {
            throw new ArgumentException(
                "The aggregate usage URL must use the same origin as the base URL.",
                nameof(settings));
        }

        return usageUri;
    }

    private static Uri ValidateEndpoint(string value, string label)
    {
        if (!Uri.TryCreate(value.Trim(), UriKind.Absolute, out var uri) ||
            string.IsNullOrWhiteSpace(uri.Host))
        {
            throw new ArgumentException($"The {label} must be an absolute URL.", nameof(value));
        }

        var secure = string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase);
        var localHttp = string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) &&
                        uri.IsLoopback;
        if (!secure && !localHttp)
        {
            throw new ArgumentException(
                $"The {label} must use HTTPS, except for loopback development servers.",
                nameof(value));
        }

        if (!string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Fragment))
        {
            throw new ArgumentException(
                $"The {label} cannot contain credentials or a fragment.",
                nameof(value));
        }

        return uri;
    }

    private static Uri ModelsUri(Uri baseUri)
    {
        var path = baseUri.AbsolutePath.TrimEnd('/');
        return path.EndsWith("/v1", StringComparison.OrdinalIgnoreCase)
            ? new Uri(baseUri, "models")
            : new Uri(baseUri, "v1/models");
    }

    private static Uri EnsureTrailingSlash(Uri uri) =>
        uri.AbsolutePath.EndsWith("/", StringComparison.Ordinal)
            ? uri
            : new UriBuilder(uri) { Path = $"{uri.AbsolutePath}/" }.Uri;

    private static bool SameOrigin(Uri left, Uri right) =>
        string.Equals(left.Scheme, right.Scheme, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(left.Host, right.Host, StringComparison.OrdinalIgnoreCase) &&
        left.Port == right.Port;

    private static bool ContainsSecretQueryParameter(Uri uri)
    {
        foreach (var pair in uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var separator = pair.IndexOf('=');
            var encodedName = separator >= 0 ? pair[..separator] : pair;
            var name = Uri.UnescapeDataString(encodedName.Replace('+', ' '))
                .Replace('-', '_')
                .ToLowerInvariant();
            if (name is "key" or "auth" or "authorization" or "credential" or "credentials" or "password" ||
                name.EndsWith("_key", StringComparison.Ordinal) ||
                name.Contains("token", StringComparison.Ordinal) ||
                name.Contains("secret", StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static Uri WithPage(Uri uri, string page)
    {
        var builder = new UriBuilder(uri);
        var existing = builder.Query.TrimStart('?');
        builder.Query = string.IsNullOrEmpty(existing)
            ? $"page={Uri.EscapeDataString(page)}"
            : $"{existing}&page={Uri.EscapeDataString(page)}";
        return builder.Uri;
    }

    private static bool HasUsageField(JsonElement result) =>
        result.TryGetProperty("input_tokens", out _) ||
        result.TryGetProperty("output_tokens", out _) ||
        result.TryGetProperty("num_model_requests", out _);

    private static ConnectorApiException FormatChanged() => new(
        ConnectionHealth.FormatChanged,
        "The aggregate usage endpoint returned an unrecognized response.");

    private sealed class UsageTotals
    {
        public decimal InputTokens { get; set; }
        public decimal OutputTokens { get; set; }
        public decimal Requests { get; set; }
    }
}

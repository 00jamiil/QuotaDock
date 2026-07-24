using System.Globalization;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using QuotaDock.Connectors.Api;
using QuotaDock.Core.Abstractions;
using QuotaDock.Core.Domain;

namespace QuotaDock.Connectors.OpenAI;

public sealed class OpenAiOrganizationConnector(
    HttpClient httpClient,
    ISecretVault secretVault,
    TimeProvider timeProvider) : AdminApiConnectorBase(secretVault, timeProvider)
{
    public override ConnectorDefinition Definition { get; } = new(
        "openai-organization",
        ProviderKind.OpenAI,
        "OpenAI organization",
        DataSourceKind.OfficialApi,
        ConnectorCapabilities.Tokens |
        ConnectorCapabilities.Requests |
        ConnectorCapabilities.Costs |
        ConnectorCapabilities.ProjectBreakdown,
        RequiresSecret: true);

    public override async Task<ConnectorFetchResult> FetchAsync(
        ConnectorConnection connection,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);
        var secret = await GetSecretAsync(connection, cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(secret))
        {
            return ConnectorFetchResult.Failure(
                ConnectionHealth.AuthenticationRequired,
                "The OpenAI admin key is missing from Windows Credential Locker.");
        }

        try
        {
            var monthStart = new DateTimeOffset(
                TimeProvider.GetUtcNow().Year,
                TimeProvider.GetUtcNow().Month,
                1,
                0,
                0,
                0,
                TimeSpan.Zero);
            var start = monthStart.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
            var inputTokens = 0m;
            var outputTokens = 0m;
            var requests = 0m;
            var spend = 0m;
            var projects = new Dictionary<string, decimal[]>(StringComparer.Ordinal);

            string? page = null;
            do
            {
                var uri = $"v1/organization/usage/completions?start_time={start}&bucket_width=1d&limit=31&group_by=project_id";
                if (!string.IsNullOrWhiteSpace(page))
                {
                    uri += $"&page={Uri.EscapeDataString(page)}";
                }

                using var document = await SendAsync(uri, secret, cancellationToken).ConfigureAwait(false);
                foreach (var result in EnumerateResults(document.RootElement))
                {
                    inputTokens += ApiConnectorSupport.Decimal(result, "input_tokens");
                    outputTokens += ApiConnectorSupport.Decimal(result, "output_tokens");
                    requests += ApiConnectorSupport.Decimal(result, "num_model_requests");
                    var project = ApiConnectorSupport.String(result, "project_id") ?? "default";
                    if (!projects.TryGetValue(project, out var values))
                    {
                        values = new decimal[4];
                        projects[project] = values;
                    }

                    values[0] += ApiConnectorSupport.Decimal(result, "input_tokens");
                    values[1] += ApiConnectorSupport.Decimal(result, "output_tokens");
                    values[2] += ApiConnectorSupport.Decimal(result, "num_model_requests");
                }

                page = NextPage(document.RootElement);
            }
            while (page is not null);

            page = null;
            do
            {
                var uri = $"v1/organization/costs?start_time={start}&bucket_width=1d&limit=31&group_by=project_id";
                if (!string.IsNullOrWhiteSpace(page))
                {
                    uri += $"&page={Uri.EscapeDataString(page)}";
                }

                using var document = await SendAsync(uri, secret, cancellationToken).ConfigureAwait(false);
                foreach (var result in EnumerateResults(document.RootElement))
                {
                    if (result.TryGetProperty("amount", out var amount))
                    {
                        var amountValue = ApiConnectorSupport.Decimal(amount, "value");
                        spend += amountValue;
                        var project = ApiConnectorSupport.String(result, "project_id") ?? "default";
                        if (!projects.TryGetValue(project, out var values))
                        {
                            values = new decimal[4];
                            projects[project] = values;
                        }

                        values[3] += amountValue;
                    }
                }

                page = NextPage(document.RootElement);
            }
            while (page is not null);

            var capturedAt = TimeProvider.GetUtcNow();
            var resetAt = monthStart.AddMonths(1);
            var metrics = new List<UsageMetric>
            {
                UsageMetric.Create("openai-input-tokens", "Input tokens", MetricKind.Tokens,
                    MetricDirection.Used, inputTokens, null, "tokens", MetricScope.Monthly, resetAt),
                UsageMetric.Create("openai-output-tokens", "Output tokens", MetricKind.Tokens,
                    MetricDirection.Used, outputTokens, null, "tokens", MetricScope.Monthly, resetAt),
                UsageMetric.Create("openai-requests", "Requests", MetricKind.Requests,
                    MetricDirection.Used, requests, null, "requests", MetricScope.Monthly, resetAt),
                UsageMetric.Create("openai-spend", "Month-to-date spend", MetricKind.Currency,
                    MetricDirection.Used, spend, null, "USD", MetricScope.Monthly, resetAt)
            };
            foreach (var (project, values) in projects.OrderBy(item => item.Key, StringComparer.Ordinal))
            {
                var dimensions = new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["project_id"] = project
                };
                var stableId = StableId(project);
                metrics.Add(UsageMetric.Create(
                    $"openai-project-{stableId}-tokens",
                    $"{project} tokens",
                    MetricKind.Tokens,
                    MetricDirection.Used,
                    values[0] + values[1],
                    null,
                    "tokens",
                    MetricScope.Project,
                    resetAt,
                    dimensions));
                metrics.Add(UsageMetric.Create(
                    $"openai-project-{stableId}-spend",
                    $"{project} spend",
                    MetricKind.Currency,
                    MetricDirection.Used,
                    values[3],
                    null,
                    "USD",
                    MetricScope.Project,
                    resetAt,
                    dimensions));
            }

            return ConnectorFetchResult.Success(new UsageSnapshot(
                connection.Id,
                ProviderKind.OpenAI,
                connection.AccountLabel,
                DataSourceKind.OfficialApi,
                capturedAt,
                ConnectionHealth.Fresh,
                metrics,
                null));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            return Failure(exception);
        }
    }

    private async Task<JsonDocument> SendAsync(
        string relativeUri,
        string secret,
        CancellationToken cancellationToken)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, relativeUri);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", secret);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return await ApiConnectorSupport.SendForJsonAsync(httpClient, request, cancellationToken).ConfigureAwait(false);
    }

    private static IEnumerable<JsonElement> EnumerateResults(JsonElement root)
    {
        if (!root.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
        {
            yield break;
        }

        foreach (var bucket in data.EnumerateArray())
        {
            if (!bucket.TryGetProperty("results", out var results) || results.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (var result in results.EnumerateArray())
            {
                yield return result;
            }
        }
    }

    private static string? NextPage(JsonElement root)
    {
        var hasMore = root.TryGetProperty("has_more", out var more) && more.ValueKind == JsonValueKind.True;
        return hasMore ? ApiConnectorSupport.String(root, "next_page") : null;
    }

    private static string StableId(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)))[..12].ToLowerInvariant();
}

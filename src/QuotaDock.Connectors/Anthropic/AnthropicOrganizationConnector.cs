using System.Globalization;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using QuotaDock.Connectors.Api;
using QuotaDock.Core.Abstractions;
using QuotaDock.Core.Domain;

namespace QuotaDock.Connectors.Anthropic;

public sealed class AnthropicOrganizationConnector(
    HttpClient httpClient,
    ISecretVault secretVault,
    TimeProvider timeProvider) : AdminApiConnectorBase(secretVault, timeProvider)
{
    public override ConnectorDefinition Definition { get; } = new(
        "anthropic-organization",
        ProviderKind.Anthropic,
        "Anthropic organization",
        DataSourceKind.OfficialApi,
        ConnectorCapabilities.Tokens |
        ConnectorCapabilities.Costs |
        ConnectorCapabilities.ProjectBreakdown |
        ConnectorCapabilities.ModelBreakdown,
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
                "The Anthropic admin key is missing from Windows Credential Locker.");
        }

        try
        {
            var now = TimeProvider.GetUtcNow();
            var monthStart = new DateTimeOffset(now.Year, now.Month, 1, 0, 0, 0, TimeSpan.Zero);
            var start = Uri.EscapeDataString(monthStart.ToString("O", CultureInfo.InvariantCulture));
            var end = Uri.EscapeDataString(now.AddMinutes(1).ToString("O", CultureInfo.InvariantCulture));
            var inputTokens = 0m;
            var outputTokens = 0m;
            var spendUsd = 0m;
            var groupedUsage = new Dictionary<string, GroupedUsage>(StringComparer.Ordinal);
            var groupedCosts = new Dictionary<string, decimal>(StringComparer.Ordinal);

            string? page = null;
            do
            {
                var uri = $"v1/organizations/usage_report/messages?starting_at={start}&ending_at={end}&bucket_width=1d&limit=31&group_by%5B%5D=workspace_id&group_by%5B%5D=model";
                if (!string.IsNullOrWhiteSpace(page))
                {
                    uri += $"&page={Uri.EscapeDataString(page)}";
                }

                using var document = await SendAsync(uri, secret, cancellationToken).ConfigureAwait(false);
                foreach (var result in EnumerateResults(document.RootElement))
                {
                    inputTokens += ApiConnectorSupport.Decimal(result, "uncached_input_tokens");
                    inputTokens += ApiConnectorSupport.Decimal(result, "cache_read_input_tokens");
                    if (result.TryGetProperty("cache_creation", out var cacheCreation) &&
                        cacheCreation.ValueKind == JsonValueKind.Object)
                    {
                        inputTokens += ApiConnectorSupport.Decimal(cacheCreation, "ephemeral_5m_input_tokens");
                        inputTokens += ApiConnectorSupport.Decimal(cacheCreation, "ephemeral_1h_input_tokens");
                    }

                    outputTokens += ApiConnectorSupport.Decimal(result, "output_tokens");

                    var workspace = ApiConnectorSupport.String(result, "workspace_id") ?? "default";
                    var model = ApiConnectorSupport.String(result, "model") ?? "unattributed";
                    var key = $"{workspace}\u001f{model}";
                    groupedUsage.TryGetValue(key, out var grouped);
                    grouped ??= new GroupedUsage(workspace, model, 0m, 0m);
                    var groupedInput = ApiConnectorSupport.Decimal(result, "uncached_input_tokens") +
                                       ApiConnectorSupport.Decimal(result, "cache_read_input_tokens");
                    if (result.TryGetProperty("cache_creation", out var groupedCache) &&
                        groupedCache.ValueKind == JsonValueKind.Object)
                    {
                        groupedInput += ApiConnectorSupport.Decimal(groupedCache, "ephemeral_5m_input_tokens");
                        groupedInput += ApiConnectorSupport.Decimal(groupedCache, "ephemeral_1h_input_tokens");
                    }

                    groupedUsage[key] = grouped with
                    {
                        Input = grouped.Input + groupedInput,
                        Output = grouped.Output + ApiConnectorSupport.Decimal(result, "output_tokens")
                    };
                }

                page = NextPage(document.RootElement);
            }
            while (page is not null);

            page = null;
            do
            {
                var uri = $"v1/organizations/cost_report?starting_at={start}&ending_at={end}&bucket_width=1d&limit=31&group_by%5B%5D=workspace_id&group_by%5B%5D=description";
                if (!string.IsNullOrWhiteSpace(page))
                {
                    uri += $"&page={Uri.EscapeDataString(page)}";
                }

                using var document = await SendAsync(uri, secret, cancellationToken).ConfigureAwait(false);
                foreach (var result in EnumerateResults(document.RootElement))
                {
                    var amount = ApiConnectorSupport.Decimal(result, "amount") / 100m;
                    spendUsd += amount;
                    var workspace = ApiConnectorSupport.String(result, "workspace_id") ?? "default";
                    var description = ApiConnectorSupport.String(result, "description") ?? "unattributed";
                    var key = $"{workspace}\u001f{description}";
                    groupedCosts[key] = groupedCosts.GetValueOrDefault(key) + amount;
                }

                page = NextPage(document.RootElement);
            }
            while (page is not null);

            var resetAt = monthStart.AddMonths(1);
            var metrics = new List<UsageMetric>
            {
                UsageMetric.Create("anthropic-input-tokens", "Input tokens", MetricKind.Tokens,
                    MetricDirection.Used, inputTokens, null, "tokens", MetricScope.Monthly, resetAt),
                UsageMetric.Create("anthropic-output-tokens", "Output tokens", MetricKind.Tokens,
                    MetricDirection.Used, outputTokens, null, "tokens", MetricScope.Monthly, resetAt),
                UsageMetric.Create("anthropic-spend", "Month-to-date spend", MetricKind.Currency,
                    MetricDirection.Used, spendUsd, null, "USD", MetricScope.Monthly, resetAt)
            };
            foreach (var grouped in groupedUsage.Values
                         .OrderBy(item => item.Workspace, StringComparer.Ordinal)
                         .ThenBy(item => item.Model, StringComparer.Ordinal))
            {
                var dimensions = new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["workspace_id"] = grouped.Workspace,
                    ["model"] = grouped.Model
                };
                metrics.Add(UsageMetric.Create(
                    $"anthropic-workspace-{StableId(grouped.Workspace + grouped.Model)}-tokens",
                    $"{grouped.Workspace} · {grouped.Model} tokens",
                    MetricKind.Tokens,
                    MetricDirection.Used,
                    grouped.Input + grouped.Output,
                    null,
                    "tokens",
                    MetricScope.Project,
                    resetAt,
                    dimensions));
            }

            foreach (var (key, amount) in groupedCosts.OrderBy(item => item.Key, StringComparer.Ordinal))
            {
                var parts = key.Split('\u001f', 2);
                var workspace = parts[0];
                var description = parts[1];
                metrics.Add(UsageMetric.Create(
                    $"anthropic-workspace-{StableId(key)}-spend",
                    $"{workspace} · {description} spend",
                    MetricKind.Currency,
                    MetricDirection.Used,
                    amount,
                    null,
                    "USD",
                    MetricScope.Project,
                    resetAt,
                    new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["workspace_id"] = workspace,
                        ["model"] = description
                    }));
            }

            return ConnectorFetchResult.Success(new UsageSnapshot(
                connection.Id,
                ProviderKind.Anthropic,
                connection.AccountLabel,
                DataSourceKind.OfficialApi,
                now,
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
        request.Headers.Add("x-api-key", secret);
        request.Headers.Add("anthropic-version", "2023-06-01");
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
            if (bucket.TryGetProperty("results", out var results) && results.ValueKind == JsonValueKind.Array)
            {
                foreach (var result in results.EnumerateArray())
                {
                    yield return result;
                }
            }
            else
            {
                yield return bucket;
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

    private sealed record GroupedUsage(string Workspace, string Model, decimal Input, decimal Output);
}

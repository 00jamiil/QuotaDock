using System.Net;
using QuotaDock.Connectors.OpenAI;
using QuotaDock.Core.Domain;

namespace QuotaDock.Connectors.Tests;

public sealed class OpenAiOrganizationConnectorTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 7, 23, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task FetchAsync_AggregatesPaginatedUsageAndCosts()
    {
        var handler = new StubHttpMessageHandler(request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            var query = request.RequestUri.Query;
            if (path.EndsWith("/usage/completions", StringComparison.Ordinal) && query.Contains("page=usage-next", StringComparison.Ordinal))
            {
                return StubHttpMessageHandler.Json("""
                    {"data":[{"results":[{"input_tokens":300,"output_tokens":200,"num_model_requests":3,"project_id":"proj_1"}]}],"has_more":false,"next_page":null}
                    """);
            }

            if (path.EndsWith("/usage/completions", StringComparison.Ordinal))
            {
                return StubHttpMessageHandler.Json("""
                    {"data":[{"results":[{"input_tokens":1000,"output_tokens":500,"num_model_requests":5,"project_id":"proj_1"}]}],"has_more":true,"next_page":"usage-next"}
                    """);
            }

            if (path.EndsWith("/costs", StringComparison.Ordinal))
            {
                return StubHttpMessageHandler.Json("""
                    {"data":[{"results":[{"amount":{"value":1.25,"currency":"usd"},"project_id":"proj_1"}]}],"has_more":false,"next_page":null}
                    """);
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });
        var vault = new MemorySecretVault();
        await vault.SaveAsync("openai-secret", "sk-admin-test-value");
        var connector = new OpenAiOrganizationConnector(
            new HttpClient(handler) { BaseAddress = new Uri("https://api.openai.com/") },
            vault,
            new FixedTimeProvider(Now));

        var result = await connector.FetchAsync(CreateConnection(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(ConnectionHealth.Fresh, result.UpstreamHealth);
        Assert.Equal(1300m, result.Snapshot!.Metrics.Single(metric => metric.Id == "openai-input-tokens").Current);
        Assert.Equal(700m, result.Snapshot.Metrics.Single(metric => metric.Id == "openai-output-tokens").Current);
        Assert.Equal(8m, result.Snapshot.Metrics.Single(metric => metric.Id == "openai-requests").Current);
        Assert.Equal(1.25m, result.Snapshot.Metrics.Single(metric => metric.Id == "openai-spend").Current);
        var projectTokens = result.Snapshot.Metrics.Single(metric =>
            metric.Scope == MetricScope.Project && metric.Kind == MetricKind.Tokens);
        Assert.Equal(2000m, projectTokens.Current);
        Assert.Equal("proj_1", projectTokens.Dimensions["project_id"]);
        var projectSpend = result.Snapshot.Metrics.Single(metric =>
            metric.Scope == MetricScope.Project && metric.Kind == MetricKind.Currency);
        Assert.Equal(1.25m, projectSpend.Current);
        Assert.Equal(3, handler.Requests.Count);
        Assert.All(handler.Requests, request =>
            Assert.Equal("Bearer", request.Headers.Authorization?.Scheme));
        Assert.All(handler.Requests, request =>
            Assert.Equal("sk-admin-test-value", request.Headers.Authorization?.Parameter));
        Assert.All(handler.Requests, request =>
            Assert.Contains("group_by=project_id", request.RequestUri!.Query, StringComparison.Ordinal));
    }

    [Fact]
    public async Task FetchAsync_ReturnsRateLimitedWithoutFabricatingMetrics()
    {
        var handler = new StubHttpMessageHandler(_ =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
            response.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromSeconds(90));
            return response;
        });
        var vault = new MemorySecretVault();
        await vault.SaveAsync("openai-secret", "sk-admin-test-value");
        var connector = new OpenAiOrganizationConnector(
            new HttpClient(handler) { BaseAddress = new Uri("https://api.openai.com/") },
            vault,
            new FixedTimeProvider(Now));

        var result = await connector.FetchAsync(CreateConnection(), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Null(result.Snapshot);
        Assert.Equal(ConnectionHealth.RateLimited, result.UpstreamHealth);
        Assert.Equal(TimeSpan.FromSeconds(90), result.RetryAfter);
    }

    [Fact]
    public async Task ConnectAndDisconnect_StoresThenRemovesOnlyTheVaultSecret()
    {
        var handler = new StubHttpMessageHandler(_ => StubHttpMessageHandler.Json("""
            {"data":[],"has_more":false,"next_page":null}
            """));
        var vault = new MemorySecretVault();
        var connector = new OpenAiOrganizationConnector(
            new HttpClient(handler) { BaseAddress = new Uri("https://api.openai.com/") },
            vault,
            new FixedTimeProvider(Now));

        var connection = await connector.ConnectAsync(
            new ConnectionRequest("Work", DataSourceKind.OfficialApi, "sk-admin-test-value"),
            CancellationToken.None);

        Assert.DoesNotContain("sk-admin", connection.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.NotNull(await vault.RetrieveAsync(connection.SecretReference!));

        await connector.DisconnectAsync(connection, CancellationToken.None);

        Assert.Null(await vault.RetrieveAsync(connection.SecretReference!));
    }

    [Fact]
    public async Task ConnectAsync_RejectsWrongSourceOrMissingAdminKey()
    {
        var connector = new OpenAiOrganizationConnector(
            new HttpClient(new StubHttpMessageHandler(_ => StubHttpMessageHandler.Json("{}")))
            {
                BaseAddress = new Uri("https://api.openai.com/")
            },
            new MemorySecretVault(),
            new FixedTimeProvider(Now));

        await Assert.ThrowsAsync<ArgumentException>(() => connector.ConnectAsync(
            new ConnectionRequest("Work", DataSourceKind.LocalCli, "secret")));
        await Assert.ThrowsAsync<ArgumentException>(() => connector.ConnectAsync(
            new ConnectionRequest("Work", DataSourceKind.OfficialApi)));
    }

    [Fact]
    public async Task ValidateAsync_ReportsInvalidCredentialWithoutThrowing()
    {
        var vault = new MemorySecretVault();
        await vault.SaveAsync("openai-secret", "rejected-key");
        var connector = new OpenAiOrganizationConnector(
            new HttpClient(new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.Forbidden)))
            {
                BaseAddress = new Uri("https://api.openai.com/")
            },
            vault,
            new FixedTimeProvider(Now));

        var validation = await connector.ValidateAsync(CreateConnection());

        Assert.False(validation.IsValid);
        Assert.Contains("rejected", validation.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task FetchAsync_MapsMissingKeyServerFailureAndMalformedJson()
    {
        var missing = new OpenAiOrganizationConnector(
            new HttpClient(new StubHttpMessageHandler(_ => throw new InvalidOperationException())),
            new MemorySecretVault(),
            new FixedTimeProvider(Now));
        var missingResult = await missing.FetchAsync(CreateConnection());
        Assert.Equal(ConnectionHealth.AuthenticationRequired, missingResult.UpstreamHealth);

        foreach (var response in new[]
                 {
                     new HttpResponseMessage(HttpStatusCode.InternalServerError),
                     StubHttpMessageHandler.Json("not-json")
                 })
        {
            var vault = new MemorySecretVault();
            await vault.SaveAsync("openai-secret", "admin-key");
            var connector = new OpenAiOrganizationConnector(
                new HttpClient(new StubHttpMessageHandler(_ => response))
                {
                    BaseAddress = new Uri("https://api.openai.com/")
                },
                vault,
                new FixedTimeProvider(Now));

            var result = await connector.FetchAsync(CreateConnection());
            Assert.Contains(result.UpstreamHealth,
                new[] { ConnectionHealth.Unavailable, ConnectionHealth.FormatChanged });
            Assert.Null(result.Snapshot);
        }
    }

    private static ConnectorConnection CreateConnection() => new(
        "openai-org",
        ProviderKind.OpenAI,
        "Work",
        DataSourceKind.OfficialApi,
        "openai-secret",
        null);
}

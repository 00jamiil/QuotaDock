using System.Net;
using QuotaDock.Connectors.OpenAI;
using QuotaDock.Core.Domain;

namespace QuotaDock.Connectors.Tests;

public sealed class OpenAiCompatibleConnectorTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 7, 24, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task ValidateAsync_AcceptsConfiguredModelAndDoesNotFabricateUsage()
    {
        var handler = new StubHttpMessageHandler(request =>
        {
            Assert.Equal("/v1/models", request.RequestUri!.AbsolutePath);
            return StubHttpMessageHandler.Json("""
                {"object":"list","data":[{"id":"qwen3-coder","object":"model"}]}
                """);
        });
        var vault = new MemorySecretVault();
        var connector = CreateConnector(handler, vault);
        var connection = await connector.ConnectAsync(Request(
            "https://gateway.example/v1/", "qwen3-coder", secret: "provider-key"));

        var validation = await connector.ValidateAsync(connection);
        var result = await connector.FetchAsync(connection);

        Assert.True(validation.IsValid);
        Assert.True(result.IsSuccess);
        Assert.Empty(result.Snapshot!.Metrics);
        Assert.Contains("usage endpoint", result.Snapshot.StatusMessage, StringComparison.OrdinalIgnoreCase);
        Assert.All(handler.Requests, request =>
        {
            Assert.Equal("Bearer", request.Headers.Authorization?.Scheme);
            Assert.Equal("provider-key", request.Headers.Authorization?.Parameter);
        });
    }

    [Fact]
    public async Task FetchAsync_AggregatesPaginatedOpenAiUsageBuckets()
    {
        var handler = new StubHttpMessageHandler(request =>
        {
            if (request.RequestUri!.AbsolutePath == "/v1/models")
            {
                return StubHttpMessageHandler.Json("""{"data":[{"id":"model-a"}]}""");
            }

            if (request.RequestUri.Query.Contains("page=next-page", StringComparison.Ordinal))
            {
                return StubHttpMessageHandler.Json("""
                    {"data":[{"results":[{"input_tokens":40,"output_tokens":10,"num_model_requests":2}]}],"has_more":false}
                    """);
            }

            return StubHttpMessageHandler.Json("""
                {"data":[{"results":[{"input_tokens":100,"output_tokens":25,"num_model_requests":3}]}],"has_more":true,"next_page":"next-page"}
                """);
        });
        var connector = CreateConnector(handler, new MemorySecretVault());
        var connection = await connector.ConnectAsync(Request(
            "https://gateway.example/v1", "model-a", "https://gateway.example/admin/usage", "secret"));

        var result = await connector.FetchAsync(connection);

        Assert.True(result.IsSuccess);
        Assert.Equal(140m, result.Snapshot!.Metrics.Single(metric => metric.Id == "compatible-input-tokens").Current);
        Assert.Equal(35m, result.Snapshot.Metrics.Single(metric => metric.Id == "compatible-output-tokens").Current);
        Assert.Equal(5m, result.Snapshot.Metrics.Single(metric => metric.Id == "compatible-requests").Current);
        Assert.Equal(3, handler.Requests.Count);
        Assert.Equal("next-page", System.Web.HttpUtility.ParseQueryString(handler.Requests[2].RequestUri!.Query)["page"]);
    }

    [Theory]
    [InlineData("http://remote.example/v1")]
    [InlineData("ftp://localhost/v1")]
    [InlineData("https://user:password@example.com/v1")]
    [InlineData("https://example.com/v1#fragment")]
    public async Task ConnectAsync_RejectsUnsafeBaseUrls(string baseUrl)
    {
        var connector = CreateConnector(
            new StubHttpMessageHandler(_ => StubHttpMessageHandler.Json("{}")),
            new MemorySecretVault());

        await Assert.ThrowsAsync<ArgumentException>(() =>
            connector.ConnectAsync(Request(baseUrl, "model-a")));
    }

    [Theory]
    [InlineData("http://localhost:11434/v1")]
    [InlineData("http://127.0.0.1:1234/v1/")]
    [InlineData("http://[::1]:8080/v1")]
    public async Task ConnectAsync_AllowsLoopbackHttpWithoutAnApiKey(string baseUrl)
    {
        var connector = CreateConnector(
            new StubHttpMessageHandler(_ => StubHttpMessageHandler.Json("""{"data":[{"id":"local-model"}]}""")),
            new MemorySecretVault());

        var connection = await connector.ConnectAsync(Request(baseUrl, "local-model"));
        var validation = await connector.ValidateAsync(connection);

        Assert.True(validation.IsValid);
        Assert.Null(connection.SecretReference);
    }

    [Fact]
    public async Task ConnectAsync_RejectsCrossOriginUsageEndpoint()
    {
        var connector = CreateConnector(
            new StubHttpMessageHandler(_ => StubHttpMessageHandler.Json("{}")),
            new MemorySecretVault());

        await Assert.ThrowsAsync<ArgumentException>(() => connector.ConnectAsync(Request(
            "https://models.example/v1", "model-a", "https://billing.example/usage", "secret")));
    }

    [Theory]
    [InlineData("api_key")]
    [InlineData("access_token")]
    [InlineData("password")]
    [InlineData("client_secret")]
    [InlineData("authorization")]
    public async Task ConnectAsync_RejectsSecretsEmbeddedInUsageQuery(string parameter)
    {
        var connector = CreateConnector(
            new StubHttpMessageHandler(_ => StubHttpMessageHandler.Json("{}")),
            new MemorySecretVault());

        await Assert.ThrowsAsync<ArgumentException>(() => connector.ConnectAsync(Request(
            "https://gateway.example/v1",
            "model-a",
            $"https://gateway.example/usage?{parameter}=do-not-store")));
    }

    [Fact]
    public async Task ValidateAsync_RejectsMissingModelWithoutSavingUsage()
    {
        var connector = CreateConnector(
            new StubHttpMessageHandler(_ => StubHttpMessageHandler.Json("""{"data":[{"id":"other-model"}]}""")),
            new MemorySecretVault());
        var connection = await connector.ConnectAsync(Request(
            "https://gateway.example/v1", "wanted-model", secret: "secret"));

        var validation = await connector.ValidateAsync(connection);

        Assert.False(validation.IsValid);
        Assert.Contains("wanted-model", validation.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task FetchAsync_DoesNotFollowRedirectsOrFabricateMetrics()
    {
        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.Redirect)
        {
            Headers = { Location = new Uri("https://attacker.example/v1/models") }
        });
        var connector = CreateConnector(handler, new MemorySecretVault());
        var connection = await connector.ConnectAsync(Request(
            "https://gateway.example/v1", "model-a", secret: "secret"));

        var result = await connector.FetchAsync(connection);

        Assert.False(result.IsSuccess);
        Assert.Null(result.Snapshot);
        Assert.Single(handler.Requests);
        Assert.Equal("gateway.example", handler.Requests[0].RequestUri!.Host);
    }

    [Fact]
    public async Task DisconnectAsync_RemovesStoredSecret()
    {
        var vault = new MemorySecretVault();
        var connector = CreateConnector(
            new StubHttpMessageHandler(_ => StubHttpMessageHandler.Json("{}")), vault);
        var connection = await connector.ConnectAsync(Request(
            "https://gateway.example/v1", "model-a", secret: "do-not-persist-raw-key"));

        Assert.Equal("do-not-persist-raw-key", await vault.RetrieveAsync(connection.SecretReference!));
        Assert.DoesNotContain("do-not-persist-raw-key", connection.ToString(), StringComparison.Ordinal);
        await connector.DisconnectAsync(connection);

        Assert.Null(await vault.RetrieveAsync(connection.SecretReference!));
    }

    [Fact]
    public void CreateSecureHandler_DisablesAutomaticRedirects()
    {
        using var handler = OpenAiCompatibleConnector.CreateSecureHandler();

        Assert.False(handler.AllowAutoRedirect);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, ConnectionHealth.AuthenticationRequired)]
    [InlineData(HttpStatusCode.Forbidden, ConnectionHealth.AuthenticationRequired)]
    [InlineData(HttpStatusCode.TooManyRequests, ConnectionHealth.RateLimited)]
    [InlineData(HttpStatusCode.InternalServerError, ConnectionHealth.Unavailable)]
    public async Task FetchAsync_MapsProviderHttpFailures(
        HttpStatusCode status,
        ConnectionHealth expectedHealth)
    {
        var handler = new StubHttpMessageHandler(_ =>
        {
            var response = new HttpResponseMessage(status);
            if (status == HttpStatusCode.TooManyRequests)
            {
                response.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(
                    TimeSpan.FromSeconds(45));
            }

            return response;
        });
        var connector = CreateConnector(handler, new MemorySecretVault());
        var connection = await connector.ConnectAsync(Request(
            "https://gateway.example/v1", "model-a", secret: "secret"));

        var result = await connector.FetchAsync(connection);

        Assert.Equal(expectedHealth, result.UpstreamHealth);
        Assert.Null(result.Snapshot);
        if (status == HttpStatusCode.TooManyRequests)
        {
            Assert.Equal(TimeSpan.FromSeconds(45), result.RetryAfter);
        }
    }

    [Fact]
    public async Task FetchAsync_MapsNetworkFailureAndCallerCancellation()
    {
        var networkConnector = CreateConnector(
            new StubHttpMessageHandler(_ => throw new HttpRequestException("offline")),
            new MemorySecretVault());
        var networkConnection = await networkConnector.ConnectAsync(Request(
            "https://gateway.example/v1", "model-a"));

        var networkResult = await networkConnector.FetchAsync(networkConnection);

        Assert.Equal(ConnectionHealth.Unavailable, networkResult.UpstreamHealth);
        Assert.Null(networkResult.Snapshot);

        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            networkConnector.FetchAsync(networkConnection, cancellation.Token));
    }

    [Fact]
    public async Task FetchAsync_MapsMissingVaultSecretToAuthenticationRequired()
    {
        var vault = new MemorySecretVault();
        var connector = CreateConnector(
            new StubHttpMessageHandler(_ => throw new InvalidOperationException("HTTP must not run")),
            vault);
        var connection = await connector.ConnectAsync(Request(
            "https://gateway.example/v1", "model-a", secret: "secret"));
        await vault.RemoveAsync(connection.SecretReference!);

        var result = await connector.FetchAsync(connection);

        Assert.Equal(ConnectionHealth.AuthenticationRequired, result.UpstreamHealth);
        Assert.Null(result.Snapshot);
    }

    [Fact]
    public async Task FetchAsync_FailsClosedForMalformedModelsResponse()
    {
        var connector = CreateConnector(
            new StubHttpMessageHandler(_ => StubHttpMessageHandler.Json("""{"data":{}}""")),
            new MemorySecretVault());
        var connection = await connector.ConnectAsync(Request(
            "https://gateway.example/api", "model-a"));

        var result = await connector.FetchAsync(connection);

        Assert.Equal(ConnectionHealth.FormatChanged, result.UpstreamHealth);
        Assert.Null(result.Snapshot);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"data\":[{}]}")]
    [InlineData("{\"data\":[{\"results\":[{}]}]}")]
    [InlineData("{\"data\":[],\"has_more\":true}")]
    public async Task FetchAsync_FailsClosedForMalformedUsageResponses(string usagePayload)
    {
        var handler = new StubHttpMessageHandler(request =>
            request.RequestUri!.AbsolutePath.EndsWith("/models", StringComparison.Ordinal)
                ? StubHttpMessageHandler.Json("""{"data":[{"id":"model-a"}]}""")
                : StubHttpMessageHandler.Json(usagePayload));
        var connector = CreateConnector(handler, new MemorySecretVault());
        var connection = await connector.ConnectAsync(Request(
            "https://gateway.example/v1", "model-a", "https://gateway.example/usage"));

        var result = await connector.FetchAsync(connection);

        Assert.Equal(ConnectionHealth.FormatChanged, result.UpstreamHealth);
        Assert.Null(result.Snapshot);
    }

    [Fact]
    public async Task ConnectAsync_RejectsWrongSourceAndIncompleteSettings()
    {
        var connector = CreateConnector(
            new StubHttpMessageHandler(_ => StubHttpMessageHandler.Json("{}")),
            new MemorySecretVault());
        var validSettings = new Dictionary<string, string>
        {
            [OpenAiCompatibleConnector.BaseUrlSetting] = "https://gateway.example/v1",
            [OpenAiCompatibleConnector.ModelSetting] = "model-a"
        };

        await Assert.ThrowsAsync<ArgumentException>(() => connector.ConnectAsync(
            new ConnectionRequest("Wrong", DataSourceKind.LocalCli, settings: validSettings)));
        await Assert.ThrowsAsync<ArgumentException>(() => connector.ConnectAsync(
            new ConnectionRequest("Missing", DataSourceKind.OfficialApi)));
        await Assert.ThrowsAsync<ArgumentException>(() => connector.ConnectAsync(
            new ConnectionRequest("Missing", DataSourceKind.OfficialApi, settings:
                new Dictionary<string, string>
                {
                    [OpenAiCompatibleConnector.BaseUrlSetting] = "https://gateway.example/v1"
                })));
        await Assert.ThrowsAsync<ArgumentException>(() => connector.ConnectAsync(Request(
            "https://gateway.example/v1?tenant=a", "model-a")));
    }

    private static OpenAiCompatibleConnector CreateConnector(
        HttpMessageHandler handler,
        MemorySecretVault vault) => new(
        new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(5) },
        vault,
        new FixedTimeProvider(Now));

    private static ConnectionRequest Request(
        string baseUrl,
        string model,
        string? usageUrl = null,
        string? secret = null) => new(
        "Compatible account",
        DataSourceKind.OfficialApi,
        secret,
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [OpenAiCompatibleConnector.BaseUrlSetting] = baseUrl,
            [OpenAiCompatibleConnector.ModelSetting] = model,
            [OpenAiCompatibleConnector.UsageUrlSetting] = usageUrl ?? string.Empty
        });
}

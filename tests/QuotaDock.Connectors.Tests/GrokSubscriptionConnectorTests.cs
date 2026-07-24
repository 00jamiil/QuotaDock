using QuotaDock.Connectors.Xai;
using QuotaDock.Core.Domain;

namespace QuotaDock.Connectors.Tests;

public sealed class GrokSubscriptionConnectorTests
{
    private const string UsagePayload = """
        {
          "credits": { "remaining": 120, "total": 200, "resetsAt": "2026-08-01T00:00:00Z" },
          "session": { "usedPercent": 30, "resetsAt": "2026-07-24T17:00:00Z" },
          "weekly": { "usedPercent": 55, "resetsAt": "2026-07-30T00:00:00Z" }
        }
        """;

    [Fact]
    public void Parse_ReadsCreditsAndWindows()
    {
        var now = new DateTimeOffset(2026, 7, 24, 12, 0, 0, TimeSpan.Zero);
        var result = GrokUsageParser.Parse("grok-subscription", "Grok", UsagePayload, now);

        Assert.True(result.IsSuccess);
        Assert.Equal(ProviderKind.Xai, result.Snapshot!.Provider);
        Assert.Equal(120m, result.Snapshot.Metrics.Single(m => m.Id == "grok-credits").Current);
        Assert.Equal(200m, result.Snapshot.Metrics.Single(m => m.Id == "grok-credits").Limit);
        Assert.Equal(70m, result.Snapshot.Metrics.Single(m => m.Id == "grok-session").Current);
        Assert.Equal(45m, result.Snapshot.Metrics.Single(m => m.Id == "grok-weekly").Current);
    }

    [Fact]
    public void Parse_FailsClosedWhenNoRecognizedShape()
    {
        var result = GrokUsageParser.Parse("g", "Grok", "{\"unexpected\":true}", DateTimeOffset.UtcNow);

        Assert.Equal(ConnectionHealth.FormatChanged, result.UpstreamHealth);
        Assert.Null(result.Snapshot);
    }

    [Fact]
    public void Parse_IgnoresOutOfRangeWindowPercent()
    {
        var result = GrokUsageParser.Parse(
            "g", "Grok", "{\"session\":{\"usedPercent\":150}}", DateTimeOffset.UtcNow);

        Assert.Equal(ConnectionHealth.FormatChanged, result.UpstreamHealth);
    }

    [Fact]
    public void Credentials_ParseReadsTokenAndPlan()
    {
        var json = """
            { "accessToken": "xai-EXAMPLE", "planType": "super" }
            """;
        var credentials = GrokLocalCredentialsReader.Parse(json);

        Assert.NotNull(credentials);
        Assert.Equal("xai-EXAMPLE", credentials!.AccessToken);
        Assert.Equal("super", credentials.PlanType);
    }

    [Fact]
    public void Credentials_ParseReturnsNullWhenTokenMissing()
    {
        Assert.Null(GrokLocalCredentialsReader.Parse("{}"));
    }

    [Fact]
    public void Credentials_ToStringRedactsAccessToken()
    {
        var credentials = new GrokCredentials("xai-SECRET", null, [], "super");

        Assert.DoesNotContain("SECRET", credentials.ToString());
        Assert.Contains("[REDACTED]", credentials.ToString());
    }

    [Fact]
    public async Task Fetch_ReturnsAuthRequiredWhenNoLocalCredential()
    {
        var connector = new GrokSubscriptionConnector(
            new StubCredentialsReader(null),
            new StubUsageClient(UsagePayload),
            TimeProvider.System);
        var connection = await connector.ConnectAsync(new ConnectionRequest("Grok", DataSourceKind.LocalCli));

        var result = await connector.FetchAsync(connection);

        Assert.Equal(ConnectionHealth.AuthenticationRequired, result.UpstreamHealth);
    }

    [Fact]
    public async Task Fetch_ReturnsFreshSnapshotFromLivePayload()
    {
        var valid = new GrokCredentials("t", DateTimeOffset.UtcNow.AddHours(1), [], "super");
        var connector = new GrokSubscriptionConnector(
            new StubCredentialsReader(valid),
            new StubUsageClient(UsagePayload),
            TimeProvider.System);
        var connection = await connector.ConnectAsync(new ConnectionRequest("Grok", DataSourceKind.LocalCli));

        var result = await connector.FetchAsync(connection);

        Assert.True(result.IsSuccess);
        Assert.Contains(result.Snapshot!.Metrics, m => m.Id == "grok-credits");
    }

    private sealed class StubCredentialsReader(GrokCredentials? credentials) : IGrokCredentialsReader
    {
        public GrokCredentials? Read() => credentials;
    }

    private sealed class StubUsageClient(string payload) : IGrokUsageClient
    {
        public Task<string> ReadUsageAsync(GrokCredentials credentials, CancellationToken cancellationToken = default) =>
            Task.FromResult(payload);
    }
}

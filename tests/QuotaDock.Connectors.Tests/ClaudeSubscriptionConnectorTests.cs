using QuotaDock.Connectors.Anthropic;
using QuotaDock.Core.Domain;

namespace QuotaDock.Connectors.Tests;

public sealed class ClaudeSubscriptionConnectorTests
{
    private const string UsagePayload = """
        {
          "five_hour": { "utilization": 40, "resets_at": "2026-07-24T15:00:00Z" },
          "seven_day": { "utilization": 12.5, "resets_at": "2026-07-30T00:00:00Z" },
          "seven_day_opus": { "utilization": 80, "resets_at": "2026-07-30T00:00:00Z" }
        }
        """;

    [Fact]
    public void Parse_ConvertsUtilizationToRemainingQuota()
    {
        var now = new DateTimeOffset(2026, 7, 24, 12, 0, 0, TimeSpan.Zero);
        var result = ClaudeUsageParser.Parse("claude-subscription", "Claude · Max", UsagePayload, now);

        Assert.True(result.IsSuccess);
        Assert.Equal(DataSourceKind.LocalCli, result.Snapshot!.Source);
        Assert.Equal(60m, result.Snapshot.Metrics.Single(m => m.Id == "claude-session").Current);
        Assert.Equal(87.5m, result.Snapshot.Metrics.Single(m => m.Id == "claude-weekly").Current);
        Assert.Equal(20m, result.Snapshot.Metrics.Single(m => m.Id == "claude-weekly-opus").Current);
        Assert.Equal(MetricDirection.Remaining, result.Snapshot.Metrics[0].Direction);
    }

    [Fact]
    public void Parse_TreatsUtilizationStrictlyAsPercent()
    {
        // Utilization is a documented percent in [0,100]. A value of 0.25 means
        // 0.25% used -> 99.75% remaining. We deliberately do not guess a 0-1
        // fraction scale, which would fabricate quota (review finding #1).
        var now = new DateTimeOffset(2026, 7, 24, 12, 0, 0, TimeSpan.Zero);
        var result = ClaudeUsageParser.Parse("c", "Claude", "{\"five_hour\":{\"utilization\":0.25}}", now);

        Assert.True(result.IsSuccess);
        Assert.Equal(99.75m, result.Snapshot!.Metrics.Single(m => m.Id == "claude-session").Current);
    }

    [Fact]
    public void Parse_FailsClosedWhenNoWindowsPresent()
    {
        var result = ClaudeUsageParser.Parse("c", "Claude", "{\"unexpected\":true}", DateTimeOffset.UtcNow);

        Assert.Equal(ConnectionHealth.FormatChanged, result.UpstreamHealth);
        Assert.Null(result.Snapshot);
    }

    [Fact]
    public void Credentials_ParseReadsTokenExpiryAndScopes()
    {
        var json = """
            {
              "claudeAiOauth": {
                "accessToken": "sk-ant-oat-EXAMPLE",
                "expiresAt": 1784809911801,
                "scopes": ["user:inference", "user:profile"],
                "subscriptionType": "max"
              }
            }
            """;
        var credentials = ClaudeLocalCredentialsReader.Parse(json);

        Assert.NotNull(credentials);
        Assert.Equal("sk-ant-oat-EXAMPLE", credentials!.AccessToken);
        Assert.True(credentials.HasProfileScope);
        Assert.Equal("max", credentials.SubscriptionType);
        Assert.True(credentials.IsExpired(DateTimeOffset.FromUnixTimeMilliseconds(1784809911802)));
        Assert.False(credentials.IsExpired(DateTimeOffset.FromUnixTimeMilliseconds(1784809911800)));
    }

    [Fact]
    public void Credentials_ParseReturnsNullWhenTokenMissing()
    {
        Assert.Null(ClaudeLocalCredentialsReader.Parse("{\"claudeAiOauth\":{}}"));
        Assert.Null(ClaudeLocalCredentialsReader.Parse("{}"));
    }

    [Fact]
    public void Credentials_ToStringRedactsAccessToken()
    {
        var credentials = new ClaudeCredentials(
            "sk-ant-oat-SECRET-VALUE",
            DateTimeOffset.UtcNow,
            ["user:profile"],
            "max");

        var text = credentials.ToString();

        Assert.DoesNotContain("SECRET-VALUE", text);
        Assert.Contains("[REDACTED]", text);
    }

    [Fact]
    public void Parse_TreatsUtilizationAsPercentNotFraction()
    {
        // A payload value of 1 means 1% used -> 99% remaining, never 100% used.
        var result = ClaudeUsageParser.Parse("c", "Claude", "{\"five_hour\":{\"utilization\":1}}", DateTimeOffset.UtcNow);

        Assert.True(result.IsSuccess);
        Assert.Equal(99m, result.Snapshot!.Metrics.Single(m => m.Id == "claude-session").Current);
    }

    [Fact]
    public void Parse_AcceptsEpochMillisecondResetWithoutThrowing()
    {
        var payload = "{\"five_hour\":{\"utilization\":40,\"resets_at\":1784809911801}}";
        var result = ClaudeUsageParser.Parse("c", "Claude", payload, DateTimeOffset.UtcNow);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Snapshot!.Metrics.Single(m => m.Id == "claude-session").ResetsAt);
    }

    [Fact]
    public async Task Fetch_DegradesToFailureOnMalformedPayloadInsteadOfThrowing()
    {
        var valid = new ClaudeCredentials("t", DateTimeOffset.UtcNow.AddHours(1), ["user:profile"], "max");
        var connector = new ClaudeSubscriptionConnector(
            new StubCredentialsReader(valid),
            new StubUsageClient("not-json-at-all"),
            TimeProvider.System,
            () => null);
        var connection = await connector.ConnectAsync(new ConnectionRequest("Claude", DataSourceKind.LocalCli));

        var result = await connector.FetchAsync(connection);

        Assert.False(result.IsSuccess);
        Assert.Null(result.Snapshot);
    }

    [Fact]
    public async Task Fetch_ReturnsAuthRequiredWhenNoLocalCredential()
    {
        var connector = new ClaudeSubscriptionConnector(
            new StubCredentialsReader(null),
            new StubUsageClient(UsagePayload),
            TimeProvider.System);
        var connection = await connector.ConnectAsync(new ConnectionRequest("Claude", DataSourceKind.LocalCli));

        var result = await connector.FetchAsync(connection);

        Assert.Equal(ConnectionHealth.AuthenticationRequired, result.UpstreamHealth);
    }

    [Fact]
    public async Task Fetch_ReturnsAuthRequiredWhenCredentialExpired()
    {
        var expired = new ClaudeCredentials("t", DateTimeOffset.UtcNow.AddHours(-1), ["user:profile"], "max");
        var connector = new ClaudeSubscriptionConnector(
            new StubCredentialsReader(expired),
            new StubUsageClient(UsagePayload),
            TimeProvider.System);
        var connection = await connector.ConnectAsync(new ConnectionRequest("Claude", DataSourceKind.LocalCli));

        var result = await connector.FetchAsync(connection);

        Assert.Equal(ConnectionHealth.AuthenticationRequired, result.UpstreamHealth);
    }

    [Fact]
    public async Task Fetch_ReturnsFreshSnapshotFromLiveWindows()
    {
        var valid = new ClaudeCredentials("t", DateTimeOffset.UtcNow.AddHours(1), ["user:profile"], "max");
        var connector = new ClaudeSubscriptionConnector(
            new StubCredentialsReader(valid),
            new StubUsageClient(UsagePayload),
            TimeProvider.System,
            () => null);
        var connection = await connector.ConnectAsync(new ConnectionRequest("Claude", DataSourceKind.LocalCli));

        var result = await connector.FetchAsync(connection);

        Assert.True(result.IsSuccess);
        Assert.Contains(result.Snapshot!.Metrics, m => m.Id == "claude-session");
        Assert.Contains("Max", result.Snapshot.AccountLabel);
    }

    private sealed class StubCredentialsReader(ClaudeCredentials? credentials) : IClaudeCredentialsReader
    {
        public ClaudeCredentials? Read() => credentials;
    }

    private sealed class StubUsageClient(string payload) : IClaudeUsageClient
    {
        public Task<string> ReadUsageAsync(ClaudeCredentials credentials, CancellationToken cancellationToken = default) =>
            Task.FromResult(payload);
    }
}

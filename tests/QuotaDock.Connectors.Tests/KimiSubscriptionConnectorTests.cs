using QuotaDock.Connectors.Moonshot;
using QuotaDock.Core.Domain;

namespace QuotaDock.Connectors.Tests;

public sealed class KimiSubscriptionConnectorTests
{
    private const string UsagePayload = """
        {
          "five_hour": { "utilization": 25, "resets_at": "2026-07-24T17:00:00Z" },
          "seven_day": { "utilization": 40, "resets_at": "2026-07-30T00:00:00Z" }
        }
        """;

    [Fact]
    public void Parse_ConvertsUtilizationToRemainingQuota()
    {
        var now = new DateTimeOffset(2026, 7, 24, 12, 0, 0, TimeSpan.Zero);
        var result = KimiUsageParser.Parse("kimi-subscription", "Kimi", UsagePayload, now);

        Assert.True(result.IsSuccess);
        Assert.Equal(ProviderKind.Moonshot, result.Snapshot!.Provider);
        Assert.Equal(75m, result.Snapshot.Metrics.Single(m => m.Id == "kimi-session").Current);
        Assert.Equal(60m, result.Snapshot.Metrics.Single(m => m.Id == "kimi-weekly").Current);
        Assert.Equal(MetricDirection.Remaining, result.Snapshot.Metrics[0].Direction);
    }

    [Fact]
    public void Parse_FailsClosedWhenNoWindowsPresent()
    {
        var result = KimiUsageParser.Parse("k", "Kimi", "{\"unexpected\":true}", DateTimeOffset.UtcNow);

        Assert.Equal(ConnectionHealth.FormatChanged, result.UpstreamHealth);
        Assert.Null(result.Snapshot);
    }

    [Fact]
    public void Credentials_ParseReadsNestedKimiBlock()
    {
        var json = """
            {
              "kimiCodeOauth": {
                "accessToken": "kimi-EXAMPLE",
                "subscriptionType": "pro"
              }
            }
            """;
        var credentials = KimiLocalCredentialsReader.Parse(json);

        Assert.NotNull(credentials);
        Assert.Equal("kimi-EXAMPLE", credentials!.AccessToken);
        Assert.Equal("pro", credentials.PlanType);
    }

    [Fact]
    public void Credentials_ParseReturnsNullWhenTokenMissing()
    {
        Assert.Null(KimiLocalCredentialsReader.Parse("{}"));
        Assert.Null(KimiLocalCredentialsReader.Parse("{\"kimiCodeOauth\":{}}"));
    }

    [Fact]
    public void Credentials_ToStringRedactsAccessToken()
    {
        var credentials = new KimiCredentials("kimi-SECRET", null, [], "pro");

        Assert.DoesNotContain("SECRET", credentials.ToString());
        Assert.Contains("[REDACTED]", credentials.ToString());
    }

    [Fact]
    public async Task Fetch_ReturnsAuthRequiredWhenNoLocalCredential()
    {
        var connector = new KimiSubscriptionConnector(
            new StubCredentialsReader(null),
            new StubUsageClient(UsagePayload),
            TimeProvider.System);
        var connection = await connector.ConnectAsync(new ConnectionRequest("Kimi", DataSourceKind.LocalCli));

        var result = await connector.FetchAsync(connection);

        Assert.Equal(ConnectionHealth.AuthenticationRequired, result.UpstreamHealth);
    }

    [Fact]
    public async Task Fetch_ReturnsFreshSnapshotFromLiveWindows()
    {
        var valid = new KimiCredentials("t", DateTimeOffset.UtcNow.AddHours(1), [], "pro");
        var connector = new KimiSubscriptionConnector(
            new StubCredentialsReader(valid),
            new StubUsageClient(UsagePayload),
            TimeProvider.System);
        var connection = await connector.ConnectAsync(new ConnectionRequest("Kimi", DataSourceKind.LocalCli));

        var result = await connector.FetchAsync(connection);

        Assert.True(result.IsSuccess);
        Assert.Contains(result.Snapshot!.Metrics, m => m.Id == "kimi-session");
    }

    private sealed class StubCredentialsReader(KimiCredentials? credentials) : IKimiCredentialsReader
    {
        public KimiCredentials? Read() => credentials;
    }

    private sealed class StubUsageClient(string payload) : IKimiUsageClient
    {
        public Task<string> ReadUsageAsync(KimiCredentials credentials, CancellationToken cancellationToken = default) =>
            Task.FromResult(payload);
    }
}

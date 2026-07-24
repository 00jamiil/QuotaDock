using QuotaDock.Connectors.Alibaba;
using QuotaDock.Core.Domain;

namespace QuotaDock.Connectors.Tests;

public sealed class AlibabaDashboardPayloadParserTests
{
    [Fact]
    public void Parse_ProducesCreditsAndPlanMetadataForKnownSignature()
    {
        const string payload = """
            {
              "signature":"token-plan-team-v1",
              "account":"jameel@example.com",
              "plan":"Pro",
              "quota":100000,
              "used":27600,
              "remaining":72400,
              "resetsAt":"2026-08-01T00:00:00+08:00",
              "models":[{"name":"qwen3.7-max","credits":21000},{"name":"deepseek-v4","credits":6600}]
            }
            """;

        var result = AlibabaDashboardPayloadParser.Parse(
            "alibaba-team",
            payload,
            new DateTimeOffset(2026, 7, 23, 12, 0, 0, TimeSpan.Zero));

        Assert.True(result.IsSuccess);
        Assert.Equal("jameel@example.com · Pro", result.Snapshot!.AccountLabel);
        var remaining = result.Snapshot.Metrics.Single(m => m.Id == "alibaba-credits-remaining");
        Assert.Equal(MetricDirection.Remaining, remaining.Direction);
        Assert.Equal(72400m, remaining.Current);
        Assert.Equal(100000m, remaining.Limit);
        Assert.Equal(2, result.Snapshot.Metrics.Count(m => m.Scope == MetricScope.Model));
    }

    [Fact]
    public void Parse_FailsClosedWhenTheDashboardSignatureChanges()
    {
        var result = AlibabaDashboardPayloadParser.Parse(
            "alibaba-team",
            "{\"signature\":\"token-plan-team-v2\",\"quota\":100000}",
            DateTimeOffset.UtcNow);

        Assert.False(result.IsSuccess);
        Assert.Null(result.Snapshot);
        Assert.Equal(ConnectionHealth.FormatChanged, result.UpstreamHealth);
    }
}


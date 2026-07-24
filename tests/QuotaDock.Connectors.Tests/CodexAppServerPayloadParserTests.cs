using QuotaDock.Connectors.OpenAI;
using QuotaDock.Core.Domain;

namespace QuotaDock.Connectors.Tests;

public sealed class CodexAppServerPayloadParserTests
{
    private const string RateLimits = """
        {
          "id":2,
          "result":{
            "rateLimits":{
              "planType":"plus",
              "primary":{"usedPercent":32,"windowDurationMins":300,"resetsAt":1784822400},
              "secondary":{"usedPercent":59,"windowDurationMins":10080,"resetsAt":1785168000},
              "credits":{"hasCredits":true,"unlimited":false,"balance":"112.4"}
            }
          }
        }
        """;

    private const string Usage = """
        {
          "id":3,
          "result":{
            "summary":{"lifetimeTokens":2500000,"peakDailyTokens":180000},
            "dailyUsageBuckets":[
              {"startDate":"2026-07-22","tokens":120000},
              {"startDate":"2026-07-23","tokens":145000}
            ]
          }
        }
        """;

    [Fact]
    public void Parse_MapsRateLimitWindowsCreditsAndCurrentMonthTokens()
    {
        var result = CodexAppServerPayloadParser.Parse(
            "codex-personal",
            "Personal",
            RateLimits,
            Usage,
            new DateTimeOffset(2026, 7, 23, 12, 0, 0, TimeSpan.Zero));

        Assert.True(result.IsSuccess);
        Assert.Equal("Personal · Plus", result.Snapshot!.AccountLabel);
        Assert.Equal(68m, result.Snapshot.Metrics.Single(m => m.Id == "codex-session").Current);
        Assert.Equal(41m, result.Snapshot.Metrics.Single(m => m.Id == "codex-weekly").Current);
        Assert.Equal(112.4m, result.Snapshot.Metrics.Single(m => m.Id == "codex-credits").Current);
        Assert.Equal(265000m, result.Snapshot.Metrics.Single(m => m.Id == "codex-month-tokens").Current);
    }

    [Fact]
    public void Parse_FailsClosedForUnexpectedProtocolPayload()
    {
        var result = CodexAppServerPayloadParser.Parse(
            "codex-personal",
            "Personal",
            "{\"id\":2,\"result\":{}}",
            Usage,
            DateTimeOffset.UtcNow);

        Assert.Equal(ConnectionHealth.FormatChanged, result.UpstreamHealth);
        Assert.Null(result.Snapshot);
    }
}


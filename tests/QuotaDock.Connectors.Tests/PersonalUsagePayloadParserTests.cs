using QuotaDock.Connectors.Personal;
using QuotaDock.Core.Domain;

namespace QuotaDock.Connectors.Tests;

public sealed class PersonalUsagePayloadParserTests
{
    [Fact]
    public void Parse_HandlesExplicitUsedAndRemainingMetrics()
    {
        const string payload = """
            {
              "schema":"quotadock.personal-usage.v1",
              "account":"Personal",
              "metrics":[
                {"id":"session","label":"Session","kind":"quotaPercentage","direction":"remaining","value":68,"limit":100,"unit":"%","scope":"session","resetsAt":"2026-07-23T14:00:00Z"},
                {"id":"weekly","label":"Weekly","kind":"quotaPercentage","direction":"used","value":41,"limit":100,"unit":"%","scope":"weekly","resetsAt":"2026-07-27T09:00:00Z"}
              ]
            }
            """;

        var result = PersonalUsagePayloadParser.Parse(
            ProviderKind.OpenAI,
            "codex-personal",
            DataSourceKind.LocalCli,
            payload,
            DateTimeOffset.UtcNow);

        Assert.True(result.IsSuccess);
        Assert.Equal(MetricDirection.Remaining, result.Snapshot!.Metrics[0].Direction);
        Assert.Equal(MetricDirection.Used, result.Snapshot.Metrics[1].Direction);
    }

    [Fact]
    public void Parse_DoesNotAcceptUnknownSchemas()
    {
        var result = PersonalUsagePayloadParser.Parse(
            ProviderKind.Anthropic,
            "claude-personal",
            DataSourceKind.DashboardReader,
            "{\"schema\":\"unknown\",\"metrics\":[]}",
            DateTimeOffset.UtcNow);

        Assert.Equal(ConnectionHealth.FormatChanged, result.UpstreamHealth);
        Assert.Null(result.Snapshot);
    }
}


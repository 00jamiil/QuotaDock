using QuotaDock.Connectors.Alibaba;
using QuotaDock.Connectors.Anthropic;
using QuotaDock.Core.Domain;

namespace QuotaDock.Connectors.Tests;

public sealed class DashboardTextParserTests
{
    [Fact]
    public void AlibabaParser_ExtractsExplicitCreditValuesFromVisibleConsoleText()
    {
        const string visibleText = """
            Token Plan (Team Edition)
            Plan: Pro
            Account: jameel@example.com
            Total quota
            100,000 Credits
            Used
            27,600 Credits
            Remaining
            72,400 Credits
            Reset time: 2026-08-01T00:00:00+08:00
            """;

        var result = AlibabaDashboardTextParser.Parse(
            "alibaba-team", visibleText, DateTimeOffset.UtcNow);

        Assert.True(result.IsSuccess);
        Assert.Equal(72400m, result.Snapshot!.Metrics.Single(m => m.Id == "alibaba-credits-remaining").Current);
        Assert.Equal("jameel@example.com · Pro", result.Snapshot.AccountLabel);
    }

    [Fact]
    public void AlibabaParser_ExtractsAvailableModelCreditBreakdown()
    {
        const string visibleText = """
            Token Plan (Team Edition)
            Plan: Pro
            Account: jameel@example.com
            Total quota: 100,000 Credits
            Used: 27,600 Credits
            Remaining: 72,400 Credits
            Available models
            qwen3-max: 21,000 Credits
            deepseek-v3.1: 6,600 Credits
            """;

        var result = AlibabaDashboardTextParser.Parse(
            "alibaba-team", visibleText, DateTimeOffset.UtcNow);

        Assert.True(result.IsSuccess);
        var models = result.Snapshot!.Metrics.Where(metric => metric.Scope == MetricScope.Model).ToArray();
        Assert.Equal(2, models.Length);
        Assert.Contains(models, metric => metric.Label == "qwen3-max" && metric.Current == 21000m);
        Assert.Contains(models, metric => metric.Label == "deepseek-v3.1" && metric.Current == 6600m);
    }

    [Fact]
    public void ClaudeParser_ExtractsSessionAndWeeklyRemainingQuota()
    {
        const string visibleText = """
            Usage
            Current session
            12% used
            Resets at 2026-07-23T17:00:00Z
            Weekly limits
            All models
            37% used
            Resets at 2026-07-27T09:00:00Z
            """;

        var result = ClaudeDashboardTextParser.Parse(
            "claude-personal", "Personal", visibleText, DateTimeOffset.UtcNow);

        Assert.True(result.IsSuccess);
        Assert.Equal(88m, result.Snapshot!.Metrics.Single(m => m.Id == "claude-session").Current);
        Assert.Equal(63m, result.Snapshot.Metrics.Single(m => m.Id == "claude-weekly").Current);
        Assert.All(result.Snapshot.Metrics, metric => Assert.Equal(MetricDirection.Remaining, metric.Direction));
        Assert.Equal(DataSourceKind.DashboardReader, result.Snapshot.Source);
        Assert.DoesNotContain("Current session", result.Snapshot.ToString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(ProviderKind.Alibaba, "Welcome to Model Studio")]
    [InlineData(ProviderKind.Anthropic, "Welcome to Claude")]
    public void Parsers_FailClosedWhenRequiredUsageValuesAreAbsent(ProviderKind provider, string text)
    {
        var result = provider == ProviderKind.Alibaba
            ? AlibabaDashboardTextParser.Parse("connection", text, DateTimeOffset.UtcNow)
            : ClaudeDashboardTextParser.Parse("connection", "Personal", text, DateTimeOffset.UtcNow);

        Assert.Equal(ConnectionHealth.FormatChanged, result.UpstreamHealth);
        Assert.Null(result.Snapshot);
    }
}

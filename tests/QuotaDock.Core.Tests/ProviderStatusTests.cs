using QuotaDock.Core.Domain;

namespace QuotaDock.Core.Tests;

public sealed class ProviderStatusTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 24, 9, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(ProviderStatusLevel.Degraded, true)]
    [InlineData(ProviderStatusLevel.Outage, true)]
    [InlineData(ProviderStatusLevel.Operational, false)]
    [InlineData(ProviderStatusLevel.Unknown, false)]
    public void IsIncident_OnlyForDegradedOrOutage(ProviderStatusLevel level, bool expected)
    {
        var report = new ProviderStatusReport(ProviderKind.OpenAI, level, null, Now);
        Assert.Equal(expected, report.IsIncident);
    }

    [Fact]
    public void Unknown_FactoryProducesUnknownLevel()
    {
        var report = ProviderStatusReport.Unknown(ProviderKind.Anthropic, Now);
        Assert.Equal(ProviderStatusLevel.Unknown, report.Level);
        Assert.False(report.IsIncident);
    }
}

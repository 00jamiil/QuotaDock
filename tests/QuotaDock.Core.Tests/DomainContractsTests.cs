using QuotaDock.Core.Configuration;
using QuotaDock.Core.Domain;

namespace QuotaDock.Core.Tests;

public sealed class DomainContractsTests
{
    [Fact]
    public void AppSettings_DefaultsAreLocalSafeAndUnconfigured()
    {
        var settings = AppSettings.Default;

        Assert.Equal(420, settings.Window.Width);
        Assert.Equal(640, settings.Window.Height);
        Assert.Equal(96, settings.Window.Dpi);
        Assert.True(settings.Window.IsAlwaysOnTop);
        Assert.False(settings.StartWithWindows);
        Assert.Empty(settings.PinnedMetricIds);
        Assert.Empty(settings.SoftBudgets);
        Assert.NotNull(settings.Appearance);
        Assert.False(settings.CompactCards);
        Assert.Empty(settings.Notifications);
    }

    [Fact]
    public void ConnectionRequest_TrimsInputAndRedactsSecretFromText()
    {
        var request = new ConnectionRequest(
            "  Work  ",
            DataSourceKind.OfficialApi,
            "  sk-admin-secret  ",
            new Dictionary<string, string> { ["softBudget"] = "25" });

        Assert.Equal("Work", request.AccountLabel);
        Assert.Equal("sk-admin-secret", request.Secret);
        Assert.Equal("25", request.Settings["softBudget"]);
        Assert.DoesNotContain("sk-admin-secret", request.ToString(), StringComparison.Ordinal);
        Assert.Contains("[REDACTED]", request.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void ConnectionRequest_RejectsBlankAccount()
    {
        Assert.Throws<ArgumentException>(() =>
            new ConnectionRequest(" ", DataSourceKind.LocalCli));
    }

    [Fact]
    public void SnapshotAndDefinitions_ExposeCapabilitiesAndMetricPresence()
    {
        var definition = new ConnectorDefinition(
            "provider", ProviderKind.OpenAI, "Provider", DataSourceKind.OfficialApi,
            ConnectorCapabilities.Tokens | ConnectorCapabilities.Costs, true);
        var empty = new UsageSnapshot(
            "connection", ProviderKind.OpenAI, "Work", DataSourceKind.OfficialApi,
            DateTimeOffset.UtcNow, ConnectionHealth.Fresh, [], null);

        Assert.True(definition.RequiresSecret);
        Assert.True(definition.Capabilities.HasFlag(ConnectorCapabilities.Tokens));
        Assert.False(empty.HasMetrics);
        Assert.True(ConnectionValidationResult.Valid().IsValid);
        Assert.False(ConnectionValidationResult.Invalid("no").IsValid);
    }

    [Fact]
    public void ConnectorFetchResult_RejectsFailureWithNonFailureHealth()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            ConnectorFetchResult.Failure(ConnectionHealth.Fresh, "invalid"));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            ConnectorFetchResult.Failure(ConnectionHealth.Stale, "invalid"));
    }

    [Fact]
    public void NotificationPreference_PreservesOptInThreshold()
    {
        var preference = new NotificationPreference(true, 85m);

        Assert.True(preference.Enabled);
        Assert.Equal(85m, preference.ThresholdPercentage);
    }
}

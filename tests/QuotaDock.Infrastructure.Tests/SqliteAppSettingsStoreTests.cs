using QuotaDock.Core.Configuration;
using QuotaDock.Core.Refresh;
using QuotaDock.Infrastructure.Persistence;

namespace QuotaDock.Infrastructure.Tests;

public sealed class SqliteAppSettingsStoreTests : IAsyncLifetime
{
    private readonly string databasePath = Path.Combine(
        Path.GetTempPath(), $"quotadock-settings-{Guid.NewGuid():N}.db");
    private SqliteAppSettingsStore store = null!;

    public async Task InitializeAsync()
    {
        store = new SqliteAppSettingsStore(databasePath);
        await store.InitializeAsync();
    }

    public async Task DisposeAsync()
    {
        await store.DisposeAsync();
        File.Delete(databasePath);
    }

    [Fact]
    public async Task LoadAsync_ReturnsSafeDefaultsForNewDatabase()
    {
        var settings = await store.LoadAsync();

        Assert.Equal(360, settings.Window.Width);
        Assert.True(settings.Window.IsAlwaysOnTop);
        Assert.False(settings.StartWithWindows);
        Assert.Empty(settings.PinnedMetricIds);
        Assert.Empty(settings.Notifications);
        Assert.Equal(RefreshMode.Adaptive, settings.Insights.RefreshMode);
        Assert.False(settings.Insights.AgentAwareRefresh);
        Assert.False(settings.Insights.CompactMode);
    }

    [Fact]
    public async Task SaveAndLoad_RoundTripsWindowPinsBudgetsAndNotifications()
    {
        var expected = new AppSettings(
            new WindowPlacement(120, 240, 360, 620, "monitor-b", 144, false),
            true,
            ["connection-a:metric-a", "connection-b:metric-b"],
            new Dictionary<string, decimal> { ["connection-a:spend"] = 25m },
            new Dictionary<string, NotificationPreference>
            {
                ["connection-a:spend"] = new(true, 80m)
            },
            new InsightPreferences(RefreshMode.Fixed15m, true, true, true));

        await store.SaveAsync(expected);
        var actual = await store.LoadAsync();

        Assert.Equal(expected.Window, actual.Window);
        Assert.Equal(expected.StartWithWindows, actual.StartWithWindows);
        Assert.Equal(expected.PinnedMetricIds, actual.PinnedMetricIds);
        Assert.Equal(25m, actual.SoftBudgets["connection-a:spend"]);
        Assert.True(actual.Notifications["connection-a:spend"].Enabled);
        Assert.Equal(RefreshMode.Fixed15m, actual.Insights.RefreshMode);
        Assert.True(actual.Insights.AgentAwareRefresh);
        Assert.True(actual.Insights.CompactMode);
        Assert.True(actual.Insights.ResetCelebration);
    }
}

namespace QuotaDock.Core.Configuration;

using QuotaDock.Core.Refresh;

public sealed record WindowPlacement(
    int X,
    int Y,
    int Width,
    int Height,
    string? MonitorId,
    int Dpi,
    bool IsAlwaysOnTop)
{
    public static WindowPlacement Default { get; } = new(
        0,
        0,
        360,
        560,
        null,
        96,
        true);
}

public sealed record NotificationPreference(bool Enabled, decimal ThresholdPercentage);

/// <summary>
/// Glanceable-insights preferences. All default to a calm, privacy-preserving
/// baseline: adaptive refresh on, agent-aware detection off, compact mode off,
/// and the reset acknowledgement off.
/// </summary>
public sealed record InsightPreferences(
    RefreshMode RefreshMode,
    bool AgentAwareRefresh,
    bool CompactMode,
    bool ResetCelebration)
{
    public static InsightPreferences Default { get; } = new(
        RefreshMode.Adaptive,
        false,
        false,
        false);
}

public sealed record AppSettings(
    WindowPlacement Window,
    bool StartWithWindows,
    IReadOnlyList<string> PinnedMetricIds,
    IReadOnlyDictionary<string, decimal> SoftBudgets,
    IReadOnlyDictionary<string, NotificationPreference> Notifications,
    InsightPreferences Insights)
{
    public static AppSettings Default { get; } = new(
        WindowPlacement.Default,
        false,
        [],
        new Dictionary<string, decimal>(StringComparer.Ordinal),
        new Dictionary<string, NotificationPreference>(StringComparer.Ordinal),
        InsightPreferences.Default);
}

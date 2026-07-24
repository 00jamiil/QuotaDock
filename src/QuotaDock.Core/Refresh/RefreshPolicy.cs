using QuotaDock.Core.Domain;

namespace QuotaDock.Core.Refresh;

public static class RefreshPolicy
{
    public static readonly TimeSpan ManualCooldown = TimeSpan.FromSeconds(30);

    public static TimeSpan IntervalFor(DataSourceKind source) => source switch
    {
        DataSourceKind.DashboardReader => TimeSpan.FromMinutes(15),
        DataSourceKind.OfficialApi or DataSourceKind.LocalCli => TimeSpan.FromMinutes(5),
        _ => throw new ArgumentOutOfRangeException(nameof(source), source, null)
    };

    public static bool CanRefreshManually(DateTimeOffset? lastManualRefresh, DateTimeOffset now) =>
        lastManualRefresh is null || now - lastManualRefresh.Value >= ManualCooldown;

    public static TimeSpan BackoffFor(int consecutiveFailures)
    {
        if (consecutiveFailures <= 0)
        {
            return TimeSpan.Zero;
        }

        var minutes = consecutiveFailures switch
        {
            1 => 1,
            2 => 2,
            3 => 5,
            _ => 15
        };
        return TimeSpan.FromMinutes(minutes);
    }
}


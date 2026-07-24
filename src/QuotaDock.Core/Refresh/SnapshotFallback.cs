using QuotaDock.Core.Domain;

namespace QuotaDock.Core.Refresh;

public static class SnapshotFallback
{
    public static ConnectorFetchResult PreserveLastGood(
        UsageSnapshot? previous,
        ConnectorFetchResult current)
    {
        ArgumentNullException.ThrowIfNull(current);
        if (current.IsSuccess || previous is null)
        {
            return current;
        }

        var stale = previous with
        {
            Health = ConnectionHealth.Stale,
            StatusMessage = current.Message
        };

        return current with { Snapshot = stale };
    }
}


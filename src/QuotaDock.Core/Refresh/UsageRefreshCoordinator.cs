using QuotaDock.Core.Abstractions;
using QuotaDock.Core.Domain;

namespace QuotaDock.Core.Refresh;

public sealed class UsageRefreshCoordinator
{
    private readonly IReadOnlyList<IUsageConnector> connectors;
    private readonly ISnapshotStore snapshotStore;

    public UsageRefreshCoordinator(
        IEnumerable<IUsageConnector> connectors,
        ISnapshotStore snapshotStore)
    {
        ArgumentNullException.ThrowIfNull(connectors);
        ArgumentNullException.ThrowIfNull(snapshotStore);
        this.connectors = connectors.ToArray();
        this.snapshotStore = snapshotStore;
    }

    public async Task<ConnectorFetchResult> RefreshAsync(
        ConnectorConnection connection,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);
        var candidates = connectors.Where(candidate =>
                candidate.Definition.Provider == connection.Provider &&
                candidate.Definition.Source == connection.Source)
            .ToArray();
        var connector = candidates.SingleOrDefault(candidate =>
                            string.Equals(connection.Id, candidate.Definition.Id, StringComparison.Ordinal) ||
                            connection.Id.StartsWith(
                                $"{candidate.Definition.Id}-",
                                StringComparison.Ordinal))
                        ?? (candidates.Length == 1 ? candidates[0] : null);
        var previous = await snapshotStore.LoadLatestAsync(connection.Id, cancellationToken).ConfigureAwait(false);

        ConnectorFetchResult current;
        if (connector is null)
        {
            current = ConnectorFetchResult.Failure(
                ConnectionHealth.Unavailable,
                "No compatible connector is registered for this connection.");
        }
        else
        {
            current = await connector.FetchAsync(connection, cancellationToken).ConfigureAwait(false);
        }

        if (current.IsSuccess)
        {
            await snapshotStore.SaveAsync(current.Snapshot!, cancellationToken).ConfigureAwait(false);
            return current;
        }

        return SnapshotFallback.PreserveLastGood(previous, current);
    }

    public async Task<IReadOnlyList<ConnectorFetchResult>> RefreshAllAsync(
        IEnumerable<ConnectorConnection> connections,
        int maximumConcurrency = 3,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connections);
        if (maximumConcurrency is < 1 or > 16)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumConcurrency));
        }

        using var gate = new SemaphoreSlim(maximumConcurrency, maximumConcurrency);
        var tasks = connections.Select(async connection =>
        {
            await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                return await RefreshAsync(connection, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                gate.Release();
            }
        });

        return await Task.WhenAll(tasks).ConfigureAwait(false);
    }
}

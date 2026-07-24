using QuotaDock.Core.Abstractions;
using QuotaDock.Core.Domain;
using QuotaDock.Core.Refresh;

namespace QuotaDock.Core.Tests;

public sealed class UsageRefreshCoordinatorTests
{
    [Fact]
    public async Task RefreshAsync_SavesAndReturnsFreshSnapshot()
    {
        var expected = Snapshot(ConnectionHealth.Fresh, 68m);
        var store = new MemorySnapshotStore();
        var connector = new StubConnector(ConnectorFetchResult.Success(expected));
        var coordinator = new UsageRefreshCoordinator([connector], store);

        var result = await coordinator.RefreshAsync(Connection(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Same(expected, result.Snapshot);
        Assert.Same(expected, store.Saved.Single());
    }

    [Fact]
    public async Task RefreshAsync_ReturnsStaleLastGoodWithoutPersistingFailure()
    {
        var previous = Snapshot(ConnectionHealth.Fresh, 72m);
        var store = new MemorySnapshotStore(previous);
        var connector = new StubConnector(ConnectorFetchResult.Failure(
            ConnectionHealth.Unavailable,
            "Provider unavailable."));
        var coordinator = new UsageRefreshCoordinator([connector], store);

        var result = await coordinator.RefreshAsync(Connection(), CancellationToken.None);

        Assert.Equal(ConnectionHealth.Stale, result.Snapshot!.Health);
        Assert.Equal(72m, result.Snapshot.Metrics.Single().Current);
        Assert.Empty(store.Saved);
    }

    [Fact]
    public async Task RefreshAsync_FailsHonestlyWhenNoConnectorMatches()
    {
        var coordinator = new UsageRefreshCoordinator([], new MemorySnapshotStore());

        var result = await coordinator.RefreshAsync(Connection(), CancellationToken.None);

        Assert.Null(result.Snapshot);
        Assert.Equal(ConnectionHealth.Unavailable, result.UpstreamHealth);
        Assert.Contains("connector", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RefreshAsync_RoutesToConnectorIdWhenProviderAndSourceAreShared()
    {
        var expected = Snapshot(ConnectionHealth.Fresh, 41m) with
        {
            ConnectionId = "openai-compatible-account"
        };
        var wrong = new NamedStubConnector(
            "openai-organization",
            ConnectorFetchResult.Failure(ConnectionHealth.Unavailable, "Wrong connector."));
        var right = new NamedStubConnector(
            "openai-compatible",
            ConnectorFetchResult.Success(expected));
        var coordinator = new UsageRefreshCoordinator([wrong, right], new MemorySnapshotStore());
        var connection = new ConnectorConnection(
            "openai-compatible-account",
            ProviderKind.OpenAI,
            "Compatible",
            DataSourceKind.OfficialApi,
            null,
            null);

        var result = await coordinator.RefreshAsync(connection);

        Assert.True(result.IsSuccess);
        Assert.Equal(0, wrong.FetchCount);
        Assert.Equal(1, right.FetchCount);
    }

    [Fact]
    public async Task RefreshAllAsync_RefreshesEveryConnectionAndPersistsEachSnapshot()
    {
        var store = new MemorySnapshotStore();
        var connector = new PerConnectionStubConnector();
        var coordinator = new UsageRefreshCoordinator([connector], store);
        var connections = Enumerable.Range(1, 5)
            .Select(index => Connection() with { Id = $"connection-{index}" })
            .ToArray();

        var results = await coordinator.RefreshAllAsync(connections, maximumConcurrency: 2);

        Assert.Equal(5, results.Count);
        Assert.All(results, result => Assert.True(result.IsSuccess));
        Assert.Equal(5, store.Saved.Count);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(17)]
    public async Task RefreshAllAsync_RejectsUnsafeConcurrency(int concurrency)
    {
        var coordinator = new UsageRefreshCoordinator([], new MemorySnapshotStore());

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            coordinator.RefreshAllAsync([], concurrency));
    }

    private static ConnectorConnection Connection() => new(
        "codex-personal", ProviderKind.OpenAI, "Personal", DataSourceKind.LocalCli, null, null);

    private static UsageSnapshot Snapshot(ConnectionHealth health, decimal value) => new(
        "codex-personal",
        ProviderKind.OpenAI,
        "Personal",
        DataSourceKind.LocalCli,
        new DateTimeOffset(2026, 7, 23, 12, 0, 0, TimeSpan.Zero),
        health,
        [UsageMetric.Create("session", "Session", MetricKind.QuotaPercentage,
            MetricDirection.Remaining, value, 100m, "%", MetricScope.Session, null)],
        null);

    private sealed class StubConnector(ConnectorFetchResult result) : IUsageConnector
    {
        public ConnectorDefinition Definition { get; } = new(
            "codex-personal", ProviderKind.OpenAI, "Codex", DataSourceKind.LocalCli,
            ConnectorCapabilities.Quota, false);

        public Task<ConnectorConnection> ConnectAsync(ConnectionRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<ConnectionValidationResult> ValidateAsync(ConnectorConnection connection, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<ConnectorFetchResult> FetchAsync(ConnectorConnection connection, CancellationToken cancellationToken = default) =>
            Task.FromResult(result);

        public Task DisconnectAsync(ConnectorConnection connection, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private sealed class PerConnectionStubConnector : IUsageConnector
    {
        public ConnectorDefinition Definition { get; } = new(
            "codex-personal", ProviderKind.OpenAI, "Codex", DataSourceKind.LocalCli,
            ConnectorCapabilities.Quota, false);

        public Task<ConnectorConnection> ConnectAsync(ConnectionRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<ConnectionValidationResult> ValidateAsync(ConnectorConnection connection, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<ConnectorFetchResult> FetchAsync(ConnectorConnection connection, CancellationToken cancellationToken = default) =>
            Task.FromResult(ConnectorFetchResult.Success(Snapshot(ConnectionHealth.Fresh, 50m) with
            {
                ConnectionId = connection.Id
            }));

        public Task DisconnectAsync(ConnectorConnection connection, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private sealed class NamedStubConnector(string id, ConnectorFetchResult result) : IUsageConnector
    {
        public int FetchCount { get; private set; }

        public ConnectorDefinition Definition { get; } = new(
            id,
            ProviderKind.OpenAI,
            id,
            DataSourceKind.OfficialApi,
            ConnectorCapabilities.Tokens,
            false);

        public Task<ConnectorConnection> ConnectAsync(
            ConnectionRequest request,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<ConnectionValidationResult> ValidateAsync(
            ConnectorConnection connection,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<ConnectorFetchResult> FetchAsync(
            ConnectorConnection connection,
            CancellationToken cancellationToken = default)
        {
            FetchCount++;
            return Task.FromResult(result);
        }

        public Task DisconnectAsync(
            ConnectorConnection connection,
            CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class MemorySnapshotStore(UsageSnapshot? latest = null) : ISnapshotStore
    {
        public List<UsageSnapshot> Saved { get; } = [];

        public Task SaveAsync(UsageSnapshot snapshot, CancellationToken cancellationToken = default)
        {
            Saved.Add(snapshot);
            latest = snapshot;
            return Task.CompletedTask;
        }

        public Task<UsageSnapshot?> LoadLatestAsync(string connectionId, CancellationToken cancellationToken = default) =>
            Task.FromResult(latest);

        public Task<IReadOnlyList<UsageSnapshot>> LoadLatestForAllAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<UsageSnapshot>>(latest is null ? [] : [latest]);

        public Task DeleteForConnectionAsync(string connectionId, CancellationToken cancellationToken = default)
        {
            latest = null;
            return Task.CompletedTask;
        }

        public Task<int> PurgeOlderThanAsync(DateTimeOffset cutoff, CancellationToken cancellationToken = default) =>
            Task.FromResult(0);
    }
}

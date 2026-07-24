using QuotaDock.Core.Domain;
using QuotaDock.Infrastructure.Persistence;

namespace QuotaDock.Infrastructure.Tests;

public sealed class SqliteSnapshotStoreTests : IAsyncLifetime
{
    private readonly string databasePath = Path.Combine(
        Path.GetTempPath(), $"quotadock-tests-{Guid.NewGuid():N}.db");

    private SqliteSnapshotStore store = null!;

    public async Task InitializeAsync()
    {
        store = new SqliteSnapshotStore(databasePath);
        await store.InitializeAsync();
    }

    public async Task DisposeAsync()
    {
        await store.DisposeAsync();
        File.Delete(databasePath);
    }

    [Fact]
    public async Task SaveAndLoadLatest_RoundTripsNormalizedMetrics()
    {
        var older = SnapshotAt(new DateTimeOffset(2026, 7, 22, 12, 0, 0, TimeSpan.Zero), 70m);
        var latest = SnapshotAt(new DateTimeOffset(2026, 7, 23, 12, 0, 0, TimeSpan.Zero), 64m);

        await store.SaveAsync(older);
        await store.SaveAsync(latest);

        var loaded = await store.LoadLatestAsync("codex-personal");

        Assert.NotNull(loaded);
        Assert.Equal(latest.CapturedAt, loaded.CapturedAt);
        Assert.Equal(64m, loaded.Metrics.Single().Current);
        Assert.Equal("%", loaded.Metrics.Single().Unit);
    }

    [Fact]
    public async Task PurgeOlderThan_RemovesOnlyExpiredSnapshots()
    {
        await store.SaveAsync(SnapshotAt(new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero), 10m));
        await store.SaveAsync(SnapshotAt(new DateTimeOffset(2026, 7, 23, 0, 0, 0, TimeSpan.Zero), 20m));

        var removed = await store.PurgeOlderThanAsync(new DateTimeOffset(2026, 6, 23, 0, 0, 0, TimeSpan.Zero));

        Assert.Equal(1, removed);
        Assert.Equal(20m, (await store.LoadLatestAsync("codex-personal"))!.Metrics.Single().Current);
    }

    [Fact]
    public async Task DeleteForConnection_RemovesAllSnapshotsForOnlyThatConnection()
    {
        await store.SaveAsync(SnapshotAt(DateTimeOffset.UtcNow, 20m));
        await store.SaveAsync(SnapshotAt(DateTimeOffset.UtcNow.AddMinutes(1), 30m) with
        {
            ConnectionId = "keep-this-connection"
        });

        await store.DeleteForConnectionAsync("codex-personal");

        Assert.Null(await store.LoadLatestAsync("codex-personal"));
        Assert.NotNull(await store.LoadLatestAsync("keep-this-connection"));
    }

    private static UsageSnapshot SnapshotAt(DateTimeOffset capturedAt, decimal value) => new(
        "codex-personal",
        ProviderKind.OpenAI,
        "Personal",
        DataSourceKind.LocalCli,
        capturedAt,
        ConnectionHealth.Fresh,
        [UsageMetric.Create("session", "Session", MetricKind.QuotaPercentage,
            MetricDirection.Remaining, value, 100m, "%", MetricScope.Session, null)],
        null);
}

using QuotaDock.Core.Domain;

namespace QuotaDock.Core.Abstractions;

public interface ISnapshotStore
{
    Task SaveAsync(UsageSnapshot snapshot, CancellationToken cancellationToken = default);
    Task<UsageSnapshot?> LoadLatestAsync(string connectionId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<UsageSnapshot>> LoadLatestForAllAsync(CancellationToken cancellationToken = default);
    Task DeleteForConnectionAsync(string connectionId, CancellationToken cancellationToken = default);
    Task<int> PurgeOlderThanAsync(DateTimeOffset cutoff, CancellationToken cancellationToken = default);
}

using QuotaDock.Core.Domain;

namespace QuotaDock.Core.Abstractions;

public interface IConnectionStore
{
    Task SaveAsync(ConnectorConnection connection, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ConnectorConnection>> LoadAllAsync(CancellationToken cancellationToken = default);
    Task DeleteAsync(string connectionId, CancellationToken cancellationToken = default);
    Task ClearAsync(CancellationToken cancellationToken = default);
}


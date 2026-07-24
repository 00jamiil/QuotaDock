using QuotaDock.Core.Domain;

namespace QuotaDock.Core.Abstractions;

public interface IUsageConnector
{
    ConnectorDefinition Definition { get; }

    Task<ConnectorConnection> ConnectAsync(
        ConnectionRequest request,
        CancellationToken cancellationToken = default);

    Task<ConnectionValidationResult> ValidateAsync(
        ConnectorConnection connection,
        CancellationToken cancellationToken = default);

    Task<ConnectorFetchResult> FetchAsync(
        ConnectorConnection connection,
        CancellationToken cancellationToken = default);

    Task DisconnectAsync(
        ConnectorConnection connection,
        CancellationToken cancellationToken = default);
}


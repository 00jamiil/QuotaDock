using System.Text.Json;
using QuotaDock.Core.Abstractions;
using QuotaDock.Core.Domain;

namespace QuotaDock.Connectors.Api;

public abstract class AdminApiConnectorBase(
    ISecretVault secretVault,
    TimeProvider timeProvider) : IUsageConnector
{
    protected ISecretVault SecretVault { get; } = secretVault;
    protected TimeProvider TimeProvider { get; } = timeProvider;

    public abstract ConnectorDefinition Definition { get; }

    public async Task<ConnectorConnection> ConnectAsync(
        ConnectionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Source != DataSourceKind.OfficialApi)
        {
            throw new ArgumentException("This connector requires the official API source.", nameof(request));
        }

        if (string.IsNullOrWhiteSpace(request.Secret))
        {
            throw new ArgumentException("An admin API key is required.", nameof(request));
        }

        var id = $"{Definition.Id}-{Guid.NewGuid():N}";
        var secretReference = $"connector-{id}";
        await SecretVault.SaveAsync(secretReference, request.Secret, cancellationToken).ConfigureAwait(false);

        return new ConnectorConnection(
            id,
            Definition.Provider,
            request.AccountLabel,
            DataSourceKind.OfficialApi,
            secretReference,
            request.Settings);
    }

    public async Task<ConnectionValidationResult> ValidateAsync(
        ConnectorConnection connection,
        CancellationToken cancellationToken = default)
    {
        var result = await FetchAsync(connection, cancellationToken).ConfigureAwait(false);
        return result.IsSuccess
            ? ConnectionValidationResult.Valid()
            : ConnectionValidationResult.Invalid(result.Message ?? "The connection could not be validated.");
    }

    public abstract Task<ConnectorFetchResult> FetchAsync(
        ConnectorConnection connection,
        CancellationToken cancellationToken = default);

    public async Task DisconnectAsync(
        ConnectorConnection connection,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);
        if (!string.IsNullOrWhiteSpace(connection.SecretReference))
        {
            await SecretVault.RemoveAsync(connection.SecretReference, cancellationToken).ConfigureAwait(false);
        }
    }

    protected async ValueTask<string?> GetSecretAsync(
        ConnectorConnection connection,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(connection.SecretReference))
        {
            return null;
        }

        return await SecretVault.RetrieveAsync(connection.SecretReference, cancellationToken).ConfigureAwait(false);
    }

    protected static ConnectorFetchResult Failure(Exception exception) => exception switch
    {
        ConnectorApiException api => ConnectorFetchResult.Failure(api.Health, api.Message, api.RetryAfter),
        HttpRequestException => ConnectorFetchResult.Failure(
            ConnectionHealth.Unavailable,
            "The provider usage service could not be reached."),
        JsonException => ConnectorFetchResult.Failure(
            ConnectionHealth.FormatChanged,
            "The provider returned an unrecognized usage payload."),
        _ => ConnectorFetchResult.Failure(
            ConnectionHealth.Unavailable,
            "The provider usage refresh failed.")
    };
}

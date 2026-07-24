using QuotaDock.Core.Abstractions;
using QuotaDock.Core.Domain;

namespace QuotaDock.Connectors.Moonshot;

/// <summary>
/// Reads Kimi subscription usage automatically from the local Kimi Code OAuth
/// credential — no copy/paste. Mirrors the Claude subscription connector:
/// QuotaDock reads a credential the official first-party tool already stores
/// locally and calls the account usage window, then normalizes the result. The
/// access token is used only in-memory for a single request and never
/// persisted.
/// </summary>
public sealed class KimiSubscriptionConnector(
    IKimiCredentialsReader credentialsReader,
    IKimiUsageClient usageClient,
    TimeProvider timeProvider) : IUsageConnector
{
    public ConnectorDefinition Definition { get; } = new(
        "kimi-subscription",
        ProviderKind.Moonshot,
        "Kimi subscription",
        DataSourceKind.LocalCli,
        ConnectorCapabilities.Quota | ConnectorCapabilities.ResetTimes,
        RequiresSecret: false);

    public Task<ConnectorConnection> ConnectAsync(
        ConnectionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return Task.FromResult(new ConnectorConnection(
            $"kimi-subscription-{Guid.NewGuid():N}",
            ProviderKind.Moonshot,
            request.AccountLabel,
            DataSourceKind.LocalCli,
            null,
            new Dictionary<string, string>(StringComparer.Ordinal)));
    }

    public async Task<ConnectionValidationResult> ValidateAsync(
        ConnectorConnection connection,
        CancellationToken cancellationToken = default)
    {
        var result = await FetchAsync(connection, cancellationToken).ConfigureAwait(false);
        return result.IsSuccess
            ? ConnectionValidationResult.Valid()
            : ConnectionValidationResult.Invalid(result.Message ?? "Kimi usage could not be read.");
    }

    public async Task<ConnectorFetchResult> FetchAsync(
        ConnectorConnection connection,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);

        var credentials = credentialsReader.Read();
        if (credentials is null)
        {
            return ConnectorFetchResult.Failure(
                ConnectionHealth.AuthenticationRequired,
                "Kimi sign-in was not found. Install the Kimi CLI and sign in, then retry.");
        }

        var now = timeProvider.GetUtcNow();
        if (credentials.IsExpired(now))
        {
            return ConnectorFetchResult.Failure(
                ConnectionHealth.AuthenticationRequired,
                "The local Kimi sign-in has expired. Run the Kimi CLI login to refresh it, then retry.");
        }

        try
        {
            var payload = await usageClient.ReadUsageAsync(credentials, cancellationToken).ConfigureAwait(false);
            var label = string.IsNullOrWhiteSpace(credentials.PlanType)
                ? connection.AccountLabel
                : $"{connection.AccountLabel} · {Capitalize(credentials.PlanType)}";
            return KimiUsageParser.Parse(connection.Id, label, payload, now);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (KimiUsageException exception)
        {
            return ConnectorFetchResult.Failure(exception.Health, exception.Message, exception.RetryAfter);
        }
        catch (HttpRequestException)
        {
            return ConnectorFetchResult.Failure(
                ConnectionHealth.Unavailable,
                "Kimi usage could not be reached. Saved values were kept.");
        }
        catch (Exception exception) when (
            exception is System.Text.Json.JsonException or InvalidOperationException or
            FormatException or ArgumentException or IOException)
        {
            return ConnectorFetchResult.Failure(
                ConnectionHealth.Unavailable,
                "Kimi usage could not be read this time. Saved values were kept.");
        }
    }

    public Task DisconnectAsync(
        ConnectorConnection connection,
        CancellationToken cancellationToken = default) => Task.CompletedTask;

    private static string Capitalize(string value) =>
        value.Length == 0 ? value : char.ToUpperInvariant(value[0]) + value[1..];
}

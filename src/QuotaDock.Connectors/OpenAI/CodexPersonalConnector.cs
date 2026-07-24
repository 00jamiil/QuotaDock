using System.ComponentModel;
using QuotaDock.Core.Abstractions;
using QuotaDock.Core.Domain;

namespace QuotaDock.Connectors.OpenAI;

public sealed class CodexPersonalConnector(
    ICodexAppServerClient appServerClient,
    TimeProvider timeProvider) : IUsageConnector
{
    public ConnectorDefinition Definition { get; } = new(
        "codex-personal",
        ProviderKind.OpenAI,
        "OpenAI Codex",
        DataSourceKind.LocalCli,
        ConnectorCapabilities.Quota |
        ConnectorCapabilities.Credits |
        ConnectorCapabilities.Tokens |
        ConnectorCapabilities.ResetTimes,
        RequiresSecret: false);

    public Task<ConnectorConnection> ConnectAsync(
        ConnectionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var settings = new Dictionary<string, string>(request.Settings, StringComparer.Ordinal)
        {
            ["executable"] = request.Settings.TryGetValue("executable", out var executable)
                ? executable
                : "codex.exe"
        };
        return Task.FromResult(new ConnectorConnection(
            $"codex-personal-{Guid.NewGuid():N}",
            ProviderKind.OpenAI,
            request.AccountLabel,
            DataSourceKind.LocalCli,
            null,
            settings));
    }

    public async Task<ConnectionValidationResult> ValidateAsync(
        ConnectorConnection connection,
        CancellationToken cancellationToken = default)
    {
        var result = await FetchAsync(connection, cancellationToken).ConfigureAwait(false);
        return result.IsSuccess
            ? ConnectionValidationResult.Valid()
            : ConnectionValidationResult.Invalid(result.Message ?? "Codex usage could not be read.");
    }

    public async Task<ConnectorFetchResult> FetchAsync(
        ConnectorConnection connection,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);
        var executable = connection.Settings is not null &&
                         connection.Settings.TryGetValue("executable", out var configured)
            ? configured
            : "codex.exe";

        try
        {
            var payloads = await appServerClient.ReadUsageAsync(executable, cancellationToken).ConfigureAwait(false);
            return CodexAppServerPayloadParser.Parse(
                connection.Id,
                connection.AccountLabel,
                payloads.RateLimitsPayload,
                payloads.UsagePayload,
                timeProvider.GetUtcNow());
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is Win32Exception or FileNotFoundException)
        {
            return ConnectorFetchResult.Failure(
                ConnectionHealth.Unavailable,
                "Codex CLI was not found. Install or update the official Codex CLI, then retry.");
        }
        catch (OperationCanceledException)
        {
            return ConnectorFetchResult.Failure(
                ConnectionHealth.Unavailable,
                "Codex CLI did not return usage before the local timeout.");
        }
        catch (Exception exception) when (exception is InvalidOperationException or ArgumentException)
        {
            return ConnectorFetchResult.Failure(
                ConnectionHealth.Unavailable,
                "Codex CLI could not provide account usage.");
        }
    }

    public Task DisconnectAsync(
        ConnectorConnection connection,
        CancellationToken cancellationToken = default) => Task.CompletedTask;
}

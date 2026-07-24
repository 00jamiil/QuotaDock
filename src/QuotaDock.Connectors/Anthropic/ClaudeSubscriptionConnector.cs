using QuotaDock.Core.Abstractions;
using QuotaDock.Core.Domain;

namespace QuotaDock.Connectors.Anthropic;

/// <summary>
/// Reads Claude subscription (Claude Code / claude.ai) session and weekly quota
/// automatically from the local Claude Code OAuth credential — no copy/paste.
/// This mirrors the Codex personal connector: QuotaDock reads a credential the
/// official first-party tool already stores locally and calls the same
/// account-usage window the tool uses, then normalizes the result. The access
/// token is used only in-memory for a single request and never persisted.
/// </summary>
public sealed class ClaudeSubscriptionConnector(
    IClaudeCredentialsReader credentialsReader,
    IClaudeUsageClient usageClient,
    TimeProvider timeProvider,
    Func<string?>? costLogPathProvider = null) : IUsageConnector
{
    private readonly Func<string?> costLogPathProvider =
        costLogPathProvider ?? ClaudeCostLogReader.DefaultCostLogPath;

    public ConnectorDefinition Definition { get; } = new(
        "claude-subscription",
        ProviderKind.Anthropic,
        "Claude subscription",
        DataSourceKind.LocalCli,
        ConnectorCapabilities.Quota | ConnectorCapabilities.ResetTimes | ConnectorCapabilities.Tokens |
        ConnectorCapabilities.Costs,
        RequiresSecret: false);

    public Task<ConnectorConnection> ConnectAsync(
        ConnectionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return Task.FromResult(new ConnectorConnection(
            $"claude-subscription-{Guid.NewGuid():N}",
            ProviderKind.Anthropic,
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
            : ConnectionValidationResult.Invalid(result.Message ?? "Claude usage could not be read.");
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
                "Claude Code sign-in was not found. Install Claude Code and sign in, then retry.");
        }

        var now = timeProvider.GetUtcNow();
        if (credentials.IsExpired(now))
        {
            return ConnectorFetchResult.Failure(
                ConnectionHealth.AuthenticationRequired,
                "The local Claude sign-in has expired. Open Claude Code to refresh it, then retry.");
        }

        try
        {
            var payload = await usageClient.ReadUsageAsync(credentials, cancellationToken).ConfigureAwait(false);
            var label = string.IsNullOrWhiteSpace(credentials.SubscriptionType)
                ? connection.AccountLabel
                : $"{connection.AccountLabel} · {Capitalize(credentials.SubscriptionType)}";
            var result = ClaudeUsageParser.Parse(connection.Id, label, payload, now);
            if (!result.IsSuccess)
            {
                return result;
            }

            // Enrich the live quota snapshot with on-device month-to-date token
            // and cost totals from Claude Code's own local log, when present.
            var monthMetrics = ClaudeCostLogReader.ReadMonthToDate(costLogPathProvider(), now);
            if (monthMetrics.Count == 0)
            {
                return result;
            }

            var combined = result.Snapshot!.Metrics.Concat(monthMetrics).ToArray();
            return ConnectorFetchResult.Success(result.Snapshot with { Metrics = combined });
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (ClaudeUsageException exception)
        {
            return ConnectorFetchResult.Failure(exception.Health, exception.Message, exception.RetryAfter);
        }
        catch (HttpRequestException)
        {
            return ConnectorFetchResult.Failure(
                ConnectionHealth.Unavailable,
                "Claude usage could not be reached. Saved values were kept.");
        }
        catch (Exception exception) when (
            exception is System.Text.Json.JsonException or InvalidOperationException or
            FormatException or ArgumentException or IOException)
        {
            // Any unexpected local read/parse failure must degrade to a preserved
            // last-good snapshot, never crash the refresh loop or fabricate zeros.
            return ConnectorFetchResult.Failure(
                ConnectionHealth.Unavailable,
                "Claude usage could not be read this time. Saved values were kept.");
        }
    }

    public Task DisconnectAsync(
        ConnectorConnection connection,
        CancellationToken cancellationToken = default) => Task.CompletedTask;

    private static string Capitalize(string value) =>
        value.Length == 0 ? value : char.ToUpperInvariant(value[0]) + value[1..];
}

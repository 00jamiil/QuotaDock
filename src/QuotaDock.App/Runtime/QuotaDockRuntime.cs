using QuotaDock.Connectors.Anthropic;
using QuotaDock.Connectors.Moonshot;
using QuotaDock.Connectors.OpenAI;
using QuotaDock.Connectors.Personal;
using QuotaDock.Connectors.Xai;
using QuotaDock.Core.Abstractions;
using QuotaDock.Core.Configuration;
using QuotaDock.Core.Domain;
using QuotaDock.Core.Presentation;
using QuotaDock.Core.Refresh;
using QuotaDock.Infrastructure.Persistence;
using QuotaDock.Infrastructure.Security;

namespace QuotaDock.App.Runtime;

public sealed record RefreshOutcome(bool Started, string Message);

/// <summary>
/// The result of an app-driven provider sign-in. When the provider's CLI is
/// not installed, <see cref="CliMissing"/> is true and <see cref="InstallUrl"/>
/// points at the official installation guide so the UI can offer to open it.
/// </summary>
public sealed record ProviderSignInOutcome(
    bool Succeeded,
    string Message,
    bool CliMissing = false,
    string? InstallUrl = null);

public sealed record AutoDetectOutcome(int Added, int AlreadyPresent, IReadOnlyList<string> Notes)
{
    public string Summary
    {
        get
        {
            if (Added == 0 && AlreadyPresent == 0)
            {
                return "No local AI tools were detected. Connect a provider manually below.";
            }

            var parts = new List<string>();
            if (Added > 0)
            {
                parts.Add($"connected {Added} new provider{(Added == 1 ? string.Empty : "s")}");
            }

            if (AlreadyPresent > 0)
            {
                parts.Add($"{AlreadyPresent} already connected");
            }

            return $"Auto-detect: {string.Join(", ", parts)}.";
        }
    }
}

public sealed class QuotaDockRuntime : IAsyncDisposable
{
    private readonly SqliteSnapshotStore snapshotStore;
    private readonly SqliteConnectionStore connectionStore;
    private readonly SqliteAppSettingsStore settingsStore;
    private readonly WindowsCredentialVault secretVault;
    private readonly IReadOnlyList<IUsageConnector> connectors;
    private readonly UsageRefreshCoordinator refreshCoordinator;
    private readonly HttpClient claudeUsageClient;
    private readonly HttpClient grokUsageClient;
    private readonly HttpClient kimiUsageClient;
    private readonly CancellationTokenSource lifetime = new();
    private readonly SemaphoreSlim refreshGate = new(1, 1);
    private readonly Dictionary<string, DateTimeOffset> lastRefreshes = new(StringComparer.Ordinal);
    private static readonly TimeSpan AgentActivityWindow = TimeSpan.FromMinutes(10);
    private DateTimeOffset? lastAgentActivity;
    private DateTimeOffset lastAgentProbe = DateTimeOffset.MinValue;
    private Task? backgroundLoop;
    private DateTimeOffset? lastManualRefresh;
    private bool initialized;

    public QuotaDockRuntime(bool isEndToEndMode)
    {
        IsEndToEndMode = isEndToEndMode;
        // In end-to-end mode we must never touch the user's real local database.
        // GetFolderPath(LocalApplicationData) ignores the LOCALAPPDATA env var on
        // Windows, so the UI test sandbox cannot redirect it. GetTempPath does
        // honor TEMP/TMP, which the test harness sets, giving each run its own
        // isolated, disposable database.
        var dataRoot = isEndToEndMode
            ? Path.Combine(Path.GetTempPath(), "QuotaDock-e2e")
            : Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "QuotaDock");
        Directory.CreateDirectory(dataRoot);
        var databasePath = Path.Combine(dataRoot, "quotadock.db");

        snapshotStore = new SqliteSnapshotStore(databasePath);
        connectionStore = new SqliteConnectionStore(databasePath);
        settingsStore = new SqliteAppSettingsStore(databasePath);
        secretVault = new WindowsCredentialVault("QuotaDock");

        claudeUsageClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(30)
        };
        grokUsageClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(30)
        };
        kimiUsageClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(30)
        };

        connectors =
        [
            new CodexPersonalConnector(new CodexAppServerClient(), TimeProvider.System),
            new ClaudeSubscriptionConnector(
                new ClaudeLocalCredentialsReader(),
                new ClaudeUsageClient(claudeUsageClient),
                TimeProvider.System),
            new GrokSubscriptionConnector(
                new GrokLocalCredentialsReader(),
                new GrokUsageClient(grokUsageClient),
                TimeProvider.System),
            new KimiSubscriptionConnector(
                new KimiLocalCredentialsReader(),
                new KimiUsageClient(kimiUsageClient),
                TimeProvider.System)
        ];
        refreshCoordinator = new UsageRefreshCoordinator(connectors, snapshotStore);
    }

    public bool IsEndToEndMode { get; }
    public IReadOnlyList<ConnectorConnection> Connections { get; private set; } = [];
    public IReadOnlyList<UsageSnapshot> Snapshots { get; private set; } = [];
    public AppSettings Settings { get; private set; } = AppSettings.Default;
    public string? LastError { get; private set; }

    /// <summary>
    /// True only when the user has opted into agent-aware refresh AND a supported
    /// local coding agent was recently observed running. Detection is a bounded
    /// process-name check that records only the latest activity time; it never
    /// stores command lines, paths, or identities.
    /// </summary>
    public bool AgentActive
    {
        get
        {
            if (!Settings.Insights.AgentAwareRefresh)
            {
                return false;
            }

            var now = TimeProvider.System.GetUtcNow();
            if (now - lastAgentProbe >= TimeSpan.FromMinutes(1))
            {
                lastAgentProbe = now;
                if (IsCodingAgentRunning())
                {
                    lastAgentActivity = now;
                }
            }

            return lastAgentActivity is { } last && now - last <= AgentActivityWindow;
        }
    }

    private static bool IsCodingAgentRunning()
    {
        // Bounded, name-only probe. No command lines, paths, or window titles are
        // read, and nothing is persisted beyond an in-memory activity timestamp.
        foreach (var name in new[] { "codex", "claude", "grok", "kimi" })
        {
            try
            {
                if (System.Diagnostics.Process.GetProcessesByName(name).Length > 0)
                {
                    return true;
                }
            }
            catch
            {
                // Never let a probe failure disrupt refresh scheduling.
            }
        }

        return false;
    }

    public event EventHandler? StateChanged;

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (initialized)
        {
            return;
        }

        await snapshotStore.InitializeAsync(cancellationToken).ConfigureAwait(false);
        await connectionStore.InitializeAsync(cancellationToken).ConfigureAwait(false);
        await settingsStore.InitializeAsync(cancellationToken).ConfigureAwait(false);
        Settings = await settingsStore.LoadAsync(cancellationToken).ConfigureAwait(false);
        Connections = await connectionStore.LoadAllAsync(cancellationToken).ConfigureAwait(false);

        if (IsEndToEndMode)
        {
            await SeedEndToEndSnapshotAsync(cancellationToken).ConfigureAwait(false);
        }

        Snapshots = await snapshotStore.LoadLatestForAllAsync(cancellationToken).ConfigureAwait(false);
        await snapshotStore.PurgeOlderThanAsync(
            TimeProvider.System.GetUtcNow().AddDays(-30),
            cancellationToken).ConfigureAwait(false);
        initialized = true;
        StateChanged?.Invoke(this, EventArgs.Empty);

        if (!IsEndToEndMode)
        {
            backgroundLoop = RunBackgroundRefreshAsync(lifetime.Token);
            _ = RefreshDueConnectionsAsync(lifetime.Token);
        }
    }

    public async Task<RefreshOutcome> RefreshManuallyAsync(CancellationToken cancellationToken = default)
    {
        var now = TimeProvider.System.GetUtcNow();
        if (!RefreshPolicy.CanRefreshManually(lastManualRefresh, now))
        {
            var remaining = RefreshPolicy.ManualCooldown - (now - lastManualRefresh!.Value);
            return new RefreshOutcome(false, $"Refresh available in {Math.Ceiling(remaining.TotalSeconds)} seconds");
        }

        lastManualRefresh = now;
        if (IsEndToEndMode)
        {
            StateChanged?.Invoke(this, EventArgs.Empty);
            return new RefreshOutcome(true, "Up to date");
        }

        await RefreshConnectionsAsync(Connections.ToArray(), cancellationToken)
            .ConfigureAwait(false);
        return new RefreshOutcome(true, LastError is null ? "Up to date" : "Some providers need attention");
    }

    /// <summary>
    /// Scans for locally available first-party AI tools (Codex CLI and Claude
    /// Code sign-in) and connects any that are present but not yet configured.
    /// Detection is bounded and read-only: it probes for the Codex executable and
    /// checks whether a local Claude Code credential exists. Nothing is added
    /// twice, and providers that require a manual API key are never auto-added.
    /// </summary>
    public async Task<AutoDetectOutcome> AutoDetectAsync(CancellationToken cancellationToken = default)
    {
        var notes = new List<string>();
        var added = 0;
        var alreadyPresent = 0;

        // Codex personal (local CLI).
        var hasCodex = Connections.Any(c => c.Provider == ProviderKind.OpenAI && c.Source == DataSourceKind.LocalCli);
        if (hasCodex)
        {
            alreadyPresent++;
        }
        else
        {
            var executable = await new CodexCliLocator().FindAsync(cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            if (executable is not null)
            {
                var result = await ConnectAsync(
                    "codex-personal",
                    "Codex account",
                    null,
                    new Dictionary<string, string>(StringComparer.Ordinal) { ["executable"] = executable },
                    cancellationToken).ConfigureAwait(false);
                if (result.IsValid)
                {
                    added++;
                    notes.Add("Codex CLI detected and connected.");
                }
            }
        }

        // Claude subscription (local Claude Code credential).
        var hasClaudeLocal = Connections.Any(c =>
            c.Provider == ProviderKind.Anthropic && c.Source == DataSourceKind.LocalCli);
        if (hasClaudeLocal)
        {
            alreadyPresent++;
        }
        else if (new ClaudeLocalCredentialsReader().Read() is not null)
        {
            var result = await ConnectAsync(
                "claude-subscription",
                "Claude account",
                null,
                null,
                cancellationToken).ConfigureAwait(false);
            if (result.IsValid)
            {
                added++;
                notes.Add("Claude Code sign-in detected and connected.");
            }
            else
            {
                notes.Add(result.Message ?? "Claude Code sign-in found but usage could not be read yet.");
            }
        }

        // Grok subscription (local Grok CLI credential).
        var hasGrokLocal = Connections.Any(c =>
            c.Provider == ProviderKind.Xai && c.Source == DataSourceKind.LocalCli);
        if (hasGrokLocal)
        {
            alreadyPresent++;
        }
        else if (new GrokLocalCredentialsReader().Read() is not null)
        {
            var result = await ConnectAsync(
                "grok-subscription",
                "Grok account",
                null,
                null,
                cancellationToken).ConfigureAwait(false);
            if (result.IsValid)
            {
                added++;
                notes.Add("Grok sign-in detected and connected.");
            }
            else
            {
                notes.Add(result.Message ?? "Grok sign-in found but usage could not be read yet.");
            }
        }

        // Kimi subscription (local Kimi Code credential).
        var hasKimiLocal = Connections.Any(c =>
            c.Provider == ProviderKind.Moonshot && c.Source == DataSourceKind.LocalCli);
        if (hasKimiLocal)
        {
            alreadyPresent++;
        }
        else if (new KimiLocalCredentialsReader().Read() is not null)
        {
            var result = await ConnectAsync(
                "kimi-subscription",
                "Kimi account",
                null,
                null,
                cancellationToken).ConfigureAwait(false);
            if (result.IsValid)
            {
                added++;
                notes.Add("Kimi Code sign-in detected and connected.");
            }
            else
            {
                notes.Add(result.Message ?? "Kimi Code sign-in found but usage could not be read yet.");
            }
        }

        return new AutoDetectOutcome(added, alreadyPresent, notes);
    }

    /// <summary>
    /// App-driven sign-in for the Claude subscription. Runs the official
    /// Claude Code login (which opens the user's default browser), then reads
    /// the refreshed local credential and connects or refreshes the provider.
    /// The user never has to touch a terminal.
    /// </summary>
    public async Task<ProviderSignInOutcome> SignInClaudeAsync(CancellationToken cancellationToken = default)
    {
        var executable = await new ClaudeCliLocator().FindAsync(cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        if (executable is null)
        {
            return new ProviderSignInOutcome(
                false,
                "Claude Code is not installed on this PC. QuotaDock reads your Claude limits through Claude Code, so install it first, then sign in from here.",
                CliMissing: true,
                InstallUrl: "https://docs.claude.com/en/docs/claude-code/setup");
        }

        var login = await new CliSignInLauncher()
            .SignInAsync(executable, ["auth", "login", "--claudeai"], cancellationToken)
            .ConfigureAwait(false);
        if (!login.Succeeded)
        {
            return new ProviderSignInOutcome(false, login.Message);
        }

        return await CompleteSignInAsync(
            ProviderKind.Anthropic,
            "claude-subscription",
            "Claude account",
            null,
            "Claude sign-in finished, but usage could not be read yet.",
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// App-driven sign-in for local Codex. Runs <c>codex login</c> (which opens
    /// the browser) and then connects or refreshes the Codex provider.
    /// </summary>
    public async Task<ProviderSignInOutcome> SignInCodexAsync(CancellationToken cancellationToken = default)
    {
        var executable = await new CodexCliLocator().FindAsync(cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        if (executable is null)
        {
            return new ProviderSignInOutcome(
                false,
                "The Codex CLI is not installed on this PC. QuotaDock reads your Codex usage through the official CLI, so install it first, then sign in from here.",
                CliMissing: true,
                InstallUrl: "https://developers.openai.com/codex/cli/");
        }

        var login = await new CliSignInLauncher()
            .SignInAsync(executable, ["login"], cancellationToken)
            .ConfigureAwait(false);
        if (!login.Succeeded)
        {
            return new ProviderSignInOutcome(false, login.Message);
        }

        return await CompleteSignInAsync(
            ProviderKind.OpenAI,
            "codex-personal",
            "Codex account",
            new Dictionary<string, string>(StringComparer.Ordinal) { ["executable"] = executable },
            "Codex sign-in finished, but usage could not be read yet.",
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// App-driven sign-in for local Grok. Runs the Grok CLI login (which opens
    /// the browser) and then connects or refreshes the Grok provider.
    /// </summary>
    public async Task<ProviderSignInOutcome> SignInGrokAsync(CancellationToken cancellationToken = default)
    {
        var executable = await new GrokCliLocator().FindAsync(cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        if (executable is null)
        {
            return new ProviderSignInOutcome(
                false,
                "The Grok CLI is not installed on this PC. QuotaDock reads your Grok usage through the official CLI, so install it first, then sign in from here.",
                CliMissing: true,
                InstallUrl: "https://docs.x.ai/build/cli");
        }

        var login = await new CliSignInLauncher()
            .SignInAsync(executable, ["login"], cancellationToken)
            .ConfigureAwait(false);
        if (!login.Succeeded)
        {
            return new ProviderSignInOutcome(false, login.Message);
        }

        return await CompleteSignInAsync(
            ProviderKind.Xai,
            "grok-subscription",
            "Grok account",
            null,
            "Grok sign-in finished, but usage could not be read yet.",
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// App-driven sign-in for local Kimi. Runs the Kimi CLI login (which opens
    /// the browser) and then connects or refreshes the Kimi provider.
    /// </summary>
    public async Task<ProviderSignInOutcome> SignInKimiAsync(CancellationToken cancellationToken = default)
    {
        var executable = await new KimiCliLocator().FindAsync(cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        if (executable is null)
        {
            return new ProviderSignInOutcome(
                false,
                "The Kimi CLI is not installed on this PC. QuotaDock reads your Kimi usage through the official CLI, so install it first, then sign in from here.",
                CliMissing: true,
                InstallUrl: "https://moonshotai.github.io/kimi-code/");
        }

        var login = await new CliSignInLauncher()
            .SignInAsync(executable, ["login"], cancellationToken)
            .ConfigureAwait(false);
        if (!login.Succeeded)
        {
            return new ProviderSignInOutcome(false, login.Message);
        }

        return await CompleteSignInAsync(
            ProviderKind.Moonshot,
            "kimi-subscription",
            "Kimi account",
            null,
            "Kimi sign-in finished, but usage could not be read yet.",
            cancellationToken).ConfigureAwait(false);
    }

    private async Task<ProviderSignInOutcome> CompleteSignInAsync(
        ProviderKind provider,
        string connectorId,
        string accountLabel,
        IReadOnlyDictionary<string, string>? settings,
        string failureMessage,
        CancellationToken cancellationToken)
    {
        // If the provider is already connected, the fresh credential just needs
        // a refresh; otherwise connect it for the first time.
        var existing = Connections.FirstOrDefault(connection =>
            connection.Provider == provider && connection.Source == DataSourceKind.LocalCli);
        if (existing is not null)
        {
            await RefreshConnectionsAsync([existing], cancellationToken).ConfigureAwait(false);
            var latest = Snapshots.FirstOrDefault(snapshot => snapshot.ConnectionId == existing.Id);
            return latest is { Health: ConnectionHealth.Fresh }
                ? new ProviderSignInOutcome(true, "Signed in. Usage is up to date.")
                : new ProviderSignInOutcome(false, LastError ?? failureMessage);
        }

        var result = await ConnectAsync(connectorId, accountLabel, null, settings, cancellationToken)
            .ConfigureAwait(false);
        return result.IsValid
            ? new ProviderSignInOutcome(true, "Signed in and connected.")
            : new ProviderSignInOutcome(false, result.Message ?? failureMessage);
    }

    public async Task<ConnectionValidationResult> ConnectAsync(
        string connectorId,
        string accountLabel,
        string? secret,
        IReadOnlyDictionary<string, string>? settings = null,
        CancellationToken cancellationToken = default)
    {
        var connector = connectors.SingleOrDefault(item => item.Definition.Id == connectorId);
        if (connector is null)
        {
            return ConnectionValidationResult.Invalid("This connector is not available in this build.");
        }

        ConnectorConnection? connection = null;
        try
        {
            connection = await connector.ConnectAsync(
                new ConnectionRequest(accountLabel, connector.Definition.Source, secret, settings),
                cancellationToken).ConfigureAwait(false);
            var validation = await connector.ValidateAsync(connection, cancellationToken).ConfigureAwait(false);
            if (!validation.IsValid)
            {
                await connector.DisconnectAsync(connection, cancellationToken).ConfigureAwait(false);
                return validation;
            }

            await connectionStore.SaveAsync(connection, cancellationToken).ConfigureAwait(false);
            Connections = await connectionStore.LoadAllAsync(cancellationToken).ConfigureAwait(false);
            await refreshCoordinator.RefreshAsync(connection, cancellationToken).ConfigureAwait(false);
            Snapshots = await snapshotStore.LoadLatestForAllAsync(cancellationToken).ConfigureAwait(false);
            LastError = null;
            StateChanged?.Invoke(this, EventArgs.Empty);
            return ConnectionValidationResult.Valid();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            if (connection is not null)
            {
                await connector.DisconnectAsync(connection, CancellationToken.None).ConfigureAwait(false);
            }

            throw;
        }
        catch
        {
            if (connection is not null)
            {
                await connector.DisconnectAsync(connection, CancellationToken.None).ConfigureAwait(false);
            }

            return ConnectionValidationResult.Invalid("The provider connection could not be completed.");
        }
    }

    public async Task DisconnectAsync(string connectionId, CancellationToken cancellationToken = default)
    {
        var connection = Connections.SingleOrDefault(item => item.Id == connectionId);
        if (connection is null)
        {
            return;
        }

        var connector = FindConnector(connection);
        if (connector is not null)
        {
            await connector.DisconnectAsync(connection, cancellationToken).ConfigureAwait(false);
        }
        else if (!string.IsNullOrWhiteSpace(connection.SecretReference))
        {
            await secretVault.RemoveAsync(connection.SecretReference, cancellationToken).ConfigureAwait(false);
        }

        await connectionStore.DeleteAsync(connectionId, cancellationToken).ConfigureAwait(false);
        await snapshotStore.DeleteForConnectionAsync(connectionId, cancellationToken).ConfigureAwait(false);
        Connections = await connectionStore.LoadAllAsync(cancellationToken).ConfigureAwait(false);
        Snapshots = await snapshotStore.LoadLatestForAllAsync(cancellationToken).ConfigureAwait(false);
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public async Task SaveSettingsAsync(AppSettings settings, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        Settings = settings;
        await settingsStore.SaveAsync(settings, cancellationToken).ConfigureAwait(false);
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public async ValueTask DisposeAsync()
    {
        lifetime.Cancel();
        if (backgroundLoop is not null)
        {
            try
            {
                await backgroundLoop.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Normal application shutdown.
            }
        }

        lifetime.Dispose();
        refreshGate.Dispose();
        claudeUsageClient.Dispose();
        grokUsageClient.Dispose();
        kimiUsageClient.Dispose();
        await snapshotStore.DisposeAsync().ConfigureAwait(false);
        await connectionStore.DisposeAsync().ConfigureAwait(false);
        await settingsStore.DisposeAsync().ConfigureAwait(false);
    }

    private async Task RunBackgroundRefreshAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));
        while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
        {
            try
            {
                await RefreshDueConnectionsAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch
            {
                // A single bad connection or transient I/O error must never
                // kill the background refresh loop. The next tick retries.
            }
        }
    }

    private async Task RefreshDueConnectionsAsync(CancellationToken cancellationToken)
    {
        var now = TimeProvider.System.GetUtcNow();
        var due = Connections.Where(connection =>
                !lastRefreshes.TryGetValue(connection.Id, out var last) ||
                now - last >= NextIntervalFor(connection, now))
            .ToArray();
        await RefreshConnectionsAsync(due, cancellationToken).ConfigureAwait(false);
    }

    private TimeSpan NextIntervalFor(ConnectorConnection connection, DateTimeOffset now)
    {
        var latest = Snapshots.FirstOrDefault(snapshot => snapshot.ConnectionId == connection.Id);
        var worstPace = PaceStatus.Unknown;
        TimeSpan? soonestReset = null;

        if (latest is not null)
        {
            foreach (var metric in latest.Metrics)
            {
                if (metric.ResetsAt is { } reset)
                {
                    var remaining = reset - now;
                    if (remaining > TimeSpan.Zero && (soonestReset is null || remaining < soonestReset))
                    {
                        soonestReset = remaining;
                    }

                    var pace = UsagePace.Calculate(metric, latest.CapturedAt, now).Status;
                    if (pace > worstPace)
                    {
                        worstPace = pace;
                    }
                }
            }
        }

        var context = new AdaptiveRefreshContext(
            connection.Source,
            Settings.Insights.RefreshMode,
            0,
            null,
            soonestReset,
            worstPace,
            AgentActive);
        return AdaptiveRefreshPlanner.NextInterval(context);
    }

    private async Task RefreshConnectionsAsync(
        IReadOnlyList<ConnectorConnection> connections,
        CancellationToken cancellationToken)
    {
        if (connections.Count == 0 || !await refreshGate.WaitAsync(0, cancellationToken).ConfigureAwait(false))
        {
            return;
        }

        try
        {
            var results = await refreshCoordinator.RefreshAllAsync(connections, cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            var now = TimeProvider.System.GetUtcNow();
            foreach (var connection in connections)
            {
                lastRefreshes[connection.Id] = now;
            }

            LastError = results.FirstOrDefault(result => !result.IsSuccess)?.Message;
            Snapshots = await snapshotStore.LoadLatestForAllAsync(cancellationToken).ConfigureAwait(false);
            StateChanged?.Invoke(this, EventArgs.Empty);
        }
        finally
        {
            refreshGate.Release();
        }
    }

    private async Task SeedEndToEndSnapshotAsync(CancellationToken cancellationToken)
    {
        const string connectionId = "e2e-sample";
        if (await snapshotStore.LoadLatestAsync(connectionId, cancellationToken).ConfigureAwait(false) is not null)
        {
            return;
        }

        var now = TimeProvider.System.GetUtcNow();
        await snapshotStore.SaveAsync(new UsageSnapshot(
            connectionId,
            ProviderKind.OpenAI,
            "E2E sample",
            DataSourceKind.LocalCli,
            now,
            ConnectionHealth.Fresh,
            [
                UsageMetric.Create("session", "Session remaining", MetricKind.QuotaPercentage,
                    MetricDirection.Remaining, 72m, 100m, "%", MetricScope.Session, now.AddHours(3)),
                UsageMetric.Create("weekly", "Weekly remaining", MetricKind.QuotaPercentage,
                    MetricDirection.Remaining, 44m, 100m, "%", MetricScope.Weekly, now.AddDays(4)),
                UsageMetric.Create("credits", "Credits remaining", MetricKind.Credits,
                    MetricDirection.Remaining, 12.5m, 20m, "credits", MetricScope.Account, null)
            ],
            null), cancellationToken).ConfigureAwait(false);
    }

    private IUsageConnector? FindConnector(ConnectorConnection connection)
    {
        var candidates = connectors.Where(item =>
                item.Definition.Provider == connection.Provider &&
                item.Definition.Source == connection.Source)
            .ToArray();
        return candidates.SingleOrDefault(item =>
                   string.Equals(connection.Id, item.Definition.Id, StringComparison.Ordinal) ||
                   connection.Id.StartsWith($"{item.Definition.Id}-", StringComparison.Ordinal))
               ?? (candidates.Length == 1 ? candidates[0] : null);
    }
}

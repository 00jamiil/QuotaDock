using QuotaDock.Connectors.OpenAI;
using QuotaDock.Core.Domain;

namespace QuotaDock.Connectors.Tests;

public sealed class CodexPersonalConnectorTests
{
    [Fact]
    public async Task FetchAsync_UsesReadOnlyAppServerClientAndReturnsQuota()
    {
        var client = new StubCodexClient(new CodexAppServerResult(
            """
            {"id":2,"result":{"rateLimits":{"planType":"pro","primary":{"usedPercent":10,"windowDurationMins":300,"resetsAt":1784822400},"secondary":null,"credits":null}}}
            """,
            """
            {"id":3,"result":{"summary":{},"dailyUsageBuckets":[]}}
            """));
        var connector = new CodexPersonalConnector(
            client,
            new FixedTimeProvider(new DateTimeOffset(2026, 7, 23, 12, 0, 0, TimeSpan.Zero)));
        var connection = new ConnectorConnection(
            "codex-personal", ProviderKind.OpenAI, "Personal", DataSourceKind.LocalCli,
            null, new Dictionary<string, string> { ["executable"] = "codex.exe" });

        var result = await connector.FetchAsync(connection, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("codex.exe", client.LastExecutable);
        Assert.Equal(90m, result.Snapshot!.Metrics.Single(m => m.Id == "codex-session").Current);
    }

    [Fact]
    public async Task FetchAsync_MapsMissingCliToUnavailable()
    {
        var connector = new CodexPersonalConnector(
            new ThrowingCodexClient(new System.ComponentModel.Win32Exception(2)),
            TimeProvider.System);

        var result = await connector.FetchAsync(new ConnectorConnection(
            "codex-personal", ProviderKind.OpenAI, "Personal", DataSourceKind.LocalCli,
            null, null), CancellationToken.None);

        Assert.Equal(ConnectionHealth.Unavailable, result.UpstreamHealth);
        Assert.Contains("Codex CLI", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ConnectAndValidate_UsesLocalCliWithoutSecrets()
    {
        var client = new StubCodexClient(new CodexAppServerResult(
            """{"id":2,"result":{"rateLimits":{"primary":{"usedPercent":25,"resetsAt":1784822400}}}}""",
            """{"id":3,"result":{"dailyUsageBuckets":[]}}"""));
        var connector = new CodexPersonalConnector(client, TimeProvider.System);

        var connection = await connector.ConnectAsync(
            new ConnectionRequest("Personal", DataSourceKind.LocalCli));
        var validation = await connector.ValidateAsync(connection);

        Assert.Null(connection.SecretReference);
        Assert.Equal("codex.exe", connection.Settings!["executable"]);
        Assert.True(validation.IsValid);
        await connector.DisconnectAsync(connection);
    }

    [Fact]
    public async Task ValidateAsync_ReturnsMessageWhenCliCannotBeRead()
    {
        var connector = new CodexPersonalConnector(
            new ThrowingCodexClient(new InvalidOperationException()),
            TimeProvider.System);
        var connection = new ConnectorConnection(
            "codex", ProviderKind.OpenAI, "Personal", DataSourceKind.LocalCli, null, null);

        var result = await connector.ValidateAsync(connection);

        Assert.False(result.IsValid);
        Assert.Contains("Codex", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(typeof(OperationCanceledException))]
    [InlineData(typeof(InvalidOperationException))]
    [InlineData(typeof(ArgumentException))]
    public async Task FetchAsync_MapsExpectedLocalFailures(Type exceptionType)
    {
        var exception = (Exception)Activator.CreateInstance(exceptionType)!;
        var connector = new CodexPersonalConnector(new ThrowingCodexClient(exception), TimeProvider.System);

        var result = await connector.FetchAsync(new ConnectorConnection(
            "codex", ProviderKind.OpenAI, "Personal", DataSourceKind.LocalCli, null, null));

        Assert.Equal(ConnectionHealth.Unavailable, result.UpstreamHealth);
        Assert.Null(result.Snapshot);
    }

    [Fact]
    public async Task FetchAsync_PropagatesCallerCancellation()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var connector = new CodexPersonalConnector(
            new ThrowingCodexClient(new OperationCanceledException(cancellation.Token)),
            TimeProvider.System);

        await Assert.ThrowsAsync<OperationCanceledException>(() => connector.FetchAsync(
            new ConnectorConnection("codex", ProviderKind.OpenAI, "Personal", DataSourceKind.LocalCli, null, null),
            cancellation.Token));
    }

    [Fact]
    public async Task AppServerClient_RejectsUnsafeExecutableBeforeLaunching()
    {
        var client = new CodexAppServerClient();

        await Assert.ThrowsAsync<ArgumentException>(() =>
            client.ReadUsageAsync("not-codex.exe", CancellationToken.None));
        await Assert.ThrowsAsync<FileNotFoundException>(() =>
            client.ReadUsageAsync(
                Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "codex.exe"),
                CancellationToken.None));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(121)]
    public void AppServerClient_RejectsUnsafeTimeouts(int seconds)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new CodexAppServerClient(TimeSpan.FromSeconds(seconds)));
    }

    private sealed class StubCodexClient(CodexAppServerResult result) : ICodexAppServerClient
    {
        public string? LastExecutable { get; private set; }

        public Task<CodexAppServerResult> ReadUsageAsync(string executable, CancellationToken cancellationToken)
        {
            LastExecutable = executable;
            return Task.FromResult(result);
        }
    }

    private sealed class ThrowingCodexClient(Exception exception) : ICodexAppServerClient
    {
        public Task<CodexAppServerResult> ReadUsageAsync(string executable, CancellationToken cancellationToken) =>
            Task.FromException<CodexAppServerResult>(exception);
    }
}

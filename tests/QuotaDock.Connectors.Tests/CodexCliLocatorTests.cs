using QuotaDock.Connectors.OpenAI;

namespace QuotaDock.Connectors.Tests;

public sealed class CodexCliLocatorTests
{
    [Fact]
    public void DefaultConstructor_IsAvailableForTheNativeApp()
    {
        var locator = new CodexCliLocator();

        Assert.NotNull(locator);
    }

    [Fact]
    public async Task FindAsync_PrefersAConfiguredUsableExecutable()
    {
        var checkedPaths = new List<string>();
        var locator = new CodexCliLocator(
            path => path is "C:\\tools\\codex.exe" or "C:\\path\\codex.exe",
            (path, _) =>
            {
                checkedPaths.Add(path);
                return Task.FromResult(true);
            },
            name => name == "PATH" ? "C:\\path" : null);

        var found = await locator.FindAsync("C:\\tools\\codex.exe");

        Assert.Equal("C:\\tools\\codex.exe", found);
        Assert.Equal(["C:\\tools\\codex.exe"], checkedPaths);
    }

    [Fact]
    public async Task FindAsync_FallsBackToPathAndSkipsUnusableCandidates()
    {
        var locator = new CodexCliLocator(
            path => path.EndsWith("codex.exe", StringComparison.OrdinalIgnoreCase),
            (path, _) => Task.FromResult(path.StartsWith("C:\\second", StringComparison.Ordinal)),
            name => name == "PATH" ? "C:\\first;C:\\second" : null);

        var found = await locator.FindAsync();

        Assert.Equal("C:\\second\\codex.exe", found);
    }

    [Fact]
    public async Task FindAsync_ReturnsNullWhenNoLaunchableCliExists()
    {
        var locator = new CodexCliLocator(
            _ => false,
            (_, _) => throw new InvalidOperationException("Probe must not run"),
            _ => null);

        var found = await locator.FindAsync();

        Assert.Null(found);
    }

    [Fact]
    public async Task FindAsync_ChecksTheOfficialWindowsStandaloneInstallLocation()
    {
        const string expected = @"C:\Local\Programs\OpenAI\Codex\bin\codex.exe";
        var locator = new CodexCliLocator(
            path => string.Equals(path, expected, StringComparison.Ordinal),
            (path, _) => Task.FromResult(string.Equals(path, expected, StringComparison.Ordinal)),
            name => name == "LOCALAPPDATA" ? @"C:\Local" : null);

        var found = await locator.FindAsync();

        Assert.Equal(expected, found);
    }

    [Fact]
    public async Task FindAsync_ChecksTheCurrentNpmWindowsBinaryLayout()
    {
        const string expected =
            @"C:\Roaming\npm\node_modules\@openai\codex\node_modules\@openai\codex-win32-x64\vendor\x86_64-pc-windows-msvc\bin\codex.exe";
        var locator = new CodexCliLocator(
            path => string.Equals(path, expected, StringComparison.Ordinal),
            (path, _) => Task.FromResult(string.Equals(path, expected, StringComparison.Ordinal)),
            name => name == "APPDATA" ? @"C:\Roaming" : null);

        var found = await locator.FindAsync();

        Assert.Equal(expected, found);
    }

    [Fact]
    public async Task FindAsync_PropagatesCallerCancellation()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var locator = new CodexCliLocator(
            _ => true,
            (_, token) => Task.FromCanceled<bool>(token),
            name => name == "PATH" ? "C:\\path" : null);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            locator.FindAsync(cancellationToken: cancellation.Token));
    }
}

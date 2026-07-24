using QuotaDock.Connectors.Anthropic;

namespace QuotaDock.Connectors.Tests;

public sealed class ClaudeCliLocatorTests
{
    [Fact]
    public async Task FindAsync_PrefersAConfiguredUsableExecutable()
    {
        var probed = new List<string>();
        var locator = new ClaudeCliLocator(
            path => path is "C:\\tools\\claude.exe" or "C:\\path\\claude.cmd",
            (path, _) =>
            {
                probed.Add(path);
                return Task.FromResult(true);
            },
            name => name == "PATH" ? "C:\\path" : null);

        var found = await locator.FindAsync("C:\\tools\\claude.exe");

        Assert.Equal("C:\\tools\\claude.exe", found);
        Assert.Equal(["C:\\tools\\claude.exe"], probed);
    }

    [Fact]
    public async Task FindAsync_AcceptsTheNpmCmdShim()
    {
        var locator = new ClaudeCliLocator(
            path => path.EndsWith("claude.cmd", StringComparison.OrdinalIgnoreCase),
            (_, _) => Task.FromResult(true),
            name => name switch
            {
                "APPDATA" => "C:\\Users\\u\\AppData\\Roaming",
                _ => null
            });

        var found = await locator.FindAsync();

        Assert.Equal("C:\\Users\\u\\AppData\\Roaming\\npm\\claude.cmd", found);
    }

    [Fact]
    public async Task FindAsync_ChecksTheNativeInstallLocation()
    {
        var locator = new ClaudeCliLocator(
            path => path == "C:\\Users\\u\\.local\\bin\\claude.exe",
            (_, _) => Task.FromResult(true),
            name => name == "USERPROFILE" ? "C:\\Users\\u" : null);

        var found = await locator.FindAsync();

        Assert.Equal("C:\\Users\\u\\.local\\bin\\claude.exe", found);
    }

    [Fact]
    public async Task FindAsync_SkipsCandidatesThatFailTheProbe()
    {
        var locator = new ClaudeCliLocator(
            path => path.EndsWith("claude.exe", StringComparison.OrdinalIgnoreCase) ||
                    path.EndsWith("claude.cmd", StringComparison.OrdinalIgnoreCase),
            (path, _) => Task.FromResult(path.StartsWith("C:\\second", StringComparison.Ordinal)),
            name => name == "PATH" ? "C:\\first;C:\\second" : null);

        var found = await locator.FindAsync();

        Assert.Equal("C:\\second\\claude.exe", found);
    }

    [Fact]
    public async Task FindAsync_ReturnsNullWhenNothingIsLaunchable()
    {
        var locator = new ClaudeCliLocator(
            _ => false,
            (_, _) => throw new InvalidOperationException("Probe must not run"),
            _ => null);

        Assert.Null(await locator.FindAsync());
    }

    [Fact]
    public async Task FindAsync_IgnoresExecutablesWithOtherNames()
    {
        var locator = new ClaudeCliLocator(
            _ => true,
            (_, _) => throw new InvalidOperationException("Probe must not run"),
            _ => null);

        Assert.Null(await locator.FindAsync("C:\\evil\\not-claude.exe"));
    }
}

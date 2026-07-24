using QuotaDock.Connectors.Personal;

namespace QuotaDock.Connectors.Tests;

public sealed class CliProcessTests
{
    [Fact]
    public void CreateStartInfo_UsesTheExecutableDirectlyForExeFiles()
    {
        var info = CliProcess.CreateStartInfo("C:\\bin\\claude.exe", ["auth", "login", "--claudeai"]);

        Assert.Equal("C:\\bin\\claude.exe", info.FileName);
        Assert.Equal(["auth", "login", "--claudeai"], info.ArgumentList);
        Assert.False(info.UseShellExecute);
        Assert.True(info.CreateNoWindow);
    }

    [Fact]
    public void CreateStartInfo_WrapsCmdShimsInComSpec()
    {
        var info = CliProcess.CreateStartInfo("C:\\npm\\claude.cmd", ["auth", "login"]);

        Assert.EndsWith("cmd.exe", info.FileName, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("/d /s /c \"\"C:\\npm\\claude.cmd\" auth login\"", info.Arguments);
        Assert.False(info.UseShellExecute);
    }

    [Theory]
    [InlineData("auth login")]
    [InlineData("a&b")]
    [InlineData("a\"b")]
    [InlineData("a|b")]
    [InlineData("")]
    public void CreateStartInfo_RejectsUnsafeArguments(string argument)
    {
        Assert.Throws<ArgumentException>(() =>
            CliProcess.CreateStartInfo("C:\\npm\\claude.cmd", [argument]));
    }

    [Fact]
    public void CreateStartInfo_RejectsQuotedExecutablePaths()
    {
        Assert.Throws<ArgumentException>(() =>
            CliProcess.CreateStartInfo("C:\\evil\" & calc & \"\\claude.cmd", []));
    }
}

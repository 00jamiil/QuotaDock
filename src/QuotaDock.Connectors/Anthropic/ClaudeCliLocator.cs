using System.ComponentModel;
using System.Diagnostics;
using QuotaDock.Connectors.Personal;

namespace QuotaDock.Connectors.Anthropic;

/// <summary>
/// Finds a launchable Claude Code CLI on this machine. Mirrors
/// <see cref="OpenAI.CodexCliLocator"/>: candidates are checked in order and
/// each one must actually answer <c>--version</c> within a bounded probe
/// before it is returned. On Windows the npm install ships a <c>claude.cmd</c>
/// shim and the native installer ships <c>claude.exe</c>; both are accepted.
/// </summary>
public sealed class ClaudeCliLocator
{
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(10);
    private static readonly string[] AllowedFileNames = ["claude.exe", "claude.cmd"];
    private readonly Func<string, bool> fileExists;
    private readonly Func<string, CancellationToken, Task<bool>> probe;
    private readonly Func<string, string?> getEnvironmentVariable;

    public ClaudeCliLocator()
        : this(File.Exists, ProbeAsync, Environment.GetEnvironmentVariable)
    {
    }

    public ClaudeCliLocator(
        Func<string, bool> fileExists,
        Func<string, CancellationToken, Task<bool>> probe,
        Func<string, string?> getEnvironmentVariable)
    {
        this.fileExists = fileExists ?? throw new ArgumentNullException(nameof(fileExists));
        this.probe = probe ?? throw new ArgumentNullException(nameof(probe));
        this.getEnvironmentVariable = getEnvironmentVariable ??
                                      throw new ArgumentNullException(nameof(getEnvironmentVariable));
    }

    public async Task<string?> FindAsync(
        string? configuredPath = null,
        CancellationToken cancellationToken = default)
    {
        foreach (var candidate in Candidates(configuredPath).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!IsClaudeExecutable(candidate) || !fileExists(candidate))
            {
                continue;
            }

            if (await probe(candidate, cancellationToken).ConfigureAwait(false))
            {
                return candidate;
            }
        }

        return null;
    }

    private IEnumerable<string> Candidates(string? configuredPath)
    {
        if (!string.IsNullOrWhiteSpace(configuredPath))
        {
            yield return configuredPath.Trim();
        }

        var path = getEnvironmentVariable("PATH");
        if (!string.IsNullOrWhiteSpace(path))
        {
            foreach (var directory in path.Split(
                         Path.PathSeparator,
                         StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                foreach (var fileName in AllowedFileNames)
                {
                    yield return Path.Combine(directory, fileName);
                }
            }
        }

        // Native installer location.
        var userProfile = getEnvironmentVariable("USERPROFILE");
        if (!string.IsNullOrWhiteSpace(userProfile))
        {
            yield return Path.Combine(userProfile, ".local", "bin", "claude.exe");
        }

        // npm global shim location.
        var appData = getEnvironmentVariable("APPDATA");
        if (!string.IsNullOrWhiteSpace(appData))
        {
            yield return Path.Combine(appData, "npm", "claude.cmd");
        }
    }

    private static bool IsClaudeExecutable(string path) =>
        AllowedFileNames.Any(name =>
            string.Equals(Path.GetFileName(path), name, StringComparison.OrdinalIgnoreCase));

    private static async Task<bool> ProbeAsync(string executable, CancellationToken cancellationToken)
    {
        try
        {
            var startInfo = CliProcess.CreateStartInfo(executable, ["--version"]);
            startInfo.RedirectStandardOutput = true;
            startInfo.RedirectStandardError = true;
            using var process = Process.Start(startInfo);
            if (process is null)
            {
                return false;
            }

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(ProbeTimeout);
            try
            {
                await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                process.Kill(entireProcessTree: true);
                return false;
            }

            return process.ExitCode == 0;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is Win32Exception or IOException or InvalidOperationException or ArgumentException)
        {
            return false;
        }
    }
}

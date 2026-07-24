using System.ComponentModel;
using System.Diagnostics;

namespace QuotaDock.Connectors.OpenAI;

public sealed class CodexCliLocator
{
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(5);
    private readonly Func<string, bool> fileExists;
    private readonly Func<string, CancellationToken, Task<bool>> probe;
    private readonly Func<string, string?> getEnvironmentVariable;

    public CodexCliLocator()
        : this(File.Exists, ProbeAsync, Environment.GetEnvironmentVariable)
    {
    }

    public CodexCliLocator(
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
            if (!IsCodexExecutable(candidate) || !fileExists(candidate))
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
            foreach (var directory in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                yield return Path.Combine(directory, "codex.exe");
            }
        }

        var appData = getEnvironmentVariable("APPDATA");
        if (!string.IsNullOrWhiteSpace(appData))
        {
            yield return Path.Combine(
                appData,
                "npm",
                "node_modules",
                "@openai",
                "codex",
                "node_modules",
                "@openai",
                "codex-win32-x64",
                "vendor",
                "x86_64-pc-windows-msvc",
                "bin",
                "codex.exe");
        }

        var localAppData = getEnvironmentVariable("LOCALAPPDATA");
        if (!string.IsNullOrWhiteSpace(localAppData))
        {
            yield return Path.Combine(
                localAppData,
                "Programs",
                "OpenAI",
                "Codex",
                "bin",
                "codex.exe");
        }
    }

    private static bool IsCodexExecutable(string path) =>
        string.Equals(Path.GetFileName(path), "codex.exe", StringComparison.OrdinalIgnoreCase);

    private static async Task<bool> ProbeAsync(string executable, CancellationToken cancellationToken)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = executable,
                Arguments = "--version",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            });
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
        catch (Exception exception) when (exception is Win32Exception or IOException or InvalidOperationException)
        {
            return false;
        }
    }
}

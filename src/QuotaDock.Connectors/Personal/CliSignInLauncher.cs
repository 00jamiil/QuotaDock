using System.ComponentModel;
using System.Diagnostics;

namespace QuotaDock.Connectors.Personal;

public sealed record CliSignInResult(bool Succeeded, string Message);

public interface ICliSignInLauncher
{
    /// <summary>
    /// Runs a first-party CLI's own login command and waits for it to finish.
    /// The CLI opens the user's default browser itself (Codex and Claude Code
    /// both do); QuotaDock never sees the password or the OAuth code — it only
    /// observes the process exit code.
    /// </summary>
    Task<CliSignInResult> SignInAsync(
        string executable,
        IReadOnlyList<string> loginArguments,
        CancellationToken cancellationToken = default);
}

public sealed class CliSignInLauncher : ICliSignInLauncher
{
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromMinutes(5);
    private readonly TimeSpan timeout;

    public CliSignInLauncher(TimeSpan? timeout = null)
    {
        this.timeout = timeout ?? DefaultTimeout;
        if (this.timeout < TimeSpan.FromSeconds(10) || this.timeout > TimeSpan.FromMinutes(15))
        {
            throw new ArgumentOutOfRangeException(nameof(timeout));
        }
    }

    public async Task<CliSignInResult> SignInAsync(
        string executable,
        IReadOnlyList<string> loginArguments,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(loginArguments);

        ProcessStartInfo startInfo;
        try
        {
            startInfo = CliProcess.CreateStartInfo(executable, loginArguments);
        }
        catch (ArgumentException)
        {
            return new CliSignInResult(false, "The sign-in command could not be prepared safely.");
        }

        // The login runs headless from the app's perspective: no console window,
        // and stdin/stdout are captured so interactive fallbacks (like Claude's
        // "paste code" prompt) cannot crash on a missing console. The browser
        // window the CLI opens is the only user-visible surface.
        startInfo.RedirectStandardInput = true;
        startInfo.RedirectStandardOutput = true;
        startInfo.RedirectStandardError = true;

        using var process = new Process { StartInfo = startInfo };
        try
        {
            if (!process.Start())
            {
                return new CliSignInResult(false, "The sign-in helper could not be started.");
            }
        }
        catch (Exception exception) when (exception is Win32Exception or InvalidOperationException)
        {
            return new CliSignInResult(false, "The sign-in helper could not be started.");
        }

        // Drain output so the child never blocks on a full pipe. The content is
        // intentionally discarded: login output can echo OAuth URLs with state
        // values and must never reach logs or the UI.
        var drainOutput = process.StandardOutput.ReadToEndAsync(CancellationToken.None);
        var drainError = process.StandardError.ReadToEndAsync(CancellationToken.None);

        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(timeout);
        try
        {
            await process.WaitForExitAsync(timeoutSource.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch (Exception exception) when (exception is Win32Exception or InvalidOperationException)
            {
                // The process already exited between the timeout and the kill.
            }

            if (cancellationToken.IsCancellationRequested)
            {
                throw;
            }

            return new CliSignInResult(
                false,
                "Sign-in was not completed in the browser within 5 minutes. Try again when ready.");
        }
        finally
        {
            await Task.WhenAll(drainOutput, drainError).ConfigureAwait(false);
        }

        return process.ExitCode == 0
            ? new CliSignInResult(true, "Sign-in completed.")
            : new CliSignInResult(false, "Sign-in did not complete. Finish the login in the browser and try again.");
    }
}

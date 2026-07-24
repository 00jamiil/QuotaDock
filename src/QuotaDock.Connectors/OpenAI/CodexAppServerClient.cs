using System.Diagnostics;
using System.Text.Json;

namespace QuotaDock.Connectors.OpenAI;

public sealed record CodexAppServerResult(string RateLimitsPayload, string UsagePayload);

public interface ICodexAppServerClient
{
    Task<CodexAppServerResult> ReadUsageAsync(
        string executable,
        CancellationToken cancellationToken);
}

public sealed class CodexAppServerClient : ICodexAppServerClient
{
    private const int MaximumResponseCharacters = 2 * 1024 * 1024;
    private readonly TimeSpan timeout;

    public CodexAppServerClient(TimeSpan? timeout = null)
    {
        this.timeout = timeout ?? TimeSpan.FromSeconds(20);
        if (this.timeout < TimeSpan.FromSeconds(1) || this.timeout > TimeSpan.FromMinutes(2))
        {
            throw new ArgumentOutOfRangeException(nameof(timeout));
        }
    }

    public async Task<CodexAppServerResult> ReadUsageAsync(
        string executable,
        CancellationToken cancellationToken)
    {
        var safeExecutable = ValidateExecutable(executable);
        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(timeout);

        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = safeExecutable,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = false
            }
        };
        process.StartInfo.ArgumentList.Add("app-server");
        process.StartInfo.ArgumentList.Add("--stdio");
        process.StartInfo.Environment["NO_COLOR"] = "1";

        if (!process.Start())
        {
            throw new InvalidOperationException("Codex CLI could not be started.");
        }

        try
        {
            await WriteAsync(process, new
            {
                id = 1,
                method = "initialize",
                @params = new
                {
                    clientInfo = new { name = "quotadock", title = "QuotaDock", version = "0.1.0" }
                }
            }, timeoutSource.Token).ConfigureAwait(false);
            _ = await ReadResponseAsync(process, 1, timeoutSource.Token).ConfigureAwait(false);

            await WriteAsync(process, new { method = "initialized" }, timeoutSource.Token).ConfigureAwait(false);
            await WriteAsync(process, new { id = 2, method = "account/rateLimits/read", @params = (object?)null }, timeoutSource.Token).ConfigureAwait(false);
            var rates = await ReadResponseAsync(process, 2, timeoutSource.Token).ConfigureAwait(false);

            await WriteAsync(process, new { id = 3, method = "account/usage/read", @params = (object?)null }, timeoutSource.Token).ConfigureAwait(false);
            var usage = await ReadResponseAsync(process, 3, timeoutSource.Token).ConfigureAwait(false);

            return new CodexAppServerResult(rates, usage);
        }
        finally
        {
            try
            {
                process.StandardInput.Close();
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }
            }
            catch (InvalidOperationException)
            {
                // The process already exited.
            }
        }
    }

    private static async Task WriteAsync(
        Process process,
        object message,
        CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(message);
        await process.StandardInput.WriteLineAsync(json.AsMemory(), cancellationToken).ConfigureAwait(false);
        await process.StandardInput.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task<string> ReadResponseAsync(
        Process process,
        int expectedId,
        CancellationToken cancellationToken)
    {
        while (true)
        {
            var line = await process.StandardOutput.ReadLineAsync(cancellationToken).ConfigureAwait(false);
            if (line is null)
            {
                throw new InvalidOperationException("Codex CLI exited before returning account usage.");
            }

            if (line.Length > MaximumResponseCharacters)
            {
                throw new InvalidOperationException("Codex CLI returned an oversized response.");
            }

            try
            {
                using var document = JsonDocument.Parse(line);
                if (document.RootElement.TryGetProperty("id", out var id) &&
                    id.TryGetInt32(out var responseId) &&
                    responseId == expectedId)
                {
                    if (document.RootElement.TryGetProperty("error", out _))
                    {
                        throw new InvalidOperationException("Codex CLI rejected the account usage request.");
                    }

                    return line;
                }
            }
            catch (JsonException)
            {
                // Ignore non-protocol stdout emitted by an incompatible CLI build.
            }
        }
    }

    private static string ValidateExecutable(string executable)
    {
        var value = string.IsNullOrWhiteSpace(executable) ? "codex.exe" : executable.Trim();
        var fileName = Path.GetFileName(value);
        if (!string.Equals(fileName, "codex.exe", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Only the native codex.exe executable is allowed.", nameof(executable));
        }

        if (Path.IsPathFullyQualified(value) && !File.Exists(value))
        {
            throw new FileNotFoundException("The configured Codex CLI executable was not found.", value);
        }

        return value;
    }
}

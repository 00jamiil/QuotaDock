using System.Diagnostics;
using System.Text.RegularExpressions;

namespace QuotaDock.Connectors.Personal;

/// <summary>
/// Builds safe <see cref="ProcessStartInfo"/> values for launching local
/// first-party AI CLIs (Codex, Claude Code). Windows npm installs ship these
/// tools as <c>.cmd</c> shims, which .NET cannot start directly with
/// <c>UseShellExecute = false</c>, so shims are wrapped in <c>cmd.exe /c</c>.
/// Arguments are restricted to a conservative literal character set because
/// the cmd wrapper collapses them into a single command line.
/// </summary>
public static partial class CliProcess
{
    [GeneratedRegex("^[A-Za-z0-9._=-]+$")]
    private static partial Regex SafeArgument();

    public static ProcessStartInfo CreateStartInfo(string executable, IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        if (string.IsNullOrWhiteSpace(executable) || executable.Contains('"'))
        {
            throw new ArgumentException("The executable path is not launchable.", nameof(executable));
        }

        foreach (var argument in arguments)
        {
            if (string.IsNullOrWhiteSpace(argument) || !SafeArgument().IsMatch(argument))
            {
                throw new ArgumentException(
                    $"CLI argument '{argument}' contains characters outside the safe literal set.",
                    nameof(arguments));
            }
        }

        var extension = Path.GetExtension(executable);
        if (string.Equals(extension, ".cmd", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(extension, ".bat", StringComparison.OrdinalIgnoreCase))
        {
            var comSpec = Environment.GetEnvironmentVariable("ComSpec");
            var joined = arguments.Count == 0 ? string.Empty : " " + string.Join(' ', arguments);
            return new ProcessStartInfo
            {
                FileName = string.IsNullOrWhiteSpace(comSpec) ? "cmd.exe" : comSpec,
                Arguments = $"/d /s /c \"\"{executable}\"{joined}\"",
                UseShellExecute = false,
                CreateNoWindow = true
            };
        }

        var info = new ProcessStartInfo
        {
            FileName = executable,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (var argument in arguments)
        {
            info.ArgumentList.Add(argument);
        }

        return info;
    }
}

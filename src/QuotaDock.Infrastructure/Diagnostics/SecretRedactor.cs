using System.Text.RegularExpressions;

namespace QuotaDock.Infrastructure.Diagnostics;

public static partial class SecretRedactor
{
    public static string Redact(string message)
    {
        ArgumentNullException.ThrowIfNull(message);
        var redacted = AuthorizationHeader().Replace(message, "$1[REDACTED]");
        redacted = ApiKeyHeader().Replace(redacted, "$1[REDACTED]");
        redacted = CookieHeader().Replace(redacted, "$1[REDACTED]");
        redacted = ProviderKey().Replace(redacted, "[REDACTED]");
        return redacted;
    }

    [GeneratedRegex("(?i)(Authorization\\s*:\\s*Bearer\\s+)[^\\s,;]+", RegexOptions.CultureInvariant)]
    private static partial Regex AuthorizationHeader();

    [GeneratedRegex("(?i)(x-api-key\\s*:\\s*)[^\\s,;]+", RegexOptions.CultureInvariant)]
    private static partial Regex ApiKeyHeader();

    [GeneratedRegex("(?i)(Cookie\\s*:\\s*)[^\\r\\n]+", RegexOptions.CultureInvariant)]
    private static partial Regex CookieHeader();

    [GeneratedRegex("(?i)sk-(?:ant-|admin-|proj-|sp-)?[a-z0-9_-]{8,}", RegexOptions.CultureInvariant)]
    private static partial Regex ProviderKey();
}


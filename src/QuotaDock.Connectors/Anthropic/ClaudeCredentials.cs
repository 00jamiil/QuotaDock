using System.Text.Json;

namespace QuotaDock.Connectors.Anthropic;

/// <summary>
/// The read-only view of the local Claude Code OAuth credential that QuotaDock
/// needs to query the account usage window. The access token is held only in
/// memory for the duration of a single usage request and is never written to
/// SQLite, logs, or diagnostics.
/// </summary>
public sealed record ClaudeCredentials(
    string AccessToken,
    DateTimeOffset? ExpiresAt,
    IReadOnlyList<string> Scopes,
    string? SubscriptionType)
{
    public bool IsExpired(DateTimeOffset now) => ExpiresAt is { } expiry && expiry <= now;

    public bool HasProfileScope =>
        Scopes.Any(scope => string.Equals(scope, "user:profile", StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Redacts the access token from the record's string form. The compiler-
    /// generated <c>ToString()</c> would otherwise print every property,
    /// including the bearer token, which could leak into logs, debugger output,
    /// or exception context. This override guarantees the token never appears.
    /// </summary>
    public override string ToString() =>
        $"ClaudeCredentials {{ AccessToken = [REDACTED], ExpiresAt = {ExpiresAt:o}, " +
        $"SubscriptionType = {SubscriptionType} }}";
}

public interface IClaudeCredentialsReader
{
    /// <summary>Returns the local Claude Code credential, or null when it is absent or unreadable.</summary>
    ClaudeCredentials? Read();
}

/// <summary>
/// Reads the Claude Code OAuth credential from the user's local
/// <c>~/.claude/.credentials.json</c>. This is the same file the official
/// Claude Code CLI owns; QuotaDock only reads it, mirroring how the Codex
/// connector reads Codex's local usage output.
/// </summary>
public sealed class ClaudeLocalCredentialsReader : IClaudeCredentialsReader
{
    private readonly Func<string?> credentialsPathProvider;

    public ClaudeLocalCredentialsReader()
        : this(DefaultCredentialsPath)
    {
    }

    public ClaudeLocalCredentialsReader(Func<string?> credentialsPathProvider) =>
        this.credentialsPathProvider = credentialsPathProvider ??
            throw new ArgumentNullException(nameof(credentialsPathProvider));

    public static string? DefaultCredentialsPath()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (string.IsNullOrWhiteSpace(home))
        {
            return null;
        }

        return Path.Combine(home, ".claude", ".credentials.json");
    }

    public ClaudeCredentials? Read()
    {
        var path = credentialsPathProvider();
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return null;
        }

        try
        {
            var json = File.ReadAllText(path);
            return Parse(json);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    public static ClaudeCredentials? Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        if (!document.RootElement.TryGetProperty("claudeAiOauth", out var oauth) ||
            oauth.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        if (!oauth.TryGetProperty("accessToken", out var tokenElement) ||
            tokenElement.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        var token = tokenElement.GetString();
        if (string.IsNullOrWhiteSpace(token))
        {
            return null;
        }

        DateTimeOffset? expiresAt = null;
        if (oauth.TryGetProperty("expiresAt", out var expiryElement) &&
            expiryElement.ValueKind == JsonValueKind.Number &&
            expiryElement.TryGetInt64(out var expiryMs))
        {
            expiresAt = DateTimeOffset.FromUnixTimeMilliseconds(expiryMs);
        }

        var scopes = new List<string>();
        if (oauth.TryGetProperty("scopes", out var scopesElement) &&
            scopesElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var scope in scopesElement.EnumerateArray())
            {
                if (scope.ValueKind == JsonValueKind.String && scope.GetString() is { Length: > 0 } value)
                {
                    scopes.Add(value);
                }
            }
        }

        var subscription = oauth.TryGetProperty("subscriptionType", out var subElement) &&
                           subElement.ValueKind == JsonValueKind.String
            ? subElement.GetString()
            : null;

        return new ClaudeCredentials(token, expiresAt, scopes, subscription);
    }
}

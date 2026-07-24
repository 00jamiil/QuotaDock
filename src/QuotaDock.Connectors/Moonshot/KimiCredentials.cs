using System.Text.Json;

namespace QuotaDock.Connectors.Moonshot;

/// <summary>
/// The read-only view of the local Kimi Code OAuth credential that QuotaDock
/// needs to query account usage. The access token is held only in memory for
/// the duration of a single usage request and is never written to SQLite,
/// logs, or diagnostics.
/// </summary>
public sealed record KimiCredentials(
    string AccessToken,
    DateTimeOffset? ExpiresAt,
    IReadOnlyList<string> Scopes,
    string? PlanType)
{
    public bool IsExpired(DateTimeOffset now) => ExpiresAt is { } expiry && expiry <= now;

    /// <summary>
    /// Redacts the access token from the record's string form so it can never
    /// leak into logs, debugger output, or exception context.
    /// </summary>
    public override string ToString() =>
        $"KimiCredentials {{ AccessToken = [REDACTED], ExpiresAt = {ExpiresAt:o}, " +
        $"PlanType = {PlanType} }}";
}

public interface IKimiCredentialsReader
{
    /// <summary>Returns the local Kimi credential, or null when it is absent or unreadable.</summary>
    KimiCredentials? Read();
}

/// <summary>
/// Reads the Kimi Code OAuth credential from the user's local
/// <c>~/.kimi-code/credentials/</c> directory. Kimi Code stores one credential
/// file per profile; QuotaDock reads the most recently written one. This is the
/// same store the official Kimi CLI owns; QuotaDock only reads it, mirroring
/// how the Claude connector reads Claude Code's local credential. Anything
/// unrecognized yields null rather than a fabricated credential.
/// </summary>
public sealed class KimiLocalCredentialsReader : IKimiCredentialsReader
{
    private readonly Func<string?> credentialsDirectoryProvider;

    public KimiLocalCredentialsReader()
        : this(DefaultCredentialsDirectory)
    {
    }

    public KimiLocalCredentialsReader(Func<string?> credentialsDirectoryProvider) =>
        this.credentialsDirectoryProvider = credentialsDirectoryProvider ??
            throw new ArgumentNullException(nameof(credentialsDirectoryProvider));

    public static string? DefaultCredentialsDirectory()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (string.IsNullOrWhiteSpace(home))
        {
            return null;
        }

        return Path.Combine(home, ".kimi-code", "credentials");
    }

    public KimiCredentials? Read()
    {
        var directory = credentialsDirectoryProvider();
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
        {
            return null;
        }

        try
        {
            // Pick the most recently updated credential profile.
            var file = new DirectoryInfo(directory)
                .EnumerateFiles("*.json")
                .OrderByDescending(info => info.LastWriteTimeUtc)
                .FirstOrDefault();
            if (file is null)
            {
                return null;
            }

            return Parse(File.ReadAllText(file.FullName));
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    public static KimiCredentials? Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        // Kimi Code is a Claude Code fork, so its credential commonly nests the
        // OAuth block; accept both a nested block and top-level keys, failing
        // closed when neither yields a token.
        var source = root;
        if (root.TryGetProperty("kimiCodeOauth", out var nested) && nested.ValueKind == JsonValueKind.Object)
        {
            source = nested;
        }
        else if (root.TryGetProperty("claudeAiOauth", out var claudeNested) && claudeNested.ValueKind == JsonValueKind.Object)
        {
            source = claudeNested;
        }

        string? token = ReadString(source, "accessToken") ??
                        ReadString(source, "access_token") ??
                        ReadString(source, "token");
        if (string.IsNullOrWhiteSpace(token))
        {
            return null;
        }

        DateTimeOffset? expiresAt = null;
        if (source.TryGetProperty("expiresAt", out var expiryElement) &&
            expiryElement.ValueKind == JsonValueKind.Number &&
            expiryElement.TryGetInt64(out var expiryMs))
        {
            expiresAt = DateTimeOffset.FromUnixTimeMilliseconds(expiryMs);
        }

        var scopes = new List<string>();
        if (source.TryGetProperty("scopes", out var scopesElement) &&
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

        var plan = ReadString(source, "subscriptionType") ?? ReadString(source, "planType");

        return new KimiCredentials(token, expiresAt, scopes, plan);
    }

    private static string? ReadString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
}

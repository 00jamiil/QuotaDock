using System.Text.Json;

namespace QuotaDock.Connectors.Xai;

/// <summary>
/// The read-only view of the local Grok Build OAuth credential that QuotaDock
/// needs to query account usage. The access token is held only in memory for
/// the duration of a single usage request and is never written to SQLite,
/// logs, or diagnostics.
/// </summary>
public sealed record GrokCredentials(
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
        $"GrokCredentials {{ AccessToken = [REDACTED], ExpiresAt = {ExpiresAt:o}, " +
        $"PlanType = {PlanType} }}";
}

public interface IGrokCredentialsReader
{
    /// <summary>Returns the local Grok credential, or null when it is absent or unreadable.</summary>
    GrokCredentials? Read();
}

/// <summary>
/// Reads the Grok Build OAuth credential from the user's local
/// <c>~/.grok/</c> directory. This is the same store the official Grok CLI
/// owns; QuotaDock only reads it, mirroring how the Claude connector reads
/// Claude Code's local credential. The exact on-disk shape is treated as
/// opaque and parsed defensively: anything unrecognized yields null rather
/// than a fabricated credential.
/// </summary>
public sealed class GrokLocalCredentialsReader : IGrokCredentialsReader
{
    private readonly Func<string?> credentialsPathProvider;

    public GrokLocalCredentialsReader()
        : this(DefaultCredentialsPath)
    {
    }

    public GrokLocalCredentialsReader(Func<string?> credentialsPathProvider) =>
        this.credentialsPathProvider = credentialsPathProvider ??
            throw new ArgumentNullException(nameof(credentialsPathProvider));

    public static string? DefaultCredentialsPath()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (string.IsNullOrWhiteSpace(home))
        {
            return null;
        }

        return Path.Combine(home, ".grok", "credentials.json");
    }

    public GrokCredentials? Read()
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

    public static GrokCredentials? Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        // Accept the token under a couple of conventional keys; the Grok CLI's
        // exact schema is not publicly documented, so we parse defensively and
        // fail closed rather than guessing.
        string? token = ReadString(root, "accessToken") ??
                        ReadString(root, "access_token") ??
                        ReadString(root, "token");
        if (string.IsNullOrWhiteSpace(token))
        {
            return null;
        }

        DateTimeOffset? expiresAt = null;
        if (root.TryGetProperty("expiresAt", out var expiryElement) &&
            expiryElement.ValueKind == JsonValueKind.Number &&
            expiryElement.TryGetInt64(out var expiryMs))
        {
            expiresAt = DateTimeOffset.FromUnixTimeMilliseconds(expiryMs);
        }

        var scopes = new List<string>();
        if (root.TryGetProperty("scopes", out var scopesElement) &&
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

        var plan = ReadString(root, "planType") ?? ReadString(root, "plan");

        return new GrokCredentials(token, expiresAt, scopes, plan);
    }

    private static string? ReadString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
}

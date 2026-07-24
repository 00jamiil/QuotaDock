namespace QuotaDock.Core.Domain;

public sealed record ConnectorConnection(
    string Id,
    ProviderKind Provider,
    string AccountLabel,
    DataSourceKind Source,
    string? SecretReference,
    IReadOnlyDictionary<string, string>? Settings);

public sealed class ConnectionRequest
{
    public string AccountLabel { get; }
    public DataSourceKind Source { get; }
    public string? Secret { get; }
    public IReadOnlyDictionary<string, string> Settings { get; }

    public ConnectionRequest(
        string accountLabel,
        DataSourceKind source,
        string? secret = null,
        IReadOnlyDictionary<string, string>? settings = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(accountLabel);
        AccountLabel = accountLabel.Trim();
        Source = source;
        Secret = string.IsNullOrWhiteSpace(secret) ? null : secret.Trim();
        Settings = settings ?? new Dictionary<string, string>(StringComparer.Ordinal);
    }

    public override string ToString() =>
        $"ConnectionRequest {{ AccountLabel = {AccountLabel}, Source = {Source}, Secret = [REDACTED] }}";
}

public sealed record ConnectorDefinition(
    string Id,
    ProviderKind Provider,
    string DisplayName,
    DataSourceKind Source,
    ConnectorCapabilities Capabilities,
    bool RequiresSecret);

public sealed record ConnectionValidationResult(bool IsValid, string? Message)
{
    public static ConnectionValidationResult Valid() => new(true, null);
    public static ConnectionValidationResult Invalid(string message) => new(false, message);
}


namespace QuotaDock.Core.Domain;

public sealed record UsageSnapshot(
    string ConnectionId,
    ProviderKind Provider,
    string AccountLabel,
    DataSourceKind Source,
    DateTimeOffset CapturedAt,
    ConnectionHealth Health,
    IReadOnlyList<UsageMetric> Metrics,
    string? StatusMessage)
{
    public bool HasMetrics => Metrics.Count > 0;
}

public sealed record ConnectorFetchResult(
    UsageSnapshot? Snapshot,
    ConnectionHealth UpstreamHealth,
    string? Message,
    TimeSpan? RetryAfter)
{
    public bool IsSuccess => Snapshot is not null && UpstreamHealth == ConnectionHealth.Fresh;

    public static ConnectorFetchResult Success(UsageSnapshot snapshot) =>
        new(snapshot, ConnectionHealth.Fresh, null, null);

    public static ConnectorFetchResult Failure(
        ConnectionHealth health,
        string message,
        TimeSpan? retryAfter = null)
    {
        if (health is ConnectionHealth.Fresh or ConnectionHealth.Stale)
        {
            throw new ArgumentOutOfRangeException(nameof(health), "Failure health must describe an upstream failure.");
        }

        return new ConnectorFetchResult(null, health, message, retryAfter);
    }
}


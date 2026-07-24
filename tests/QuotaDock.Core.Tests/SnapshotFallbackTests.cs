using QuotaDock.Core.Domain;
using QuotaDock.Core.Refresh;

namespace QuotaDock.Core.Tests;

public sealed class SnapshotFallbackTests
{
    [Fact]
    public void PreserveLastGood_KeepsMetricsAndMarksSnapshotStale()
    {
        var capturedAt = new DateTimeOffset(2026, 7, 23, 12, 0, 0, TimeSpan.Zero);
        var previous = new UsageSnapshot(
            "openai-personal",
            ProviderKind.OpenAI,
            "Personal",
            DataSourceKind.LocalCli,
            capturedAt,
            ConnectionHealth.Fresh,
            [UsageMetric.Create("session", "Session", MetricKind.QuotaPercentage,
                MetricDirection.Remaining, 68m, 100m, "%", MetricScope.Session, null)],
            null);

        var failed = ConnectorFetchResult.Failure(
            ConnectionHealth.RateLimited,
            "OpenAI asked QuotaDock to slow down.",
            TimeSpan.FromMinutes(2));

        var merged = SnapshotFallback.PreserveLastGood(previous, failed);

        Assert.Equal(ConnectionHealth.Stale, merged.Snapshot!.Health);
        Assert.Equal(previous.Metrics, merged.Snapshot.Metrics);
        Assert.Equal(capturedAt, merged.Snapshot.CapturedAt);
        Assert.Equal(ConnectionHealth.RateLimited, merged.UpstreamHealth);
        Assert.Contains("slow down", merged.Snapshot.StatusMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void PreserveLastGood_DoesNotInventMetricsWithoutHistory()
    {
        var failed = ConnectorFetchResult.Failure(
            ConnectionHealth.Unavailable,
            "Provider unavailable.");

        var merged = SnapshotFallback.PreserveLastGood(null, failed);

        Assert.Null(merged.Snapshot);
        Assert.Equal(ConnectionHealth.Unavailable, merged.UpstreamHealth);
    }
}


namespace QuotaDock.Core.Domain;

public enum ProviderStatusLevel
{
    Unknown,
    Operational,
    Degraded,
    Outage
}

/// <summary>
/// Advisory provider health, kept separate from usage so an incident is never
/// confused with "you are out of quota." Status never overwrites last-good usage.
/// </summary>
public sealed record ProviderStatusReport(
    ProviderKind Provider,
    ProviderStatusLevel Level,
    string? Message,
    DateTimeOffset ObservedAt)
{
    public bool IsIncident => Level is ProviderStatusLevel.Degraded or ProviderStatusLevel.Outage;

    public static ProviderStatusReport Unknown(ProviderKind provider, DateTimeOffset observedAt) =>
        new(provider, ProviderStatusLevel.Unknown, null, observedAt);
}

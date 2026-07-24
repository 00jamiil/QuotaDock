using QuotaDock.Core.Domain;
using QuotaDock.Core.Presentation;

namespace QuotaDock.Core.Refresh;

/// <summary>
/// Inputs that influence how soon a connection should refresh next.
/// </summary>
public sealed record AdaptiveRefreshContext(
    DataSourceKind Source,
    RefreshMode Mode,
    int ConsecutiveFailures,
    TimeSpan? RetryAfter,
    TimeSpan? SoonestReset,
    PaceStatus WorstPace,
    bool AgentActive);

/// <summary>
/// Resolves the next refresh interval for a connection. Fixed modes are honored
/// verbatim; Adaptive tightens near a reset, when pace is over, or when a local
/// agent is known to be active, and eases off otherwise. Transient failures and
/// Retry-After always take precedence so the app is a good API citizen.
/// </summary>
public static class AdaptiveRefreshPlanner
{
    public static readonly TimeSpan AdaptiveFloor = TimeSpan.FromMinutes(1);
    public static readonly TimeSpan AdaptiveIdleCeiling = TimeSpan.FromMinutes(30);

    public static TimeSpan NextInterval(AdaptiveRefreshContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        // Backoff and Retry-After win over any cadence choice.
        if (context.ConsecutiveFailures > 0)
        {
            var backoff = RefreshPolicy.BackoffFor(context.ConsecutiveFailures);
            return Max(backoff, context.RetryAfter);
        }

        if (context.RetryAfter is { } retryAfter && retryAfter > TimeSpan.Zero)
        {
            return retryAfter;
        }

        return context.Mode switch
        {
            RefreshMode.Manual => TimeSpan.MaxValue,
            RefreshMode.Fixed1m => TimeSpan.FromMinutes(1),
            RefreshMode.Fixed2m => TimeSpan.FromMinutes(2),
            RefreshMode.Fixed5m => TimeSpan.FromMinutes(5),
            RefreshMode.Fixed15m => TimeSpan.FromMinutes(15),
            RefreshMode.Fixed30m => TimeSpan.FromMinutes(30),
            RefreshMode.Adaptive => Adaptive(context),
            _ => RefreshPolicy.IntervalFor(context.Source)
        };
    }

    private static TimeSpan Adaptive(AdaptiveRefreshContext context)
    {
        var baseInterval = RefreshPolicy.IntervalFor(context.Source);

        // Tighten when something needs attention.
        if (context.WorstPace == PaceStatus.Exceeds || context.AgentActive)
        {
            baseInterval = AdaptiveFloor;
        }
        else if (context.WorstPace == PaceStatus.Watch)
        {
            baseInterval = Min(baseInterval, TimeSpan.FromMinutes(2));
        }

        // Never poll slower than the moment a window is about to reset.
        if (context.SoonestReset is { } reset && reset > TimeSpan.Zero)
        {
            if (reset <= TimeSpan.FromMinutes(2))
            {
                baseInterval = Min(baseInterval, AdaptiveFloor);
            }
            else if (reset <= TimeSpan.FromMinutes(15))
            {
                baseInterval = Min(baseInterval, TimeSpan.FromMinutes(2));
            }
        }

        if (baseInterval < AdaptiveFloor)
        {
            baseInterval = AdaptiveFloor;
        }

        if (baseInterval > AdaptiveIdleCeiling)
        {
            baseInterval = AdaptiveIdleCeiling;
        }

        return baseInterval;
    }

    private static TimeSpan Min(TimeSpan a, TimeSpan b) => a <= b ? a : b;

    private static TimeSpan Max(TimeSpan a, TimeSpan? b) =>
        b is { } value && value > a ? value : a;
}

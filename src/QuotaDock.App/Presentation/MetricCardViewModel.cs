using System.Globalization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using QuotaDock.Core.Configuration;
using QuotaDock.Core.Domain;
using QuotaDock.Core.Presentation;

namespace QuotaDock.App.Presentation;

public sealed record MetricCardViewModel(
    string Key,
    string Provider,
    string Account,
    string Label,
    string Value,
    string Detail,
    string Reset,
    string Freshness,
    double Progress,
    bool HasProgress,
    bool IsStale,
    string PaceText,
    string PaceColorKey)
{
    public double ProgressPercent => Progress * 100d;
    public double ProgressOpacity => HasProgress ? 1d : 0d;
    public bool HasPace => PaceText.Length > 0;
    public Visibility PaceVisibility => HasPace ? Visibility.Visible : Visibility.Collapsed;
    public Brush PaceBrush => (Brush)Application.Current.Resources[PaceColorKey];
    public string AutomationName =>
        $"{Provider} {Account}, {Label}, {Value}, {Detail}, {Reset}, updated {Freshness}" +
        (HasPace ? $", {PaceText}" : string.Empty);

    public static MetricCardViewModel Create(
        UsageSnapshot snapshot,
        UsageMetric metric,
        AppSettings settings,
        DateTimeOffset now)
    {
        var key = $"{snapshot.ConnectionId}:{metric.Id}";
        var limit = metric.Limit;
        var isSoftBudget = false;
        if (limit is null && settings.SoftBudgets.TryGetValue(key, out var budget) && budget > 0m)
        {
            limit = budget;
            isSoftBudget = true;
        }

        var progress = limit is > 0m
            ? (double)decimal.Clamp(metric.Current / limit.Value, 0m, 1m)
            : 0d;
        var direction = metric.Direction == MetricDirection.Used ? "used" : "remaining";
        var value = FormatValue(metric.Current, metric.Unit);
        var detail = limit is > 0m
            ? $"{direction} · {FormatValue(limit.Value, metric.Unit)} {(isSoftBudget ? "soft budget" : "limit")}"
            : $"{direction} · no provider limit";

        var (paceText, paceColorKey) = DescribePace(
            UsagePace.Calculate(metric, snapshot.CapturedAt, now).Status);

        return new MetricCardViewModel(
            key,
            snapshot.Provider switch
            {
                ProviderKind.OpenAI => "OPENAI",
                ProviderKind.Anthropic => "ANTHROPIC",
                ProviderKind.Alibaba => "ALIBABA",
                _ => snapshot.Provider.ToString().ToUpperInvariant()
            },
            snapshot.AccountLabel,
            metric.Label,
            value,
            detail,
            metric.ResetsAt is { } reset ? ResetCountdown.Format(reset, now) : "No reset reported",
            FormatFreshness(snapshot.CapturedAt, now),
            progress,
            limit is > 0m,
            snapshot.Health == ConnectionHealth.Stale,
            paceText,
            paceColorKey);
    }

    private static (string Text, string ColorKey) DescribePace(PaceStatus status) => status switch
    {
        PaceStatus.OnTrack => ("On track", "QuotaDockAccentBrush"),
        PaceStatus.Watch => ("Watch pace", "QuotaDockWarningBrush"),
        PaceStatus.Exceeds => ("Over pace", "QuotaDockDangerBrush"),
        _ => (string.Empty, "QuotaDockMutedBrush")
    };

    private static string FormatValue(decimal value, string unit)
    {
        var formatted = value switch
        {
            >= 1_000_000m => $"{value / 1_000_000m:0.##}M",
            >= 1_000m => $"{value / 1_000m:0.##}K",
            _ => value.ToString("0.##", CultureInfo.CurrentCulture)
        };
        return string.Equals(unit, "USD", StringComparison.OrdinalIgnoreCase)
            ? $"${formatted} USD"
            : $"{formatted} {unit}";
    }

    private static string FormatFreshness(DateTimeOffset capturedAt, DateTimeOffset now)
    {
        var age = now - capturedAt;
        if (age < TimeSpan.FromMinutes(1))
        {
            return "just now";
        }

        return age < TimeSpan.FromHours(1)
            ? $"{Math.Floor(age.TotalMinutes)}m ago"
            : age < TimeSpan.FromDays(1)
                ? $"{Math.Floor(age.TotalHours)}h ago"
                : $"{Math.Floor(age.TotalDays)}d ago";
    }
}

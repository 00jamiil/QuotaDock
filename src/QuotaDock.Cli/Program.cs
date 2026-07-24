using System.Globalization;
using System.Text.Json;
using QuotaDock.Core.Configuration;
using QuotaDock.Core.Domain;
using QuotaDock.Core.Presentation;
using QuotaDock.Infrastructure.Persistence;

namespace QuotaDock.Cli;

// quotadock — a small read-only console over the same local QuotaDock stores the
// app uses. It renders usage, pace, and local spend as text or JSON so scripts,
// CI, and third-party panels can reuse QuotaDock without the UI. It never writes
// usage data and never contacts providers directly; it reads the local database.
internal static class Program
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private static async Task<int> Main(string[] args)
    {
        try
        {
            var command = args.Length > 0 ? args[0].ToLowerInvariant() : "usage";
            var asJson = args.Any(a => a is "--json" or "-j");

            return command switch
            {
                "usage" => await RunUsageAsync(asJson).ConfigureAwait(false),
                "pace" => await RunPaceAsync(asJson).ConfigureAwait(false),
                "cost" => await RunCostAsync(asJson).ConfigureAwait(false),
                "help" or "--help" or "-h" => PrintHelp(),
                _ => Unknown(command)
            };
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"quotadock: {exception.Message}");
            return 1;
        }
    }

    private static string DatabasePath() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "QuotaDock",
        "quotadock.db");

    private static async Task<IReadOnlyList<UsageSnapshot>> LoadSnapshotsAsync()
    {
        var path = DatabasePath();
        if (!File.Exists(path))
        {
            return [];
        }

        await using var store = new SqliteSnapshotStore(path);
        await store.InitializeAsync().ConfigureAwait(false);
        return await store.LoadLatestForAllAsync().ConfigureAwait(false);
    }

    private static async Task<int> RunUsageAsync(bool asJson)
    {
        var snapshots = await LoadSnapshotsAsync().ConfigureAwait(false);
        var now = DateTimeOffset.Now;

        if (asJson)
        {
            var payload = snapshots.SelectMany(snapshot => snapshot.Metrics.Select(metric => new
            {
                provider = snapshot.Provider.ToString(),
                account = snapshot.AccountLabel,
                metric = metric.Label,
                unit = metric.Unit,
                current = metric.Current,
                limit = metric.Limit,
                direction = metric.Direction.ToString(),
                health = snapshot.Health.ToString(),
                resetsAt = metric.ResetsAt
            }));
            Console.WriteLine(JsonSerializer.Serialize(payload, JsonOptions));
            return 0;
        }

        if (snapshots.Count == 0)
        {
            Console.WriteLine("No usage recorded yet. Connect a provider in QuotaDock first.");
            return 0;
        }

        foreach (var snapshot in snapshots.OrderBy(s => s.Provider).ThenBy(s => s.AccountLabel))
        {
            Console.WriteLine($"{snapshot.Provider} · {snapshot.AccountLabel} [{snapshot.Health}]");
            foreach (var metric in snapshot.Metrics)
            {
                var direction = metric.Direction == MetricDirection.Used ? "used" : "remaining";
                var limit = metric.Limit is { } l ? $" / {l:0.##}" : string.Empty;
                var reset = metric.ResetsAt is { } r ? $"  ({ResetCountdown.Format(r, now)})" : string.Empty;
                Console.WriteLine(
                    $"  {metric.Label}: {metric.Current:0.##}{limit} {metric.Unit} {direction}{reset}");
            }
        }

        return 0;
    }

    private static async Task<int> RunPaceAsync(bool asJson)
    {
        var snapshots = await LoadSnapshotsAsync().ConfigureAwait(false);
        var now = DateTimeOffset.Now;

        var rows = snapshots.SelectMany(snapshot => snapshot.Metrics.Select(metric =>
        {
            var pace = UsagePace.Calculate(metric, snapshot.CapturedAt, now);
            return new
            {
                snapshot.Provider,
                snapshot.AccountLabel,
                metric.Label,
                pace.Status,
                pace.UsedPerHour,
                pace.ProjectedAtReset,
                metric.Limit
            };
        })).ToList();

        if (asJson)
        {
            Console.WriteLine(JsonSerializer.Serialize(rows.Select(r => new
            {
                provider = r.Provider.ToString(),
                account = r.AccountLabel,
                metric = r.Label,
                status = r.Status.ToString(),
                usedPerHour = r.UsedPerHour,
                projectedAtReset = r.ProjectedAtReset,
                limit = r.Limit
            }), JsonOptions));
            return 0;
        }

        var withPace = rows.Where(r => r.Status != PaceStatus.Unknown).ToList();
        if (withPace.Count == 0)
        {
            Console.WriteLine("No metrics with enough window data to project pace.");
            return 0;
        }

        foreach (var row in withPace)
        {
            Console.WriteLine(
                $"{row.Provider} · {row.Label}: {UsagePace.Describe(row.Status)} " +
                $"(~{row.UsedPerHour:0.##}/h, projected {row.ProjectedAtReset:0.##}/{row.Limit:0.##})");
        }

        return 0;
    }

    private static async Task<int> RunCostAsync(bool asJson)
    {
        var snapshots = await LoadSnapshotsAsync().ConfigureAwait(false);
        var summary = SpendEstimator.Summarize(snapshots, DateTimeOffset.Now);

        if (asJson)
        {
            Console.WriteLine(JsonSerializer.Serialize(new
            {
                last7Days = summary.LastSevenDays.Select(t => new { t.Currency, t.Amount }),
                last30Days = summary.LastThirtyDays.Select(t => new { t.Currency, t.Amount })
            }, JsonOptions));
            return 0;
        }

        if (!summary.HasData)
        {
            Console.WriteLine("No local cost history. Only providers that report spend contribute here.");
            return 0;
        }

        Console.WriteLine("Last 7 days:");
        PrintTotals(summary.LastSevenDays);
        Console.WriteLine("Last 30 days:");
        PrintTotals(summary.LastThirtyDays);
        return 0;
    }

    private static void PrintTotals(IReadOnlyList<SpendTotal> totals)
    {
        if (totals.Count == 0)
        {
            Console.WriteLine("  (none)");
            return;
        }

        foreach (var total in totals)
        {
            Console.WriteLine($"  {total.Amount.ToString("0.##", CultureInfo.InvariantCulture)} {total.Currency}");
        }
    }

    private static int PrintHelp()
    {
        Console.WriteLine(
            """
            quotadock — local AI usage from your QuotaDock database.

            Usage:
              quotadock usage [--json]   Current usage per provider and metric (default).
              quotadock pace  [--json]   Burn-rate projection toward each reset.
              quotadock cost  [--json]   Local 7/30-day spend, grouped by currency.
              quotadock help             Show this help.

            Reads the same local database as the app; it does not contact providers.
            """);
        return 0;
    }

    private static int Unknown(string command)
    {
        Console.Error.WriteLine($"quotadock: unknown command '{command}'. Try 'quotadock help'.");
        return 2;
    }
}

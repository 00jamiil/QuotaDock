namespace QuotaDock.Core.Presentation;

public static class ResetCountdown
{
    public static string Format(DateTimeOffset resetsAt, DateTimeOffset now)
    {
        var remaining = resetsAt - now;
        if (remaining <= TimeSpan.Zero)
        {
            return "Reset due";
        }

        if (remaining.TotalDays >= 1)
        {
            return $"Resets in {(int)remaining.TotalDays}d {remaining.Hours}h";
        }

        if (remaining.TotalHours >= 1)
        {
            return $"Resets in {(int)remaining.TotalHours}h {remaining.Minutes}m";
        }

        return $"Resets in {Math.Max(1, remaining.Minutes)}m";
    }
}

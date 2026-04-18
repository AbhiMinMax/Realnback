namespace MemoryTrainer.Helpers;

public static class TimeFormatter
{
    public static string FormatDuration(TimeSpan duration)
    {
        if (duration.TotalDays >= 1)
        {
            int days = (int)duration.TotalDays;
            int hours = duration.Hours;
            int minutes = duration.Minutes;
            var parts = new List<string>();
            if (days > 0) parts.Add($"{days} day{(days == 1 ? "" : "s")}");
            if (hours > 0) parts.Add($"{hours} hour{(hours == 1 ? "" : "s")}");
            if (minutes > 0) parts.Add($"{minutes} minute{(minutes == 1 ? "" : "s")}");
            return string.Join(", ", parts);
        }
        if (duration.TotalHours >= 1)
        {
            int hours = (int)duration.TotalHours;
            int minutes = duration.Minutes;
            if (minutes > 0)
                return $"{hours} hour{(hours == 1 ? "" : "s")} {minutes} minute{(minutes == 1 ? "" : "s")}";
            return $"{hours} hour{(hours == 1 ? "" : "s")}";
        }
        int mins = (int)duration.TotalMinutes;
        return $"{mins} minute{(mins == 1 ? "" : "s")}";
    }

    public static string FormatCountdown(TimeSpan remaining)
    {
        if (remaining <= TimeSpan.Zero) return "0s";
        if (remaining.TotalHours >= 1)
            return $"{(int)remaining.TotalHours}h {remaining.Minutes:D2}m {remaining.Seconds:D2}s";
        if (remaining.TotalMinutes >= 1)
            return $"{remaining.Minutes}m {remaining.Seconds:D2}s";
        return $"{remaining.Seconds}s";
    }
}

using System.Globalization;
using System.Text;
using CodexUsageMonitor.Core.Configuration;
using CodexUsageMonitor.Core.Models;
using CodexUsageMonitor.Core.Notifications;

namespace CodexUsageMonitor.Core.Presentation;

/// <summary>Formats usage records for tray tooltips, notifications, and the details window.</summary>
public static class UsageTextFormatter
{
    // NotifyIcon.Text has room for 127 UTF-16 code units on modern Windows.
    /// <summary>The maximum tooltip length in UTF-16 code units.</summary>
    public const int TooltipMaxLength = 127;
    /// <summary>The maximum notification body length in UTF-16 code units.</summary>
    public const int NotificationMaxLength = 255;

    /// <summary>Combines threshold crossings into a notification body with a details prompt.</summary>
    /// <param name="alerts">The window alerts to display together.</param>
    /// <returns>Notification text limited to <see cref="NotificationMaxLength"/> code units.</returns>
    public static string ThresholdNotification(IEnumerable<UsageThresholdAlert> alerts)
    {
        var lines = alerts.Select(alert =>
        {
            var bucket = Limit(SingleLine(alert.BucketName), 32);
            var window = Duration(alert.Window.WindowDurationMins, "duration unknown");
            var reached = string.Join(", ", alert.ThresholdsPercent.Select(threshold => $"{threshold}%"));
            return $"{bucket} {alert.WindowName} ({window}): {Percent(alert.Window.UsedPercent)}% used; reached {reached}.";
        });
        const string suffix = "\nClick for usage details.";
        return Limit(string.Join('\n', lines), NotificationMaxLength - suffix.Length) + suffix;
    }

    /// <summary>Formats a compact usage summary, including loading, unavailable, or stale status.</summary>
    /// <param name="snapshot">The latest successful reading, if available.</param>
    /// <param name="error">The latest retrieval error, or <see langword="null"/> after a successful retrieval.</param>
    /// <param name="timeZone">The display time zone, or <see langword="null"/> for the local time zone.</param>
    /// <param name="now">The reference instant for reset countdowns, or <see langword="null"/> for the current UTC time.</param>
    /// <returns>Tooltip text limited to <see cref="TooltipMaxLength"/> code units.</returns>
    public static string Tooltip(UsageSnapshot? snapshot, string? error = null, TimeZoneInfo? timeZone = null,
        DateTimeOffset? now = null)
    {
        if (snapshot is null)
        {
            return error is null ? "Codex Usage Monitor\nChecking usage..."
                : "Codex Usage Monitor\nUsage unavailable\nRight-click for details";
        }

        var currentTime = now ?? DateTimeOffset.UtcNow;
        var buckets = snapshot.Usage.GetBuckets().ToArray();
        var bucket = buckets[0].Value;
        var statusTitle = error is not null ? "Codex | STALE"
            : IsLimited(snapshot.Usage) ? "Codex | limit reached" : "Codex";
        var title = statusTitle;
        if (error is null && !string.IsNullOrEmpty(bucket.PlanType))
        {
            title += " | " + Limit(SingleLine(bucket.PlanType), 16);
        }
        var lines = new List<string> { title };
        AddWindow(bucket.Primary, "Primary");
        AddWindow(bucket.Secondary, "Secondary");
        if (bucket.Primary is null && bucket.Secondary is null)
        {
            lines.Add("Usage windows unavailable");
            if (bucket.Credits is { } credits)
            {
                lines.Add("Credits: " + Limit(CreditBalance(credits), 24));
            }
        }
        var updated = LocalTime(snapshot.RetrievedAt, timeZone).ToString("HH:mm", CultureInfo.InvariantCulture);
        lines.Add(buckets.Length > 1 ? $"+{buckets.Length - 1} buckets | {updated}" : $"Updated {updated}");
        var text = string.Join('\n', lines);
        if (text.Length > TooltipMaxLength)
        {
            // Preserve both windows and their countdowns before optional plan and update metadata.
            lines[0] = statusTitle;
            text = string.Join('\n', lines);
        }
        if (text.Length > TooltipMaxLength)
        {
            lines.RemoveAt(lines.Count - 1);
            text = string.Join('\n', lines);
        }
        return Limit(text, TooltipMaxLength);

        void AddWindow(RateLimitWindow? window, string fallback)
        {
            if (window is null) { return; }
            var duration = Limit(Duration(window.WindowDurationMins, fallback), 10);
            var percent = Limit(Percent(window.UsedPercent), 8);
            lines.Add($"{duration}: {percent}% used | reset {ResetTime(window.ResetsAt, timeZone, "MM/dd HH:mm", currentTime)}");
        }
    }

    /// <summary>Formats all available usage buckets and account limits for the details window.</summary>
    /// <param name="snapshot">The latest successful reading, if available.</param>
    /// <param name="error">The latest retrieval error, used to mark cached data as stale and explain retries.</param>
    /// <param name="timeZone">The display time zone, or <see langword="null"/> for the local time zone.</param>
    /// <param name="refreshIntervalSeconds">The polling interval in seconds to display in retry guidance.</param>
    /// <param name="now">The reference instant for reset countdowns, or <see langword="null"/> for the current UTC time.</param>
    /// <returns>Multiline usage details and any retrieval error guidance.</returns>
    public static string Details(UsageSnapshot? snapshot, string? error = null, TimeZoneInfo? timeZone = null,
        int refreshIntervalSeconds = MonitorSettings.DefaultRefreshIntervalSeconds, DateTimeOffset? now = null)
    {
        var currentTime = now ?? DateTimeOffset.UtcNow;
        var text = new StringBuilder();
        if (error is not null)
        {
            text.AppendLine("Refresh failed: " + error).AppendLine();
            text.AppendLine("Check that Codex is installed and signed in with ChatGPT (run codex login).");
            text.AppendLine($"The monitor retries every {refreshIntervalSeconds} seconds; you can also choose Refresh now.").AppendLine();
        }
        if (snapshot is null)
        {
            return text.AppendLine("No usage reading is available yet.").ToString();
        }
        text.AppendLine(error is null ? "Latest usage" : "Last successful reading (stale)");
        text.AppendLine("Updated: " + LocalTime(snapshot.RetrievedAt, timeZone).ToString("yyyy-MM-dd HH:mm:ss zzz", CultureInfo.InvariantCulture));
        if (snapshot.Usage.OrdinaryUsageAllowed is { } allowed)
        {
            text.AppendLine("Included usage allowed: " + (allowed ? "Yes" : "No"));
        }
        foreach (var (id, bucket) in snapshot.Usage.GetBuckets())
        {
            text.AppendLine().AppendLine(bucket.LimitName ?? id);
            if (bucket.PlanType is not null) { text.AppendLine("Plan: " + bucket.PlanType); }
            AddWindow(bucket.Primary, "Primary");
            AddWindow(bucket.Secondary, "Secondary");
            if (bucket.Primary is null && bucket.Secondary is null)
            {
                text.AppendLine("Usage windows: not provided");
            }
            if (bucket.Credits is { } credits) { text.AppendLine("Credits: " + CreditBalance(credits)); }
            if (bucket.RateLimitReachedType is not null) { text.AppendLine("Limit status: " + bucket.RateLimitReachedType); }
            if (bucket.SpendControlReached is { } reached) { text.AppendLine("Spend limit reached: " + (reached ? "Yes" : "No")); }
            if (bucket.IndividualLimit is { } spend)
            {
                text.AppendLine($"Individual spend: {spend.Used} / {spend.Limit} ({spend.RemainingPercent}% remaining)");
                text.AppendLine("Resets: " + ResetTime(spend.ResetsAt, timeZone, "yyyy-MM-dd HH:mm zzz", currentTime));
            }
        }
        if (snapshot.Usage.RateLimitResetCredits is { } resets)
        {
            text.AppendLine().AppendLine($"Available rate-limit resets: {resets.AvailableCount}");
            if (resets.Credits is { } credits)
            {
                foreach (var credit in credits)
                {
                    text.AppendLine($"  {credit.Title ?? credit.ResetType}: {credit.Status}");
                    if (credit.ExpiresAt is not null)
                    {
                        text.AppendLine("  Expires: " + ResetTime(credit.ExpiresAt, timeZone, "yyyy-MM-dd HH:mm zzz"));
                    }
                }
            }
        }
        return text.ToString();

        void AddWindow(RateLimitWindow? window, string label)
        {
            if (window is null) { return; }
            text.AppendLine($"{label} ({Duration(window.WindowDurationMins, "duration unknown")}): {Percent(window.UsedPercent)}% used");
            text.AppendLine("Resets: " + ResetTime(window.ResetsAt, timeZone, "yyyy-MM-dd HH:mm zzz", currentTime));
        }
    }

    /// <summary>Checks whether Codex explicitly reports an account or bucket limit.</summary>
    /// <param name="usage">The account usage response to inspect.</param>
    /// <returns>Whether included usage is disallowed or a bucket reports a reached rate or spending limit.</returns>
    public static bool IsLimited(RateLimitsResponse usage) => usage.OrdinaryUsageAllowed == false
        || usage.GetBuckets().Any(pair => pair.Value.RateLimitReachedType is not null || pair.Value.SpendControlReached == true);

    /// <summary>Truncates text with an ellipsis without splitting a surrogate pair at the cut.</summary>
    /// <param name="text">The text to limit.</param>
    /// <param name="length">The positive maximum length in UTF-16 code units, including any ellipsis.</param>
    /// <returns>The original text when it fits; otherwise, truncated text followed by an ellipsis.</returns>
    public static string Limit(string text, int length)
    {
        if (text.Length <= length) { return text; }
        var end = length - 1;
        if (end > 0 && char.IsHighSurrogate(text[end - 1])) { end--; }
        return text[..end] + "…";
    }

    /// <summary>Replaces line breaks and null characters with spaces for compact display text.</summary>
    /// <param name="text">The text to flatten.</param>
    /// <returns>Text with carriage returns, newlines, and null characters replaced.</returns>
    private static string SingleLine(string text) => text.Replace('\r', ' ').Replace('\n', ' ').Replace('\0', ' ');
    /// <summary>Formats a percentage value with at most one decimal place and no percent sign.</summary>
    /// <param name="value">The percentage value to format.</param>
    /// <returns>The value formatted using the invariant culture.</returns>
    private static string Percent(double value) => value.ToString("0.#", CultureInfo.InvariantCulture);
    /// <summary>Formats credit availability, preferring unlimited status and then the reported balance.</summary>
    /// <param name="credits">The reported credit information.</param>
    /// <returns>The balance or a description of credit availability.</returns>
    private static string CreditBalance(CreditsSnapshot credits) => credits.Unlimited ? "Unlimited"
        : credits.Balance ?? (credits.HasCredits ? "Available (balance unknown)" : "None available");

    /// <summary>Formats a duration using the largest exact unit among weeks, days, hours, and minutes.</summary>
    /// <param name="minutes">The duration in minutes, if known.</param>
    /// <param name="fallback">The text used for missing or nonpositive durations.</param>
    /// <returns>A compact duration label or the fallback text.</returns>
    private static string Duration(long? minutes, string fallback) => minutes switch
    {
        null or <= 0 => fallback,
        _ when minutes % 10080 == 0 => $"{minutes / 10080}w",
        _ when minutes % 1440 == 0 => $"{minutes / 1440}d",
        _ when minutes % 60 == 0 => $"{minutes / 60}h",
        _ => $"{minutes}m"
    };

    /// <summary>Converts a timestamp to the requested display time zone.</summary>
    /// <param name="value">The timestamp to convert.</param>
    /// <param name="timeZone">The destination time zone, or <see langword="null"/> for the local time zone.</param>
    /// <returns>The same instant expressed in the destination time zone.</returns>
    private static DateTimeOffset LocalTime(DateTimeOffset value, TimeZoneInfo? timeZone) =>
        TimeZoneInfo.ConvertTime(value, timeZone ?? TimeZoneInfo.Local);

    /// <summary>Formats a Unix timestamp with an optional countdown, tolerating missing or out-of-range times.</summary>
    /// <param name="seconds">The reset timestamp in Unix seconds, if known.</param>
    /// <param name="timeZone">The display time zone, or <see langword="null"/> for the local time zone.</param>
    /// <param name="format">The date and time format applied with the invariant culture.</param>
    /// <param name="now">The reference instant for a countdown, or <see langword="null"/> to show only the timestamp.</param>
    /// <returns>The formatted timestamp and optional time remaining, or <c>unknown</c> when it cannot be represented.</returns>
    private static string ResetTime(long? seconds, TimeZoneInfo? timeZone, string format, DateTimeOffset? now = null)
    {
        if (seconds is null) { return "unknown"; }
        try
        {
            var resetAt = DateTimeOffset.FromUnixTimeSeconds(seconds.Value);
            var timestamp = LocalTime(resetAt, timeZone)
                .ToString(format, CultureInfo.InvariantCulture);
            return now is { } currentTime ? $"{timestamp} ({RemainingTime(resetAt - currentTime)})" : timestamp;
        }
        catch (ArgumentOutOfRangeException) { return "unknown"; }
    }

    /// <summary>Formats time until reset using days and hours, hours and minutes, or minutes.</summary>
    /// <param name="remaining">The elapsed-time difference between the reset and current instants.</param>
    /// <returns>A compact countdown, <c>&lt;1m left</c> for the last minute, or <c>due</c> at or after reset.</returns>
    /// <remarks>Smaller units are omitted. A due reset does not imply that a new quota has been retrieved.</remarks>
    private static string RemainingTime(TimeSpan remaining)
    {
        if (remaining <= TimeSpan.Zero) { return "due"; }
        if (remaining.TotalMinutes < 1) { return "<1m left"; }
        if (remaining.Days > 0)
        {
            return remaining.Hours > 0 ? $"{remaining.Days}d {remaining.Hours}h left" : $"{remaining.Days}d left";
        }
        if (remaining.Hours > 0)
        {
            return remaining.Minutes > 0 ? $"{remaining.Hours}h {remaining.Minutes}m left" : $"{remaining.Hours}h left";
        }
        return $"{remaining.Minutes}m left";
    }
}

using CodexUsageMonitor.Core.Configuration;
using CodexUsageMonitor.Core.Models;

namespace CodexUsageMonitor.Core.Presentation;

/// <summary>Derives tray icon severity and remaining quota from account usage.</summary>
public static class UsageIconClassifier
{
    /// <summary>Determines the highest state across both quota windows using the rules in <see cref="Describe"/>.</summary>
    /// <param name="snapshot">The latest successful reading, if available.</param>
    /// <param name="thresholds">The validated elevated, high, and critical boundaries.</param>
    /// <param name="hasError">Whether the latest retrieval failed.</param>
    /// <returns>The usage severity or retrieval status to display.</returns>
    public static UsageIconState Classify(UsageSnapshot? snapshot, IconUsageThresholds thresholds, bool hasError = false)
        => Describe(snapshot, thresholds, hasError).State;

    /// <summary>Calculates independent severity and remaining quota for the primary and secondary windows.</summary>
    /// <param name="snapshot">The latest successful reading, if available.</param>
    /// <param name="thresholds">The validated elevated, high, and critical boundaries, independent of notifications.</param>
    /// <param name="hasError">Whether to show an error state instead of the reading.</param>
    /// <returns>Both window appearances, with percentages clamped to 0 through 100 or absent when unavailable.</returns>
    /// <remarks>
    /// Each half uses the highest valid usage for its window across all buckets. Primary and secondary
    /// readings never replace each other. Nonfinite and negative values are ignored, and explicit account
    /// limits force critical severity on both halves without changing their remaining percentages.
    /// Notification thresholds do not affect either half's color.
    /// </remarks>
    public static UsageIconAppearance Describe(UsageSnapshot? snapshot, IconUsageThresholds thresholds, bool hasError = false)
    {
        if (hasError) { return new(new(UsageIconState.Error, null), new(UsageIconState.Error, null)); }
        if (snapshot is null) { return new(new(UsageIconState.Unknown, null), new(UsageIconState.Unknown, null)); }

        double? primaryUsage = null;
        double? secondaryUsage = null;
        foreach (var (_, bucket) in snapshot.Usage.GetBuckets())
        {
            Include(ref primaryUsage, bucket.Primary);
            Include(ref secondaryUsage, bucket.Secondary);
        }
        var isLimited = UsageTextFormatter.IsLimited(snapshot.Usage);

        return new(DescribeWindow(primaryUsage), DescribeWindow(secondaryUsage));

        UsageWindowAppearance DescribeWindow(double? highestUsage)
        {
            double? remaining = highestUsage is { } used ? Math.Clamp(100 - used, 0, 100) : null;
            if (isLimited) { return new(UsageIconState.Critical, remaining); }
            if (highestUsage is null) { return new(UsageIconState.Unknown, null); }
            if (highestUsage >= 100 || highestUsage >= thresholds.Critical) { return new(UsageIconState.Critical, remaining); }
            if (highestUsage >= thresholds.High) { return new(UsageIconState.High, remaining); }
            if (highestUsage >= thresholds.Elevated) { return new(UsageIconState.Elevated, remaining); }
            return new(UsageIconState.Normal, remaining);
        }

        static void Include(ref double? highestUsage, RateLimitWindow? window)
        {
            if (window is not null && double.IsFinite(window.UsedPercent) && window.UsedPercent >= 0)
            {
                highestUsage = Math.Max(highestUsage ?? 0, window.UsedPercent);
            }
        }
    }
}

using System.Collections.Immutable;
using CodexUsageMonitor.Core.Models;

namespace CodexUsageMonitor.Core.Notifications;

/// <summary>The thresholds reached by one bucket's quota window during a reading.</summary>
/// <param name="BucketId">The metered bucket identifier.</param>
/// <param name="BucketName">The bucket display name.</param>
/// <param name="WindowName">The primary or secondary window label.</param>
/// <param name="Window">The reading that triggered the alert.</param>
/// <param name="ThresholdsPercent">The consumed-quota percentages reached in this reading.</param>
public sealed record UsageThresholdAlert(
    string BucketId,
    string BucketName,
    string WindowName,
    RateLimitWindow Window,
    ImmutableArray<int> ThresholdsPercent);

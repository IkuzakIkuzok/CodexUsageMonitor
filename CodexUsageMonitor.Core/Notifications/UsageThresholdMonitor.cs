using System.Collections.Immutable;
using CodexUsageMonitor.Core.Models;

namespace CodexUsageMonitor.Core.Notifications;

// Called only for successful readings, on the UI thread. State is in memory
// and independent for each metered bucket and quota window.
/// <summary>Tracks usage threshold crossings independently for each bucket and quota window.</summary>
/// <param name="thresholds">Consumed-quota percentages; only unique integers from 1 through 100 are retained.</param>
/// <remarks>State is held in memory. Calls must be serialized by the caller.</remarks>
public sealed class UsageThresholdMonitor(IEnumerable<int> thresholds)
{
    private readonly ImmutableArray<int> thresholds = [.. thresholds.Where(value => value is >= 1 and <= 100).Distinct().Order()];
    private readonly Dictionary<(string BucketId, string WindowName), RateLimitWindow> previousWindows = [];
    private string? accountId;

    /// <summary>Records a successful reading and returns newly reached thresholds.</summary>
    /// <param name="snapshot">The latest successfully retrieved usage snapshot.</param>
    /// <returns>One alert per window with crossings, or an empty array when no thresholds are newly reached.</returns>
    /// <remarks>
    /// The first valid reading reports thresholds already reached. Falling below a threshold, a new
    /// quota window, or a known account change permits another alert. Missing or invalid readings
    /// preserve the previous window state.
    /// </remarks>
    public ImmutableArray<UsageThresholdAlert> Observe(UsageSnapshot snapshot)
    {
        if (this.thresholds.IsEmpty) { return []; }

        if (snapshot.Usage.AccountId is { } currentAccountId)
        {
            if (this.accountId is not null && this.accountId != currentAccountId)
            {
                this.previousWindows.Clear();
            }
            this.accountId = currentAccountId;
        }

        var alerts = ImmutableArray.CreateBuilder<UsageThresholdAlert>();
        foreach (var (id, bucket) in snapshot.Usage.GetBuckets())
        {
            ObserveWindow(id, bucket.LimitName ?? id, "Primary", bucket.Primary);
            ObserveWindow(id, bucket.LimitName ?? id, "Secondary", bucket.Secondary);
        }
        return alerts.ToImmutable();

        void ObserveWindow(string id, string name, string windowName, RateLimitWindow? window)
        {
            // Missing or invalid data must not rearm thresholds or imply zero usage.
            if (window is null || !double.IsFinite(window.UsedPercent) || window.UsedPercent < 0) { return; }

            var key = (id, windowName);
            this.previousWindows.TryGetValue(key, out var previous);
            var newWindow = previous is null
                || (previous.ResetsAt is { } previousReset && window.ResetsAt is { } reset && previousReset != reset)
                || (previous.WindowDurationMins is { } previousDuration
                    && window.WindowDurationMins is { } duration && previousDuration != duration);

            var crossed = this.thresholds.Where(threshold => window.UsedPercent >= threshold
                && (newWindow || previous!.UsedPercent < threshold)).ToImmutableArray();

            // Keep known window metadata when the server temporarily omits it.
            this.previousWindows[key] = window with
            {
                ResetsAt = window.ResetsAt ?? previous?.ResetsAt,
                WindowDurationMins = window.WindowDurationMins ?? previous?.WindowDurationMins
            };
            if (!crossed.IsEmpty)
            {
                alerts.Add(new UsageThresholdAlert(id, name, windowName, window, crossed));
            }
        }
    }
}

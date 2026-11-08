using System.Collections.Immutable;
using System.Text.Json;

namespace CodexUsageMonitor.Core.Configuration;

/// <summary>Validated polling, icon color, and notification settings loaded from a JSON file.</summary>
public sealed record MonitorSettings
{
    /// <summary>The configuration filename used beside the application executable.</summary>
    public const string FileName = "settings.json";
    /// <summary>The polling interval, in seconds, used when no valid interval is configured.</summary>
    public const int DefaultRefreshIntervalSeconds = 60;
    // Windows Forms timers accept a positive Int32 interval in milliseconds.
    /// <summary>The largest whole-second interval that fits the timer's millisecond range.</summary>
    public const int MaxRefreshIntervalSeconds = int.MaxValue / 1000;

    /// <summary>Gets the one-minute polling interval, default icon colors, and notification thresholds of 80, 90, and 100 percent.</summary>
    public static MonitorSettings Default { get; } = new(DefaultRefreshIntervalSeconds,
        IconUsageThresholds.Default, IconStateColors.Default, [80, 90, 100]);

    /// <summary>Gets the validated polling interval in seconds.</summary>
    public int RefreshIntervalSeconds { get; }
    /// <summary>Gets the polling interval in milliseconds for the UI timer.</summary>
    public int RefreshIntervalMilliseconds => this.RefreshIntervalSeconds * 1000;
    /// <summary>Gets the three increasing icon severity thresholds, independent of notification thresholds.</summary>
    public IconUsageThresholds IconThresholdsPercent { get; }
    /// <summary>Gets the configured normal, elevated, high, and critical drawing colors.</summary>
    public IconStateColors IconColors { get; }
    /// <summary>Gets sorted, unique notification thresholds; an empty array disables notifications.</summary>
    public ImmutableArray<int> NotificationThresholdsPercent { get; }

    /// <summary>Creates settings from an already validated interval, icon states, and notification list.</summary>
    /// <param name="refreshIntervalSeconds">The polling interval in seconds.</param>
    /// <param name="iconThresholdsPercent">The three increasing severity boundaries.</param>
    /// <param name="iconColors">The four severity drawing colors.</param>
    /// <param name="notificationThresholdsPercent">The normalized notification percentages of consumed quota.</param>
    private MonitorSettings(int refreshIntervalSeconds, IconUsageThresholds iconThresholdsPercent, IconStateColors iconColors,
        ImmutableArray<int> notificationThresholdsPercent)
    {
        this.RefreshIntervalSeconds = refreshIntervalSeconds;
        this.IconThresholdsPercent = iconThresholdsPercent;
        this.IconColors = iconColors;
        this.NotificationThresholdsPercent = notificationThresholdsPercent;
    }

    /// <summary>Reads configuration once, applying defaults to missing or invalid settings.</summary>
    /// <param name="path">The JSON file path, or <see langword="null"/> to use the file beside the executable.</param>
    /// <returns>Validated settings, or <see cref="Default"/> for unreadable or malformed configuration.</returns>
    /// <remarks>Each setting is validated independently. Legacy shared thresholds are accepted without modifying the file.</remarks>
    public static MonitorSettings Load(string? path = null)
    {
        // Resolve relative to the executable, even when launched from a shortcut.
        path ??= Path.Combine(AppContext.BaseDirectory, FileName);
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return Default;
            }

            if (!root.TryGetProperty("refreshIntervalSeconds", out var interval)
                || interval.ValueKind != JsonValueKind.Number
                || !interval.TryGetInt32(out var seconds)
                || seconds < 1 || seconds > MaxRefreshIntervalSeconds)
            {
                seconds = DefaultRefreshIntervalSeconds;
            }

            var legacyThresholds = root.TryGetProperty("usageThresholdsPercent", out var legacy)
                ? ReadNotificationThresholds(legacy) : Default.NotificationThresholdsPercent;
            var notificationThresholds = root.TryGetProperty("notificationThresholdsPercent", out var notifications)
                ? ReadNotificationThresholds(notifications) : legacyThresholds;
            var (iconThresholds, iconColors) = ReadIconStates(root, legacyThresholds);
            return new MonitorSettings(seconds, iconThresholds, iconColors, notificationThresholds);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return Default;
        }
    }

    /// <summary>Reads sorted, unique integer thresholds from 1 through 100, preserving an explicitly empty list.</summary>
    /// <param name="thresholds">The notification threshold JSON value.</param>
    /// <returns>Valid thresholds, an explicitly empty list, or the default list when no valid list is supplied.</returns>
    private static ImmutableArray<int> ReadNotificationThresholds(JsonElement thresholds)
    {
        if (thresholds.ValueKind != JsonValueKind.Array)
        {
            return Default.NotificationThresholdsPercent;
        }

        // An explicitly empty list disables notifications.
        if (thresholds.GetArrayLength() == 0) { return []; }

        var valid = new SortedSet<int>();
        foreach (var item in thresholds.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.Number && item.TryGetInt32(out var percent)
                && percent is >= 1 and <= 100)
            {
                valid.Add(percent);
            }
        }

        return valid.Count > 0 ? [.. valid] : Default.NotificationThresholdsPercent;
    }

    /// <summary>Reads semantic icon states with independent defaults for thresholds and drawing colors.</summary>
    /// <param name="root">The configuration JSON object.</param>
    /// <param name="legacyThresholds">The normalized legacy notification thresholds.</param>
    /// <returns>The validated severity boundaries and drawing colors.</returns>
    private static (IconUsageThresholds Thresholds, IconStateColors Colors) ReadIconStates(
        JsonElement root, ImmutableArray<int> legacyThresholds)
    {
        if (!root.TryGetProperty("iconStates", out var states))
        {
            return (ReadLegacyIconThresholds(root, legacyThresholds), IconStateColors.Default);
        }
        if (states.ValueKind != JsonValueKind.Object)
        {
            return (IconUsageThresholds.Default, IconStateColors.Default);
        }

        var defaults = IconUsageThresholds.Default;
        var thresholds = IconUsageThresholds.CreateOrDefault(
            ReadThreshold("elevated", defaults.Elevated),
            ReadThreshold("high", defaults.High),
            ReadThreshold("critical", defaults.Critical));
        var colors = IconStateColors.Default;
        return (thresholds, new(
            ReadColor("normal", colors.Normal), ReadColor("elevated", colors.Elevated),
            ReadColor("high", colors.High), ReadColor("critical", colors.Critical)));

        int ReadThreshold(string name, int fallback)
        {
            return states.TryGetProperty(name, out var state) && state.ValueKind == JsonValueKind.Object
                && state.TryGetProperty("thresholdPercent", out var threshold) && threshold.ValueKind == JsonValueKind.Number
                && threshold.TryGetInt32(out var percent) && percent is >= 1 and <= 100
                    ? percent : fallback;
        }

        RgbColor ReadColor(string name, RgbColor fallback)
        {
            return states.TryGetProperty(name, out var state) && state.ValueKind == JsonValueKind.Object
                && state.TryGetProperty("color", out var color) && color.ValueKind == JsonValueKind.String
                    ? RgbColor.ParseOrDefault(color.GetString(), fallback) : fallback;
        }
    }

    /// <summary>Reads the former color-named boundaries or shared threshold list when semantic icon states are absent.</summary>
    /// <param name="root">The configuration JSON object.</param>
    /// <param name="legacyThresholds">The normalized legacy notification thresholds.</param>
    /// <returns>Validated color boundaries, migrated legacy boundaries, or the default icon boundaries.</returns>
    private static IconUsageThresholds ReadLegacyIconThresholds(JsonElement root, ImmutableArray<int> legacyThresholds)
    {
        if (!root.TryGetProperty("iconColorThresholdsPercent", out var colors))
        {
            // Older lists used their first, second, and last values for the three color boundaries.
            return root.TryGetProperty("usageThresholdsPercent", out _) && legacyThresholds.Length >= 3
                ? IconUsageThresholds.CreateOrDefault(legacyThresholds[0], legacyThresholds[1], legacyThresholds[^1])
                : Default.IconThresholdsPercent;
        }
        if (colors.ValueKind != JsonValueKind.Object
            || !TryReadPercent("amber", out var amber)
            || !TryReadPercent("orange", out var orange)
            || !TryReadPercent("red", out var red))
        {
            return Default.IconThresholdsPercent;
        }
        return IconUsageThresholds.CreateOrDefault(amber, orange, red);

        bool TryReadPercent(string name, out int value)
        {
            value = 0;
            return colors.TryGetProperty(name, out var element) && element.ValueKind == JsonValueKind.Number
                && element.TryGetInt32(out value);
        }
    }
}

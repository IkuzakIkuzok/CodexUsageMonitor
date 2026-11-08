namespace CodexUsageMonitor.Core.Presentation;

/// <summary>The usage severity or retrieval status represented by the tray icon.</summary>
public enum UsageIconState
{
    /// <summary>No valid usage percentage or explicit limit status is available.</summary>
    Unknown,
    /// <summary>Usage is below the elevated threshold.</summary>
    Normal,
    /// <summary>Usage has reached the elevated threshold without reaching the high threshold.</summary>
    Elevated,
    /// <summary>Usage has reached the high threshold without reaching the critical threshold.</summary>
    High,
    /// <summary>The critical threshold, full quota consumption, or an explicit account limit has been reached.</summary>
    Critical,
    /// <summary>The most recent retrieval failed.</summary>
    Error
}

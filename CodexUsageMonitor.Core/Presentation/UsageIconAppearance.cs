namespace CodexUsageMonitor.Core.Presentation;

/// <summary>The independently rendered primary and secondary halves of the tray icon.</summary>
/// <param name="Primary">The left half, representing the primary quota window (normally five hours).</param>
/// <param name="Secondary">The right half, representing the secondary quota window (normally one week).</param>
public sealed record UsageIconAppearance(UsageWindowAppearance Primary, UsageWindowAppearance Secondary)
{
    /// <summary>Gets the highest severity or retrieval status across both halves.</summary>
    public UsageIconState State => (UsageIconState)Math.Max((int)this.Primary.State, (int)this.Secondary.State);
}

/// <summary>The visual state and remaining quota for one half of the tray icon.</summary>
/// <param name="State">The usage severity or retrieval status.</param>
/// <param name="RemainingPercent">The percentage remaining, or <see langword="null"/> when unavailable.</param>
public sealed record UsageWindowAppearance(UsageIconState State, double? RemainingPercent);

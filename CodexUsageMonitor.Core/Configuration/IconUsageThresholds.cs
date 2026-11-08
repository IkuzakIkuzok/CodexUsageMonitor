namespace CodexUsageMonitor.Core.Configuration;

/// <summary>Three validated, increasing consumed-quota percentages for the icon's severity transitions.</summary>
public sealed record IconUsageThresholds
{
    /// <summary>Gets the default elevated, high, and critical boundaries of 80, 90, and 100 percent.</summary>
    public static IconUsageThresholds Default { get; } = new(80, 90, 100);

    /// <summary>Gets the usage percentage at which normal usage becomes elevated.</summary>
    public int Elevated { get; }
    /// <summary>Gets the usage percentage at which elevated usage becomes high.</summary>
    public int High { get; }
    /// <summary>Gets the usage percentage at which high usage becomes critical.</summary>
    public int Critical { get; }

    /// <summary>Stores an already validated set of severity boundaries.</summary>
    /// <param name="elevated">The normal-to-elevated boundary.</param>
    /// <param name="high">The elevated-to-high boundary.</param>
    /// <param name="critical">The high-to-critical boundary.</param>
    private IconUsageThresholds(int elevated, int high, int critical)
    {
        this.Elevated = elevated;
        this.High = high;
        this.Critical = critical;
    }

    /// <summary>Creates severity boundaries when they are strictly increasing integers from 1 through 100.</summary>
    /// <param name="elevated">The proposed normal-to-elevated boundary.</param>
    /// <param name="high">The proposed elevated-to-high boundary.</param>
    /// <param name="critical">The proposed high-to-critical boundary.</param>
    /// <returns>The validated boundaries, or <see cref="Default"/> when any boundary is invalid.</returns>
    public static IconUsageThresholds CreateOrDefault(int elevated, int high, int critical) =>
        elevated >= 1 && elevated < high && high < critical && critical <= 100
            ? new(elevated, high, critical) : Default;
}

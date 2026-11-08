using System.Globalization;

namespace CodexUsageMonitor.Core.Configuration;

/// <summary>An opaque RGB color independent of the Windows drawing APIs.</summary>
/// <param name="Red">The red channel from 0 through 255.</param>
/// <param name="Green">The green channel from 0 through 255.</param>
/// <param name="Blue">The blue channel from 0 through 255.</param>
public readonly record struct RgbColor(byte Red, byte Green, byte Blue)
{
    /// <summary>Gets the color as an uppercase six-digit hexadecimal code.</summary>
    public string HexCode => $"#{this.Red:X2}{this.Green:X2}{this.Blue:X2}";

    /// <summary>Parses an opaque color in <c>#RRGGBB</c> format, accepting either letter case.</summary>
    /// <param name="value">The proposed color code.</param>
    /// <param name="fallback">The color to use when the code is absent or invalid.</param>
    /// <returns>The parsed color, or the fallback without throwing for malformed codes.</returns>
    public static RgbColor ParseOrDefault(string? value, RgbColor fallback)
    {
        return value is { Length: 7 } && value[0] == '#'
            && int.TryParse(value.AsSpan(1), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var rgb)
                ? new((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb) : fallback;
    }
}

/// <summary>The independent drawing colors for the four quota severity states.</summary>
/// <param name="Normal">The color below the elevated threshold.</param>
/// <param name="Elevated">The color from the elevated threshold up to the high threshold.</param>
/// <param name="High">The color from the high threshold up to the critical threshold.</param>
/// <param name="Critical">The color at the critical threshold or when an explicit account limit is reported.</param>
public sealed record IconStateColors(RgbColor Normal, RgbColor Elevated, RgbColor High, RgbColor Critical)
{
    /// <summary>Gets the original green, amber, orange, and red palette.</summary>
    public static IconStateColors Default { get; } = new(
        new(22, 140, 76), new(202, 145, 0), new(232, 108, 12), new(204, 48, 48));
}

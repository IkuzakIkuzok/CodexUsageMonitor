using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using CodexUsageMonitor.Core.Configuration;
using CodexUsageMonitor.Core.Presentation;

namespace CodexUsageMonitor;

/// <summary>Draws split circular usage icons and manages temporary native icon handles.</summary>
internal static partial class TrayIconFactory
{
    /// <summary>Creates a 32-pixel icon with primary quota on the left and secondary quota on the right.</summary>
    /// <param name="appearance">The independent severity and remaining percentage for each half.</param>
    /// <param name="colors">The configured drawing colors for the four quota severity states.</param>
    /// <returns>A new icon owned by the caller, which must dispose it when no longer in use.</returns>
    /// <remarks>Within each half, the colored sector represents remaining quota and the dark sector represents consumption.</remarks>
    public static Icon Create(UsageIconAppearance appearance, IconStateColors colors)
    {
        using var bitmap = new Bitmap(32, 32);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.Clear(Color.Transparent);
        var state = appearance.State;
        if (state is UsageIconState.Unknown or UsageIconState.Error
            || (appearance.Primary.RemainingPercent is null && appearance.Secondary.RemainingPercent is null))
        {
            using var fill = new SolidBrush(StateColor(state, colors));
            graphics.FillEllipse(fill, 1, 1, 30, 30);
            DrawStatusSymbol();
        }
        else
        {
            DrawHalf(graphics, appearance.Primary, colors, isPrimary: true);
            DrawHalf(graphics, appearance.Secondary, colors, isPrimary: false);
        }

        var handle = bitmap.GetHicon();
        try
        {
            using var borrowed = Icon.FromHandle(handle);
            return (Icon)borrowed.Clone();
        }
        finally { DestroyIcon(handle); }

        void DrawStatusSymbol()
        {
            if (state == UsageIconState.Error)
            {
                using var stroke = new Pen(Color.White, 3.5f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
                graphics.DrawLine(stroke, 10, 10, 22, 22);
                graphics.DrawLine(stroke, 22, 10, 10, 22);
            }
            else if (state == UsageIconState.Critical)
            {
                // A backend limit can be reported without percentage data.
                graphics.FillRectangle(Brushes.White, 14, 7, 4, 12);
                graphics.FillEllipse(Brushes.White, 14, 22, 4, 4);
            }
            else
            {
                graphics.FillEllipse(Brushes.White, 7, 14, 4, 4);
                graphics.FillEllipse(Brushes.White, 14, 14, 4, 4);
                graphics.FillEllipse(Brushes.White, 21, 14, 4, 4);
            }
        }
    }

    /// <summary>Draws one semicircle with an independent quota scale and leaves a gap at the center.</summary>
    /// <param name="graphics">The icon drawing surface.</param>
    /// <param name="window">The quota window's color state and remaining percentage.</param>
    /// <param name="colors">The configured severity drawing colors.</param>
    /// <param name="isPrimary">Whether to draw the primary window on the left rather than the secondary on the right.</param>
    private static void DrawHalf(Graphics graphics, UsageWindowAppearance window, IconStateColors colors, bool isPrimary)
    {
        var saved = graphics.Save();
        try
        {
            // A two-pixel transparent divider remains visible when scaled to a 16-pixel tray icon.
            graphics.SetClip(new Rectangle(isPrimary ? 0 : 17, 0, 15, 32));
            var direction = isPrimary ? -1 : 1;
            var color = StateColor(window.State, colors);
            using var fill = new SolidBrush(color);
            if (window.State is not (UsageIconState.Unknown or UsageIconState.Error)
                && window.RemainingPercent is { } remaining && double.IsFinite(remaining))
            {
                using var consumed = new SolidBrush(Color.FromArgb(64, 72, 84));
                graphics.FillPie(consumed, 2, 2, 28, 28, -90, direction * 180);
                remaining = Math.Clamp(remaining, 0, 100);
                if (remaining > 0)
                {
                    // Both gauges start at twelve o'clock and fill outward around their own half.
                    graphics.FillPie(fill, 2, 2, 28, 28, -90, (float)(direction * remaining * 1.8));
                }
                using var outline = new Pen(color, 2);
                graphics.DrawArc(outline, 2, 2, 28, 28, -90, direction * 180);
            }
            else
            {
                graphics.FillPie(fill, 2, 2, 28, 28, -90, direction * 180);
                var centerX = isPrimary ? 9 : 23;
                if (window.State == UsageIconState.Critical)
                {
                    graphics.FillRectangle(Brushes.White, centerX - 1.5f, 9, 3, 10);
                    graphics.FillEllipse(Brushes.White, centerX - 1.5f, 22, 3, 3);
                }
                else
                {
                    // A dash distinguishes an unavailable window from a known, exhausted quota.
                    using var dash = new Pen(Color.White, 2);
                    graphics.DrawLine(dash, centerX - 2, 16, centerX + 2, 16);
                }
            }
        }
        finally { graphics.Restore(saved); }
    }

    /// <summary>Maps a quota's severity or retrieval status to its drawing color.</summary>
    /// <param name="state">The quota or retrieval state.</param>
    /// <param name="colors">The configured severity drawing colors.</param>
    /// <returns>The severity color, or gray for unavailable and failed readings.</returns>
    private static Color StateColor(UsageIconState state, IconStateColors colors)
    {
        var color = state switch
        {
            UsageIconState.Normal => colors.Normal,
            UsageIconState.Elevated => colors.Elevated,
            UsageIconState.High => colors.High,
            UsageIconState.Critical => colors.Critical,
            _ => new RgbColor(111, 120, 132)
        };
        return Color.FromArgb(color.Red, color.Green, color.Blue);
    }

    /// <summary>Releases the native icon handle created from the temporary bitmap.</summary>
    /// <param name="handle">The native icon handle to destroy.</param>
    /// <returns>Whether the native handle was successfully destroyed.</returns>
    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DestroyIcon(IntPtr handle);
}

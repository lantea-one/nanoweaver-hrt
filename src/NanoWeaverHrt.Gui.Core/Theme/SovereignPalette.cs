namespace NanoWeaverHrt.Gui.Core.Theme;

/// <summary>
/// Defines visual design tokens, high-contrast dark theme colors, and UI palette constants for NanoWeaver HRT desktop and mobile shells.
/// </summary>
public static class SovereignPalette
{
    // WHAT: Visual design tokens establishing a high-contrast dark mode sovereign palette.
    // WHY:  Ensures shared visual styling across desktop (Linux/macOS/Windows) and mobile (Android/iOS) UI heads.
    // HOW:  Expose hex color constants aligned with docs/assets/sovereign-theme.css and dark-mode accessibility standards.

    /// <summary>
    /// Dark-mode root canvas background hex code (#0b0f19).
    /// </summary>
    public const string BackgroundHex = "#0b0f19";

    /// <summary>
    /// Elevated surface and card panel background hex code (#131b2e).
    /// </summary>
    public const string SurfaceHex = "#131b2e";

    /// <summary>
    /// Subtle secondary panel and sidebar background hex code (#162032).
    /// </summary>
    public const string SurfaceSubtleHex = "#162032";

    /// <summary>
    /// Sovereign cyan primary brand and interactive accent hex code (#00f5d4).
    /// </summary>
    public const string AccentCyanHex = "#00f5d4";

    /// <summary>
    /// Sovereign magenta alert, critical condition, and emergency stop hex code (#ff2a85).
    /// </summary>
    public const string AlertMagentaHex = "#ff2a85";

    /// <summary>
    /// Healthy, online, and verified condition green hex code (#00e676).
    /// </summary>
    public const string SuccessGreenHex = "#00e676";

    /// <summary>
    /// Degraded, thermal warning, and attention amber hex code (#ffd600).
    /// </summary>
    public const string WarningAmberHex = "#ffd600";

    /// <summary>
    /// Standard structural border and separator hex code (#1e293b).
    /// </summary>
    public const string BorderHex = "#1e293b";

    /// <summary>
    /// Highlighted and focused element border hex code (#334155).
    /// </summary>
    public const string BorderHighlightHex = "#334155";

    /// <summary>
    /// High-contrast primary foreground text hex code (#f0f6fc).
    /// </summary>
    public const string TextPrimaryHex = "#f0f6fc";

    /// <summary>
    /// Secondary muted foreground text hex code (#94a3b8).
    /// </summary>
    public const string TextMutedHex = "#94a3b8";

    /// <summary>
    /// Tertiary dim, hint, and disabled foreground text hex code (#64748b).
    /// </summary>
    public const string TextDimHex = "#64748b";
}

namespace QuotaDock.Core.Presentation;

using QuotaDock.Core.Configuration;

/// <summary>
/// A theme-agnostic 8-bit color. Kept free of any UI framework type so the
/// derivation math can be unit-tested without Windows. <see cref="ThemeApplier"/>
/// in the app layer maps these to platform colors and brushes.
/// </summary>
public readonly record struct Argb(byte A, byte R, byte G, byte B)
{
    public Argb WithAlpha(byte a) => new(a, R, G, B);
}

/// <summary>
/// The fully-resolved set of colors a theme produces, including the per-theme
/// translucency (alpha) that lets the Glassy/Mica backdrops show through.
/// </summary>
public sealed record ResolvedTheme(
    Argb Canvas,
    Argb Surface,
    Argb SurfaceRaised,
    Argb Text,
    Argb Muted,
    Argb Accent,
    Argb OnAccent,
    Argb Border,
    Argb Track);

/// <summary>
/// Pure color math for QuotaDock themes. Given the four user-editable colors
/// (background, text, foreground, accent), the color mode, and the theme, it
/// derives a coherent layered palette: card surfaces, borders, progress tracks,
/// and a readable on-accent color are all computed so a custom background never
/// produces an unreadable or flat UI. Unknown hex strings fall back safely.
/// </summary>
public static class ThemePalette
{
    private static readonly Argb White = new(255, 255, 255, 255);
    private static readonly Argb Black = new(255, 0, 0, 0);
    private static readonly Argb Fallback = new(255, 16, 19, 26);

    public static Argb ParseHex(string? hex, Argb fallback)
    {
        if (string.IsNullOrWhiteSpace(hex))
        {
            return fallback;
        }

        var value = hex.Trim();
        if (value.StartsWith('#'))
        {
            value = value[1..];
        }

        try
        {
            return value.Length switch
            {
                3 => new Argb(
                    255,
                    (byte)Convert.ToInt32(new string(value[0], 2), 16),
                    (byte)Convert.ToInt32(new string(value[1], 2), 16),
                    (byte)Convert.ToInt32(new string(value[2], 2), 16)),
                6 => new Argb(
                    255,
                    (byte)Convert.ToInt32(value[..2], 16),
                    (byte)Convert.ToInt32(value[2..4], 16),
                    (byte)Convert.ToInt32(value[4..6], 16)),
                8 => new Argb(
                    (byte)Convert.ToInt32(value[..2], 16),
                    (byte)Convert.ToInt32(value[2..4], 16),
                    (byte)Convert.ToInt32(value[4..6], 16),
                    (byte)Convert.ToInt32(value[6..8], 16)),
                _ => fallback
            };
        }
        catch (Exception exception) when (
            exception is FormatException or OverflowException or ArgumentException)
        {
            return fallback;
        }
    }

    public static string ToHex(Argb color) =>
        $"#{color.R:X2}{color.G:X2}{color.B:X2}";

    /// <summary>Linear blend of two colors; t=0 yields a, t=1 yields b.</summary>
    public static Argb Mix(Argb a, Argb b, double t)
    {
        t = double.Clamp(t, 0d, 1d);
        return new Argb(
            (byte)Math.Round(a.A + (b.A - a.A) * t),
            (byte)Math.Round(a.R + (b.R - a.R) * t),
            (byte)Math.Round(a.G + (b.G - a.G) * t),
            (byte)Math.Round(a.B + (b.B - a.B) * t));
    }

    /// <summary>WCAG relative luminance in [0,1].</summary>
    public static double RelativeLuminance(Argb c)
    {
        static double Channel(byte value)
        {
            var s = value / 255d;
            return s <= 0.03928d ? s / 12.92d : Math.Pow((s + 0.055d) / 1.055d, 2.4d);
        }

        return 0.2126d * Channel(c.R) + 0.7152d * Channel(c.G) + 0.0722d * Channel(c.B);
    }

    /// <summary>A color guaranteed readable on top of the given accent.</summary>
    public static Argb OnAccent(Argb accent) =>
        RelativeLuminance(accent) > 0.6d ? new Argb(255, 16, 34, 29) : White;

    public static Argb DeriveSurface(Argb background, ColorMode mode) =>
        Mix(background, mode == ColorMode.Dark ? White : Black, 0.05);

    public static Argb DeriveRaised(Argb surface, ColorMode mode) =>
        Mix(surface, mode == ColorMode.Dark ? White : Black, 0.05);

    public static Argb DeriveBorder(Argb background, ColorMode mode) =>
        Mix(background, mode == ColorMode.Dark ? White : Black, 0.12);

    public static Argb DeriveTrack(Argb background, ColorMode mode) =>
        Mix(background, mode == ColorMode.Dark ? White : Black, 0.06);

    public static ResolvedTheme Resolve(AppearanceSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var background = ParseHex(settings.Background, Fallback);
        var text = ParseHex(settings.Text, White);
        var muted = ParseHex(settings.Foreground, new Argb(255, 154, 166, 184));
        var accent = ParseHex(settings.Accent, new Argb(255, 98, 214, 181));
        var surface = DeriveSurface(background, settings.Mode);
        var raised = DeriveRaised(surface, settings.Mode);
        var border = DeriveBorder(background, settings.Mode);
        var track = DeriveTrack(background, settings.Mode);

        // Translucency lets the Glassy (acrylic) and Mica backdrops show through
        // the canvas and cards. The solid Default theme keeps everything opaque so
        // the chosen background color shows exactly.
        var (canvasA, surfaceA, raisedA, borderA) = settings.Theme switch
        {
            ThemeKind.Glassy => ((byte)35, (byte)95, (byte)110, (byte)70),
            ThemeKind.Mica => ((byte)150, (byte)190, (byte)200, (byte)150),
            _ => ((byte)255, (byte)255, (byte)255, (byte)255)
        };

        return new ResolvedTheme(
            background.WithAlpha(canvasA),
            surface.WithAlpha(surfaceA),
            raised.WithAlpha(raisedA),
            text,
            muted,
            accent,
            OnAccent(accent),
            border.WithAlpha(borderA),
            track);
    }
}

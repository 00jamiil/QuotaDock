namespace QuotaDock.Core.Configuration;

/// <summary>Visual treatment / window backdrop.</summary>
public enum ThemeKind
{
    /// <summary>Solid surfaces; the chosen background color shows exactly.</summary>
    Default,

    /// <summary>Frosted-glass acrylic backdrop with translucent surfaces.</summary>
    Glassy,

    /// <summary>Subtle Mica material backdrop with semi-translucent surfaces.</summary>
    Mica
}

/// <summary>Base light/dark scheme; also drives native WinUI control theming.</summary>
public enum ColorMode
{
    Dark,
    Light
}

/// <summary>
/// The user's appearance choices. The four color strings are the only directly
/// editable colors; card surfaces, borders, progress tracks, and the on-accent
/// color are derived from them by <see cref="Core.Presentation.ThemePalette"/> so
/// a custom palette always stays coherent. Stored as hex strings so the settings
/// JSON stays human-readable and forward-compatible.
/// </summary>
public sealed record AppearanceSettings
{
    public ThemeKind Theme { get; init; } = ThemeKind.Default;
    public ColorMode Mode { get; init; } = ColorMode.Dark;
    public string Preset { get; init; } = "Default";
    public string Background { get; init; } = "#10131A";
    public string Text { get; init; } = "#F3F6FB";
    public string Foreground { get; init; } = "#9AA6B8";
    public string Accent { get; init; } = "#62D6B5";

    public static AppearanceSettings Default { get; } = new();
}

/// <summary>A named bundle of mode + colors that fills the appearance pickers.</summary>
public sealed record AppearancePreset(string Name, AppearanceSettings Appearance);

/// <summary>
/// Built-in color presets. Each sets the color mode and the four editable colors;
/// the active theme (backdrop) is preserved when a preset is applied so picking a
/// palette never yanks the glass effect out from under the user.
/// </summary>
public static class AppearancePresets
{
    public static IReadOnlyList<AppearancePreset> All { get; } =
    [
        new("Default", new AppearanceSettings
        {
            Mode = ColorMode.Dark,
            Background = "#10131A",
            Text = "#F3F6FB",
            Foreground = "#9AA6B8",
            Accent = "#62D6B5"
        }),
        new("Light", new AppearanceSettings
        {
            Mode = ColorMode.Light,
            Background = "#F2F4F9",
            Text = "#14181F",
            Foreground = "#5C6678",
            Accent = "#0E9E78"
        }),
        new("Ocean", new AppearanceSettings
        {
            Mode = ColorMode.Dark,
            Background = "#0E1726",
            Text = "#EAF2FF",
            Foreground = "#8FA6C7",
            Accent = "#4FA8FF"
        }),
        new("Sunset", new AppearanceSettings
        {
            Mode = ColorMode.Dark,
            Background = "#1A1320",
            Text = "#FFF1EC",
            Foreground = "#C39AA6",
            Accent = "#FF8A5B"
        }),
        new("Forest", new AppearanceSettings
        {
            Mode = ColorMode.Dark,
            Background = "#101A14",
            Text = "#EEF7EF",
            Foreground = "#93B39A",
            Accent = "#6FC47F"
        }),
        new("Rose", new AppearanceSettings
        {
            Mode = ColorMode.Dark,
            Background = "#1A1016",
            Text = "#FFEEF6",
            Foreground = "#C79AB0",
            Accent = "#FF6FA5"
        }),
        new("Mono", new AppearanceSettings
        {
            Mode = ColorMode.Dark,
            Background = "#141414",
            Text = "#EDEDED",
            Foreground = "#9A9A9A",
            Accent = "#D7D7D7"
        }),
        new("Paper", new AppearanceSettings
        {
            Mode = ColorMode.Light,
            Background = "#FBF8F1",
            Text = "#2A2620",
            Foreground = "#7A7363",
            Accent = "#C2873A"
        })
    ];

    public static AppearancePreset? FindByName(string? name) =>
        string.IsNullOrWhiteSpace(name)
            ? null
            : All.FirstOrDefault(preset =>
                string.Equals(preset.Name, name, StringComparison.OrdinalIgnoreCase));
}

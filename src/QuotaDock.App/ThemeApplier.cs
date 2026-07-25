using System.Runtime.CompilerServices;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using QuotaDock.Core.Configuration;
using QuotaDock.Core.Presentation;
using Windows.UI;

namespace QuotaDock.App;

/// <summary>
/// Pushes the user's <see cref="AppearanceSettings"/> into the live UI. Colors
/// become <see cref="SolidColorBrush"/> resources (so XAML <c>ThemeResource</c>
/// references recolor instantly without a restart), the chosen color mode flips
/// each window's native WinUI chrome between light and dark, and the theme
/// selects the window backdrop (solid / Mica / frosted acrylic). All work is
/// guarded so a refresh that does not change the appearance never re-initializes
/// the backdrop, which would otherwise flash.
/// </summary>
internal static class ThemeApplier
{
    private static readonly object Gate = new();
    private static AppearanceSettings? appliedBrushes;
    private static readonly ConditionalWeakTable<Window, Box> windowThemes = new();
    private static readonly List<WeakReference<Window>> windows = [];

    /// <summary>Recolor the global brush resources. No-op when unchanged.</summary>
    public static void ApplyBrushes(AppearanceSettings appearance)
    {
        ArgumentNullException.ThrowIfNull(appearance);
        lock (Gate)
        {
            if (Equals(appliedBrushes, appearance))
            {
                return;
            }

            var theme = ThemePalette.Resolve(appearance);
            var resources = Application.Current.Resources;
            Set(resources, "QuotaDockCanvasColor", "QuotaDockCanvasBrush", theme.Canvas);
            Set(resources, "QuotaDockSurfaceColor", "QuotaDockSurfaceBrush", theme.Surface);
            Set(resources, "QuotaDockSurfaceRaisedColor", "QuotaDockSurfaceRaisedBrush", theme.SurfaceRaised);
            Set(resources, "QuotaDockTextColor", "QuotaDockTextBrush", theme.Text);
            Set(resources, "QuotaDockMutedColor", "QuotaDockMutedBrush", theme.Muted);
            Set(resources, "QuotaDockAccentColor", "QuotaDockAccentBrush", theme.Accent);
            Set(resources, "QuotaDockOnAccentColor", "QuotaDockOnAccentBrush", theme.OnAccent);
            Set(resources, "QuotaDockBorderColor", "QuotaDockBorderBrush", theme.Border);
            Set(resources, "QuotaDockTrackColor", "QuotaDockTrackBrush", theme.Track);
            appliedBrushes = appearance;
        }
    }

    /// <summary>Apply mode + backdrop to one window and remember it for broadcasts.</summary>
    public static void ApplyToWindow(Window window, AppearanceSettings appearance)
    {
        ArgumentNullException.ThrowIfNull(window);
        ArgumentNullException.ThrowIfNull(appearance);
        Register(window);
        SetRootTheme(window, appearance.Mode);
        SetBackdrop(window, appearance.Theme);
    }

    /// <summary>Re-apply mode + backdrop to every live window (live preview).</summary>
    public static void ApplyToAll(AppearanceSettings appearance)
    {
        ArgumentNullException.ThrowIfNull(appearance);
        lock (Gate)
        {
            for (var i = windows.Count - 1; i >= 0; i--)
            {
                if (windows[i].TryGetTarget(out var window))
                {
                    SetRootTheme(window, appearance.Mode);
                    SetBackdrop(window, appearance.Theme);
                }
                else
                {
                    windows.RemoveAt(i);
                }
            }
        }
    }

    private static void Register(Window window)
    {
        lock (Gate)
        {
            foreach (var reference in windows)
            {
                if (reference.TryGetTarget(out var existing) && ReferenceEquals(existing, window))
                {
                    return;
                }
            }

            windows.Add(new WeakReference<Window>(window));
        }
    }

    private static void SetRootTheme(Window window, ColorMode mode)
    {
        if (window.Content is FrameworkElement root)
        {
            root.RequestedTheme = mode == ColorMode.Dark ? ElementTheme.Dark : ElementTheme.Light;
        }
    }

    private static void SetBackdrop(Window window, ThemeKind theme)
    {
        var box = windowThemes.GetValue(window, static _ => new Box());
        if (box.Value == theme)
        {
            return;
        }

        box.Value = theme;
        try
        {
            window.SystemBackdrop = theme switch
            {
                ThemeKind.Glassy => new DesktopAcrylicBackdrop(),
                ThemeKind.Mica => new MicaBackdrop(),
                _ => null
            };
        }
        catch
        {
            // Backdrop materials require Windows 11; older builds keep a solid fill.
        }
    }

    private static void Set(ResourceDictionary resources, string colorKey, string brushKey, Argb color)
    {
        var winColor = ToColor(color);
        resources[colorKey] = winColor;

        // Mutate the existing shared brush in place instead of replacing it. Every
        // XAML binding (StaticResource and ThemeResource) and every code-built
        // control that captured this brush holds the same object, so changing its
        // Color — a dependency property — repaints them all at once, in every
        // window. Replacing the object would leave all of those pointing at the
        // stale brush, which is why recoloring used to look like it did nothing.
        if (resources[brushKey] is SolidColorBrush brush)
        {
            brush.Color = winColor;
        }
        else
        {
            resources[brushKey] = new SolidColorBrush(winColor);
        }
    }

    private static Color ToColor(Argb c) => Color.FromArgb(c.A, c.R, c.G, c.B);

    private sealed class Box
    {
        public ThemeKind Value;
    }
}

using QuotaDock.Core.Configuration;
using QuotaDock.Core.Presentation;

namespace QuotaDock.Core.Tests;

public sealed class ThemePaletteTests
{
    [Theory]
    [InlineData("#fff", 255, 255, 255, 255)]
    [InlineData("10221D", 255, 16, 34, 29)]
    [InlineData("#10221D", 255, 16, 34, 29)]
    [InlineData("#8010221D", 128, 16, 34, 29)]
    public void ParseHex_ReadsThreeSixAndEightDigitForms(string hex, byte a, byte r, byte g, byte b)
    {
        var color = ThemePalette.ParseHex(hex, new Argb(0, 0, 0, 0));

        Assert.Equal(new Argb(a, r, g, b), color);
    }

    [Fact]
    public void ParseHex_FallsBackOnGarbage()
    {
        var fallback = new Argb(255, 1, 2, 3);

        Assert.Equal(fallback, ThemePalette.ParseHex("nope", fallback));
        Assert.Equal(fallback, ThemePalette.ParseHex("#ZZZZZZ", fallback));
        Assert.Equal(fallback, ThemePalette.ParseHex(null, fallback));
    }

    [Fact]
    public void ToHex_RoundTripsRgb()
    {
        Assert.Equal("#10221D", ThemePalette.ToHex(new Argb(255, 16, 34, 29)));
    }

    [Fact]
    public void OnAccent_IsDarkOnLightAccentAndLightOnDarkAccent()
    {
        Assert.Equal(new Argb(255, 16, 34, 29), ThemePalette.OnAccent(new Argb(255, 255, 255, 255)));
        Assert.Equal(new Argb(255, 255, 255, 255), ThemePalette.OnAccent(new Argb(255, 16, 34, 29)));
    }

    [Fact]
    public void DeriveSurface_ShiftsAwayFromBackground()
    {
        var background = new Argb(255, 16, 19, 26);

        Assert.NotEqual(background, ThemePalette.DeriveSurface(background, ColorMode.Dark));
    }

    [Fact]
    public void Resolve_MakesDefaultOpaqueAndGlassTranslucent()
    {
        var settings = new AppearanceSettings { Background = "#10131A" };

        Assert.Equal(255, ThemePalette.Resolve(settings with { Theme = ThemeKind.Default }).Canvas.A);
        Assert.True(ThemePalette.Resolve(settings with { Theme = ThemeKind.Glassy }).Canvas.A < 255);
        Assert.True(ThemePalette.Resolve(settings with { Theme = ThemeKind.Mica }).Surface.A < 255);
    }

    [Fact]
    public void Resolve_PassesEditableColorsThrough()
    {
        var theme = ThemePalette.Resolve(new AppearanceSettings
        {
            Background = "#10131A",
            Text = "#F3F6FB",
            Foreground = "#9AA6B8",
            Accent = "#62D6B5"
        });

        Assert.Equal(new Argb(255, 243, 246, 251), theme.Text);
        Assert.Equal(new Argb(255, 98, 214, 181), theme.Accent);
    }
}

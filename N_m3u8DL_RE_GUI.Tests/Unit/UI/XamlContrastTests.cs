#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using Xunit;

namespace N_m3u8DL_RE_GUI.Tests.Unit.UI;

/// <summary>
/// Reads both theme palettes straight out of Themes/Theme.*.xaml and checks the pairs
/// that actually occur against WCAG 2.1. A colour edit that regresses contrast fails here.
/// </summary>
public class XamlContrastTests
{
    private static string ThemePath(string theme)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            var candidate = Path.Combine(dir.FullName, "N_m3u8DL_RE_GUI", "Themes", $"Theme.{theme}.xaml");
            if (File.Exists(candidate))
                return candidate;
            dir = dir.Parent;
        }
        throw new FileNotFoundException("Could not locate Theme." + theme + ".xaml from " + AppContext.BaseDirectory);
    }

    private static string AppXamlPath()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            var candidate = Path.Combine(dir.FullName, "N_m3u8DL_RE_GUI", "App.xaml");
            if (File.Exists(candidate))
                return candidate;
            dir = dir.Parent;
        }
        throw new FileNotFoundException("Could not locate App.xaml from " + AppContext.BaseDirectory);
    }

    /// <summary>Maps every x:Key'd SolidColorBrush to its hex value.</summary>
    private static Dictionary<string, string> Palette(string theme)
    {
        var text = File.ReadAllText(ThemePath(theme));
        var result = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (Match m in Regex.Matches(
                     text, @"<SolidColorBrush\s+x:Key=""(?<key>[^""]+)""\s+Color=""(?<color>#[0-9A-Fa-f]{6})""\s*/>"))
        {
            result[m.Groups["key"].Value] = m.Groups["color"].Value;
        }

        return result;
    }

    private static double Channel(int v)
    {
        var c = v / 255.0;
        return c <= 0.03928 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
    }

    private static double Luminance(string hex)
    {
        hex = hex.TrimStart('#');
        var r = int.Parse(hex[..2], NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        var g = int.Parse(hex.Substring(2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        var b = int.Parse(hex.Substring(4, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        return 0.2126 * Channel(r) + 0.7152 * Channel(g) + 0.0722 * Channel(b);
    }

    public static double Contrast(string a, string b)
    {
        double la = Luminance(a), lb = Luminance(b);
        var (hi, lo) = la > lb ? (la, lb) : (lb, la);
        return (hi + 0.05) / (lo + 0.05);
    }

    [Fact]
    public void ContrastFormula_ShouldMatchTheKnownReferenceValues()
    {
        Assert.Equal(21.00, Contrast("#FFFFFF", "#000000"), 2);
        Assert.Equal(1.00, Contrast("#123456", "#123456"), 2);
    }

    [Theory]
    [InlineData("Dark")]
    [InlineData("Light")]
    public void Palette_ShouldExposeEveryTokenTheseTestsReference(string theme)
    {
        var palette = Palette(theme);
        foreach (var key in new[]
                 {
                     "BgBrush", "SurfaceBrush", "CardBrush", "BorderBrushCustom",
                     "AccentBrush", "AccentTextBrush", "AccentHoverBrush", "AccentPressedBrush",
                     "TextPrimaryBrush", "TextSecondaryBrush", "CfAmberBrush",
                     "CommandBarBrush", "CommandTextBrush", "DropLabelBrush",
                     "SuccessBrush", "ErrorBrush", "CfWarningBgBrush", "DimBrush", "DisabledSurfaceBrush",
                     // The two tokens the contrast pass added: without them here, an edit
                     // that drops either one would only surface as a KeyNotFoundException
                     // inside the pair loops instead of a named failure.
                     "TintOnFillBrush", "FillTertiaryHoverBrush"
                 })
        {
            Assert.True(palette.ContainsKey(key), $"{theme} palette is missing {key}");
        }
    }

    [Fact]
    public void AppResources_ShouldMergeTheDefaultThemeDictionary()
    {
        var text = File.ReadAllText(AppXamlPath());
        Assert.Contains("Themes/Theme.Dark.xaml", text);
    }

    public static System.Collections.Generic.IEnumerable<object[]> ThemePair()
    {
        yield return new object[] { "Dark" };
        yield return new object[] { "Light" };
    }

    [Theory]
    [MemberData(nameof(ThemePair))]
    public void TextPairs_ShouldMeetWcagAaNormalText(string theme)
    {
        var palette = Palette(theme);
        foreach (var (fg, bg, usage) in new[]
                 {
                     ("TextSecondaryBrush", "CardBrush", "field labels"),
                     ("TextSecondaryBrush", "SurfaceBrush", "unselected tab text"),
                     // Secondary text also lands on the filled surfaces below. The pairs were
                     // unchecked when the iOS token set landed, and dark measured 4.07:1 on
                     // RaisedSurface / 4.40:1 on a filled field / 4.18:1 on a tertiary pill.
                     ("TextSecondaryBrush", "InputFillBrush", "input placeholder text"),
                     ("TextSecondaryBrush", "FillTertiaryBrush", "log toggle rest, secondary button rest"),
                     ("TextSecondaryBrush", "RaisedSurfaceBrush", "combo popup secondary text"),
                     // Accent used as TEXT on a filled row rather than as the focus ring.
                     ("TintOnFillBrush", "NavSelectedBrush", "selected sidebar / list / log toggle"),
                     // The hover step of a tertiary fill, which used to be BorderBrushCustom.
                     ("TextPrimaryBrush", "FillTertiaryHoverBrush", "secondary button hover"),
                     ("TextPrimaryBrush", "CardBrush", "input text and checkbox labels"),
                     ("AccentTextBrush", "CardBrush", "GroupBox headers, selected tab text"),
                     ("AccentTextBrush", "SurfaceBrush", "main title"),
                     ("CommandTextBrush", "CommandBarBrush", "command preview"),
                     ("CfAmberBrush", "CardBrush", "Cloudflare section"),
                     ("CfAmberBrush", "CfWarningBgBrush", "CF scope warning text"),
                     ("SuccessBrush", "CardBrush", "checked checkbox labels"),
                     ("ErrorBrush", "SurfaceBrush", "status bar errors")
                 })
        {
            var ratio = Contrast(palette[fg], palette[bg]);
            Assert.True(ratio >= 4.5, $"{theme}: {fg} on {bg} ({usage}) is {ratio:F2}:1, needs 4.5:1");
        }
    }

    [Theory]
    [MemberData(nameof(ThemePair))]
    public void TertiaryLabel_ShouldStayAboveTheDisabledStateFloor(string theme)
    {
        // TertiaryLabelBrush is a disabled-only token (disabled ComboBox text and its
        // fill). WCAG 2.1 exempts inactive controls from the 4.5:1 body-text rule, so the
        // bar here is the 3:1 component floor, not 4.5:1 — a disabled label that reads as
        // clearly as live text defeats its own purpose. It must still be legible though:
        // dark was 2.47:1 on Card and light 2.27:1 on a filled field, which is where the
        // "grey text over the fill" complaint came from.
        var palette = Palette(theme);
        foreach (var (fg, bg, usage) in new[]
                 {
                     ("TertiaryLabelBrush", "CardBrush", "disabled combo over a card"),
                     ("TertiaryLabelBrush", "RaisedSurfaceBrush", "disabled combo in a popup"),
                     ("TertiaryLabelBrush", "InputFillBrush", "disabled field over a filled input"),
                     ("TertiaryLabelBrush", "BgBrush", "disabled combo over a sidebar")
                 })
        {
            var ratio = Contrast(palette[fg], palette[bg]);
            Assert.True(ratio >= 3.0, $"{theme}: {fg} on {bg} ({usage}) is {ratio:F2}:1, needs the 3.0:1 disabled floor");
        }
    }

    [Theory]
    [MemberData(nameof(ThemePair))]
    public void NonTextPairs_ShouldMeetWcagAaUiBoundaries(string theme)
    {
        var palette = Palette(theme);
        foreach (var (fg, bg, usage) in new[]
                 {
                     ("BorderBrushCustom", "CardBrush", "textbox and GroupBox borders"),
                     ("BorderBrushCustom", "SurfaceBrush", "Zone A and Zone D borders"),
                     ("BorderBrushCustom", "BgBrush", "secondary button border"),
                     ("AccentBrush", "CardBrush", "focused textbox border")
                 })
        {
            var ratio = Contrast(palette[fg], palette[bg]);
            Assert.True(ratio >= 3.0, $"{theme}: {fg} on {bg} ({usage}) is {ratio:F2}:1, needs 3.0:1");
        }
    }

    [Theory]
    // White label on a coloured button fill, at every interaction state.
    [InlineData("#5865F2", "Download button, rest (dark)")]
    [InlineData("#4350D8", "Download button, hover (dark) / rest (light)")]
    [InlineData("#3E4ACB", "Download button, pressed (dark) / hover (light)")]
    [InlineData("#3743BE", "Download button, hover (light)")]
    [InlineData("#303BB0", "Download button, pressed (light)")]
    [InlineData("#C0392B", "Stop button")]
    [InlineData("#1E8449", "update pill, rest")]
    [InlineData("#196F3D", "update pill, hover")]
    [InlineData("#145A32", "update pill, pressed")]
    public void WhiteOnButtonFills_ShouldMeetWcagAa(string fill, string usage)
    {
        var ratio = Contrast("#FFFFFF", fill);

        Assert.True(ratio >= 4.5, $"White on {fill} ({usage}) is {ratio:F2}:1, needs 4.5:1");
    }

    [Fact]
    public void InteractionStates_ShouldNeverReduceContrast()
    {
        // Hover and pressed must darken, not lighten. The original ramps got lighter and
        // lost contrast exactly when the user was reaching for the control.
        Assert.True(Contrast("#FFFFFF", "#4350D8") > Contrast("#FFFFFF", "#5865F2"),
            "Dark hover must not be lower-contrast than its resting state");
        Assert.True(Contrast("#FFFFFF", "#3743BE") > Contrast("#FFFFFF", "#4350D8"),
            "Light hover must not be lower-contrast than its resting state");
        Assert.True(Contrast("#FFFFFF", "#196F3D") > Contrast("#FFFFFF", "#1E8449"),
            "Update pill hover must not be lower-contrast than its resting state");
    }

    [Theory]
    [InlineData("Dark")]
    [InlineData("Light")]
    public void DropLabels_ShouldMeetWcagAaAgainstTheCardBackground(string theme)
    {
        var palette = Palette(theme);
        Assert.True(Contrast(palette["DropLabelBrush"], palette["CardBrush"]) >= 4.5,
            $"{theme}: DropLabel on Card needs 4.5:1");
    }

    [Fact]
    public void MainWindow_ShouldNotCarryInlineBrushDefinitionsOrDarkHardcodes()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "N_m3u8DL_RE_GUI", "MainWindow.xaml")))
            dir = dir.Parent;
        var path = dir == null
            ? throw new FileNotFoundException("Could not locate MainWindow.xaml")
            : Path.Combine(dir.FullName, "N_m3u8DL_RE_GUI", "MainWindow.xaml");
        var text = File.ReadAllText(path);

        // Palette moved to Themes/*; inline definitions would silently pin one theme.
        Assert.DoesNotContain("<SolidColorBrush x:Key=", text);

        // The three "Drop *" labels are coloured via the DropLabelBrush token, not inline.
        Assert.DoesNotContain("Foreground=\"#E74C3C\"", text);
    }
}

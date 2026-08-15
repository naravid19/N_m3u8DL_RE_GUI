#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using Xunit;

namespace N_m3u8DL_RE_GUI.Tests.Unit.UI;

/// <summary>
/// Reads the palette straight out of MainWindow.xaml and checks the pairs that actually
/// occur against WCAG 2.1. A colour edit that regresses contrast fails here.
/// </summary>
public class XamlContrastTests
{
    private static string XamlPath()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            var candidate = Path.Combine(dir.FullName, "N_m3u8DL_RE_GUI", "MainWindow.xaml");
            if (File.Exists(candidate))
                return candidate;
            dir = dir.Parent;
        }
        throw new FileNotFoundException("Could not locate MainWindow.xaml from " + AppContext.BaseDirectory);
    }

    /// <summary>Maps every x:Key'd SolidColorBrush to its hex value.</summary>
    private static Dictionary<string, string> Palette()
    {
        var text = File.ReadAllText(XamlPath());
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

    [Fact]
    public void Palette_ShouldExposeEveryTokenTheseTestsReference()
    {
        var palette = Palette();
        foreach (var key in new[]
                 {
                     "BgDarkBrush", "SurfaceBrush", "CardBrush", "BorderBrushCustom",
                     "AccentBrush", "AccentTextBrush", "AccentHoverBrush", "AccentPressedBrush",
                     "TextPrimaryBrush", "TextSecondaryBrush", "CfAmberBrush",
                     "CommandBarBrush", "CommandTextBrush"
                 })
        {
            Assert.True(palette.ContainsKey(key), $"Palette is missing {key}");
        }
    }

    [Theory]
    // foreground token, background token, minimum ratio, where it is used
    [InlineData("TextSecondaryBrush", "CardBrush", 4.5, "field labels")]
    [InlineData("TextSecondaryBrush", "SurfaceBrush", 4.5, "unselected tab text")]
    [InlineData("TextPrimaryBrush", "CardBrush", 4.5, "input text and checkbox labels")]
    [InlineData("AccentTextBrush", "CardBrush", 4.5, "GroupBox headers, selected tab text")]
    [InlineData("AccentTextBrush", "SurfaceBrush", 4.5, "main title")]
    [InlineData("CommandTextBrush", "CommandBarBrush", 4.5, "command preview")]
    [InlineData("CfAmberBrush", "CardBrush", 4.5, "Cloudflare section")]
    public void TextPairs_ShouldMeetWcagAaNormalText(string fg, string bg, double minimum, string usage)
    {
        var palette = Palette();
        var ratio = Contrast(palette[fg], palette[bg]);

        Assert.True(ratio >= minimum, $"{fg} on {bg} ({usage}) is {ratio:F2}:1, needs {minimum}:1");
    }

    [Theory]
    [InlineData("BorderBrushCustom", "CardBrush", 3.0, "textbox and GroupBox borders")]
    [InlineData("BorderBrushCustom", "SurfaceBrush", 3.0, "Zone A and Zone D borders")]
    [InlineData("BorderBrushCustom", "BgDarkBrush", 3.0, "secondary button border")]
    [InlineData("AccentBrush", "CardBrush", 3.0, "focused textbox border")]
    public void NonTextPairs_ShouldMeetWcagAaUiBoundaries(string fg, string bg, double minimum, string usage)
    {
        var palette = Palette();
        var ratio = Contrast(palette[fg], palette[bg]);

        Assert.True(ratio >= minimum, $"{fg} on {bg} ({usage}) is {ratio:F2}:1, needs {minimum}:1");
    }

    [Theory]
    // White label on a coloured button fill, at every interaction state.
    [InlineData("#5865F2", "Download button, rest")]
    [InlineData("#4350D8", "Download button, hover")]
    [InlineData("#3E4ACB", "Download button, pressed")]
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
            "Download hover must not be lower-contrast than its resting state");
        Assert.True(Contrast("#FFFFFF", "#196F3D") > Contrast("#FFFFFF", "#1E8449"),
            "Update pill hover must not be lower-contrast than its resting state");
    }

    [Fact]
    public void DropLabels_ShouldMeetWcagAaAgainstTheCardBackground()
    {
        var palette = Palette();
        var text = File.ReadAllText(XamlPath());

        // The three "Drop *" labels are coloured inline rather than via a token.
        Assert.DoesNotContain("Foreground=\"#E74C3C\"", text);
        Assert.True(Contrast("#EC7063", palette["CardBrush"]) >= 4.5);
    }
}

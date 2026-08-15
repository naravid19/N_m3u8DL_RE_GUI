#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Xunit;

namespace N_m3u8DL_RE_GUI.Tests.Unit.UI;

/// <summary>
/// Structural accessibility invariants for MainWindow.xaml, asserted by parsing the file
/// as XML. These run in the normal suite and fail loudly when a control is added without
/// an accessible name, so the 78-of-86 gap the 2026-08-14 audit found cannot come back.
///
/// Earlier revisions of this file checked <c>Assert.Contains("AutomationProperties.Name", text)</c>
/// against the whole document, which passed as soon as any single control had a name.
/// Every assertion here is scoped to the element it is about.
/// </summary>
public class XamlAccessibilityTests
{
    private static readonly XNamespace X = "http://schemas.microsoft.com/winfx/2006/xaml";

    /// <summary>Elements a keyboard or screen-reader user can land on.</summary>
    private static readonly string[] InteractiveElements =
        { "TextBox", "CheckBox", "ComboBox", "Button", "ToggleButton", "TabItem", "ProgressBar" };

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

    private static string XamlText() => File.ReadAllText(XamlPath());

    /// <summary>
    /// Interactive elements in the visual tree. Anything inside Window.Resources is a
    /// style or template prototype, not a control the user can reach.
    /// </summary>
    private static List<XElement> VisualTreeControls()
    {
        var root = XDocument.Load(XamlPath()).Root!;
        var resources = root.Elements().FirstOrDefault(e => e.Name.LocalName == "Window.Resources");

        return root.Descendants()
            .Where(e => InteractiveElements.Contains(e.Name.LocalName))
            .Where(e => resources == null || !e.Ancestors().Contains(resources))
            .ToList();
    }

    private static bool HasAccessibleName(XElement e) =>
        e.Attribute("AutomationProperties.Name") != null ||
        e.Attribute("AutomationProperties.LabeledBy") != null;

    private static string Describe(XElement e)
    {
        var name = (string?)e.Attribute(X + "Name");
        var content = (string?)e.Attribute("Content") ?? (string?)e.Attribute("Header");
        return name ?? $"<{e.Name.LocalName} {content ?? "unnamed"}>";
    }

    [Fact]
    public void EveryInteractiveControl_ShouldHaveAnAccessibleName()
    {
        var controls = VisualTreeControls();
        Assert.True(controls.Count > 50, $"Only found {controls.Count} controls — the parser is probably broken");

        var missing = controls.Where(e => !HasAccessibleName(e)).Select(Describe).ToList();

        Assert.True(
            missing.Count == 0,
            $"{missing.Count} of {controls.Count} interactive controls have no AutomationProperties.Name:\n  "
            + string.Join("\n  ", missing));
    }

    [Fact]
    public void EveryTabItem_ShouldHaveAnAccessibleNameSoScreenReadersDoNotReadEmojiNames()
    {
        // Without a name, NVDA announces "package Download", "satellite antenna Live".
        var tabs = VisualTreeControls().Where(e => e.Name.LocalName == "TabItem").ToList();

        Assert.Equal(6, tabs.Count);
        Assert.All(tabs, t => Assert.True(HasAccessibleName(t), $"TabItem {Describe(t)} has no accessible name"));
    }

    [Fact]
    public void EveryInteractiveStyle_ShouldSetAVisibleFocusVisual()
    {
        // WPF's default focus visual is a black dotted rectangle, invisible on this palette.
        var text = XamlText();
        Assert.Contains("x:Key=\"AccessibleFocusVisual\"", text);

        foreach (var key in new[]
                 {
                     "TextBoxStyle", "CheckBoxStyle", "ButtonStyle",
                     "SecondaryButtonStyle", "UpdatePillButtonStyle"
                 })
        {
            var start = text.IndexOf($"x:Key=\"{key}\"", StringComparison.Ordinal);
            Assert.True(start >= 0, $"Style {key} not found in MainWindow.xaml");

            var end = text.IndexOf("</Style>", start, StringComparison.Ordinal);
            Assert.True(end > start, $"Style {key} is not closed");

            Assert.True(
                text[start..end].Contains("FocusVisualStyle", StringComparison.Ordinal),
                $"Style {key} does not set FocusVisualStyle, so focus is invisible on it");
        }
    }

    [Fact]
    public void Window_ShouldBindStartAndStopToItsOwnRoutedCommands()
    {
        var text = XamlText();

        Assert.Contains("<Window.InputBindings>", text);
        Assert.Contains("local:MainWindow.StartDownloadRoutedCommand", text);
        Assert.Contains("local:MainWindow.StopDownloadRoutedCommand", text);

        // MainViewModel is the DataContext but nothing populates its DownloadOptions, so a
        // shortcut aimed at its commands always reports "no URL". Guard against the regression.
        Assert.DoesNotContain("Command=\"{Binding StartDownloadCommand", text);
        Assert.DoesNotContain("Command=\"{Binding StopDownloadCommand", text);
    }

    [Theory]
    [InlineData("Alt+S")]
    [InlineData("Escape")]
    public void EveryAdvertisedShortcut_ShouldActuallyBeBound(string gesture)
    {
        var text = XamlText();

        // If any tooltip mentions the gesture, a KeyBinding for it has to exist.
        if (text.Contains(gesture, StringComparison.Ordinal))
            Assert.Contains($"Gesture=\"{gesture}\"", text);
    }

    [Fact]
    public void AccessKeys_ShouldNotCollideOnTheTwoPrimaryButtons()
    {
        var text = XamlText();

        // "_GO" gives Alt+G, "S_top" gives Alt+T. If both used the same letter, whichever
        // control was visible would swallow the other's shortcut.
        Assert.Contains("Content=\"▶ _GO\"", text);
        Assert.Contains("Content=\"⏹ S_top\"", text);
    }

    [Fact]
    public void GroupBoxHeaders_ShouldEscapeUnderscoresTheAccessKeyParserWouldEat()
    {
        // The GroupBox header ContentPresenter sets RecognizesAccessKey="True", so a lone
        // '_' is consumed as a mnemonic prefix: "curl_cffi" rendered as "curlcffi".
        var offenders = XDocument.Load(XamlPath()).Descendants()
            .Where(e => e.Name.LocalName == "GroupBox")
            .Select(e => (string?)e.Attribute("Header"))
            .Where(h => h != null && SingleUnderscoreCount(h!) > 0)
            .ToList();

        Assert.True(
            offenders.Count == 0,
            "GroupBox headers with an unescaped underscore (WPF will swallow it): "
            + string.Join(", ", offenders));
    }

    private static int SingleUnderscoreCount(string value)
    {
        var count = 0;
        for (var i = 0; i < value.Length; i++)
        {
            if (value[i] != '_') continue;
            if (i + 1 < value.Length && value[i + 1] == '_')
            {
                i++;        // escaped pair, skip both
                continue;
            }
            count++;
        }
        return count;
    }

    [Fact]
    public void ValidationState_ShouldTravelAsATagNotALocalBorderBrush()
    {
        // A local BorderBrush value outranks the style's IsFocused trigger in WPF property
        // precedence, which silently removed the focus ring from TextBox_URL at startup.
        var text = XamlText();

        Assert.Contains("Property=\"Tag\" Value=\"invalid\"", text);
    }
}

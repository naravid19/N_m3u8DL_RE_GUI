#nullable enable
using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace N_m3u8DL_RE_GUI.Tests.Unit.Services;

/// <summary>
/// Guard test ensuring that every ToolTip attribute in MainWindow.xaml is bound
/// to a localized DynamicResource rather than hardcoded in English.
///
/// The dictionary tests (<see cref="LanguageDictionaryTests"/>) compare the three
/// Strings.*.xaml files to each other, and the parity test checks that referenced
/// keys exist. Neither can see a string that was never turned into a key at all —
/// which is exactly how 40 of 41 tooltips shipped in English inside a "fully
/// localized" UI. This test closes that gap.
/// </summary>
public class LocalizationCoverageTests
{
    private static string MainWindowXamlPath()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "N_m3u8DL_RE_GUI.sln")))
        {
            dir = dir.Parent;
        }

        if (dir == null)
            throw new DirectoryNotFoundException("Could not locate solution root from " + AppContext.BaseDirectory);

        var xamlPath = Path.Combine(dir.FullName, "N_m3u8DL_RE_GUI", "MainWindow.xaml");
        if (!File.Exists(xamlPath))
            throw new FileNotFoundException("MainWindow.xaml not found at " + xamlPath);

        return xamlPath;
    }

    [Fact]
    public void EveryToolTipIsLocalized()
    {
        // The dictionary tests compare the three Strings.*.xaml files to each other,
        // and the usage-parity test checks that referenced keys exist. Neither can
        // see a string that was never turned into a key at all -- which is exactly
        // how 40 of 41 tooltips shipped in English inside a "fully localized" UI.
        var xaml = File.ReadAllText(MainWindowXamlPath());

        var hardcoded = Regex.Matches(xaml, @"ToolTip=""(?!\{)([^""]*)""")
            .Select(m => m.Groups[1].Value)
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .ToList();

        Assert.True(hardcoded.Count == 0,
            $"{hardcoded.Count} tooltip(s) are hardcoded rather than bound to a localized resource: {string.Join(" | ", hardcoded.Take(10))}");
    }
}

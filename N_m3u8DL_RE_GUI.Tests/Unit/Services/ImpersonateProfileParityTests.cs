#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace N_m3u8DL_RE_GUI.Tests.Unit.Services;

/// <summary>
/// Guard tests asserting that the four independent definitions of Cloudflare impersonate
/// profiles stay in sync:
/// 1. extension/lib/impersonate-profiles.js (browser extension detection)
/// 2. N_m3u8DL_RE_GUI.Core/Capture/CaptureApplyPlan.cs (C# capture plan validator)
/// 3. N_m3u8DL_RE_GUI/MainWindow.xaml (WPF GUI dropdown items)
/// 4. m3u8_cf_bypass.py (Python Cloudflare bypass CLI help)
/// </summary>
public class ImpersonateProfileParityTests
{
    private static string GetRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "N_m3u8DL_RE_GUI.sln")))
        {
            dir = dir.Parent;
        }

        if (dir == null)
            throw new DirectoryNotFoundException("Could not locate solution root from " + AppContext.BaseDirectory);

        return dir.FullName;
    }

    [Fact]
    public void FourWayImpersonateProfiles_ShouldHaveIdenticalSets()
    {
        var root = GetRepoRoot();

        // 1. Extension profiles
        var extFile = Path.Combine(root, "extension", "lib", "impersonate-profiles.js");
        Assert.True(File.Exists(extFile), $"Extension profiles file not found: {extFile}");
        var extContent = File.ReadAllText(extFile);
        var extProfiles = Regex.Matches(extContent, @"impersonate:\s*'([^']+)'")
            .Select(m => m.Groups[1].Value.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        // 2. C# Core KnownImpersonates
        var coreFile = Path.Combine(root, "N_m3u8DL_RE_GUI.Core", "Capture", "CaptureApplyPlan.cs");
        Assert.True(File.Exists(coreFile), $"CaptureApplyPlan file not found: {coreFile}");
        var coreContent = File.ReadAllText(coreFile);
        var coreBlockMatch = Regex.Match(coreContent, @"KnownImpersonates\s*=\s*new[^{]*\{([^}]+)\}");
        Assert.True(coreBlockMatch.Success, "Could not locate KnownImpersonates block in CaptureApplyPlan.cs");
        var coreProfiles = Regex.Matches(coreBlockMatch.Groups[1].Value, @"""([^""]+)""")
            .Select(m => m.Groups[1].Value.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        // 3. MainWindow.xaml ComboBoxItem Tag values
        var xamlFile = Path.Combine(root, "N_m3u8DL_RE_GUI", "MainWindow.xaml");
        Assert.True(File.Exists(xamlFile), $"MainWindow.xaml not found: {xamlFile}");
        var xamlContent = File.ReadAllText(xamlFile);
        var comboMatch = Regex.Match(xamlContent, @"<ComboBox[^>]*Name=""Combo_CFImpersonate""[^>]*>([\s\S]*?)</ComboBox>");
        Assert.True(comboMatch.Success, "Could not locate Combo_CFImpersonate in MainWindow.xaml");
        var xamlProfiles = Regex.Matches(comboMatch.Groups[1].Value, @"Tag=""([^""]+)""")
            .Select(m => m.Groups[1].Value.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        // 4. m3u8_cf_bypass.py --impersonate help text
        var pyFile = Path.Combine(root, "m3u8_cf_bypass.py");
        Assert.True(File.Exists(pyFile), $"m3u8_cf_bypass.py not found: {pyFile}");
        var pyContent = File.ReadAllText(pyFile);
        var pyHelpMatch = Regex.Match(pyContent, @"Browser fingerprint:\s*([^""\r\n]+)");
        Assert.True(pyHelpMatch.Success, "Could not locate 'Browser fingerprint:' help text in m3u8_cf_bypass.py");
        var pyProfiles = pyHelpMatch.Groups[1].Value
            .Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        // Assert all sources found at least one profile
        Assert.True(extProfiles.Count > 0, "No profiles found in extension");
        Assert.True(coreProfiles.Count > 0, "No profiles found in C# Core");
        Assert.True(xamlProfiles.Count > 0, "No profiles found in MainWindow.xaml");
        Assert.True(pyProfiles.Count > 0, "No profiles found in m3u8_cf_bypass.py");

        // Assert 4-way equality and name missing / extra values if any diverge
        AssertProfilesEqual("extension", extProfiles, "C# Core", coreProfiles);
        AssertProfilesEqual("extension", extProfiles, "MainWindow.xaml", xamlProfiles);
        AssertProfilesEqual("extension", extProfiles, "m3u8_cf_bypass.py", pyProfiles);
    }

    private static void AssertProfilesEqual(string nameA, HashSet<string> setA, string nameB, HashSet<string> setB)
    {
        var missingInB = setA.Except(setB, StringComparer.OrdinalIgnoreCase).ToList();
        var extraInB = setB.Except(setA, StringComparer.OrdinalIgnoreCase).ToList();

        var errors = new List<string>();
        if (missingInB.Count > 0)
        {
            errors.Add($"{nameB} is missing: [{string.Join(", ", missingInB)}]");
        }
        if (extraInB.Count > 0)
        {
            errors.Add($"{nameB} has unexpected extra: [{string.Join(", ", extraInB)}]");
        }

        Assert.True(errors.Count == 0,
            $"Parity mismatch between {nameA} and {nameB}: {string.Join("; ", errors)}");
    }
}

#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using N_m3u8DL_RE_GUI.Tests.Fixtures;
using Xunit;

namespace N_m3u8DL_RE_GUI.Tests.Unit.Services;

/// <summary>
/// LanguageDictionaryTests proves the three Strings.*.xaml files agree with
/// each other. It says nothing about whether a key referenced in the actual
/// UI exists in any of them. A typo in a new {DynamicResource Str_X} binding,
/// or a rename that missed one call site, renders as a silently blank label
/// at runtime -- WPF's DynamicResource has no fallback -- and every dictionary
/// test still passes. This is the guard for that gap.
/// </summary>
public class LanguageUsageParityTests
{
    private static readonly XNamespace XNamespace = "http://schemas.microsoft.com/winfx/2006/xaml";

    // Three independent ways this codebase binds a Str_* key: a XAML markup
    // extension, a code-behind lookup with a fallback, and a code-behind
    // resource binding with no fallback. A checker that recognises only the
    // first two has the same blind spot a first pass at this test did: it
    // flagged Str_Working as unused because it is set only via
    // SetResourceReference, not DynamicResource or GetString.
    private static readonly Regex DynamicResourcePattern =
        new(@"DynamicResource\s+(?:ResourceKey=)?(Str_[A-Za-z0-9_]+)", RegexOptions.Compiled);
    private static readonly Regex GetStringPattern =
        new(@"(?:GetString|Localize)\(\s*""(Str_[A-Za-z0-9_]+)""", RegexOptions.Compiled);
    private static readonly Regex SetResourceReferencePattern =
        new(@"SetResourceReference\([^,]+,\s*""(Str_[A-Za-z0-9_]+)""", RegexOptions.Compiled);

    private static HashSet<string> ExtractKeys(string source, Regex pattern)
    {
        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (Match m in pattern.Matches(source))
        {
            keys.Add(m.Groups[1].Value);
        }
        return keys;
    }

    [Fact]
    public void EveryReferencedKeyExistsInTheMasterDictionary()
    {
        var xaml = ProjectSource.ReadFile("MainWindow.xaml");
        var codeBehind = ProjectSource.ReadFile("MainWindow.xaml.cs");
        var pickerWindows = ProjectSource.ReadFile(System.IO.Path.Combine("Views", "AbyssQualityWindow.xaml"));
        var masterDoc = XDocument.Parse(ProjectSource.ReadFile(System.IO.Path.Combine("Resources", "Languages", "Strings.en-US.xaml")));

        var defined = masterDoc.Root!.Elements()
            .Select(e => e.Attribute(XNamespace + "Key")?.Value)
            .Where(k => !string.IsNullOrEmpty(k))
            .ToHashSet(StringComparer.Ordinal);

        var referenced = new HashSet<string>(StringComparer.Ordinal);
        referenced.UnionWith(ExtractKeys(xaml, DynamicResourcePattern));
        referenced.UnionWith(ExtractKeys(pickerWindows, DynamicResourcePattern));
        referenced.UnionWith(ExtractKeys(codeBehind, DynamicResourcePattern));
        referenced.UnionWith(ExtractKeys(codeBehind, GetStringPattern));
        referenced.UnionWith(ExtractKeys(codeBehind, SetResourceReferencePattern));

        // A sanity floor on the extraction itself: if this collapses to a
        // handful of keys, the regexes stopped matching the real file shape
        // rather than the UI having lost most of its localized text.
        Assert.True(referenced.Count > 100,
            $"only extracted {referenced.Count} Str_* references -- the extraction patterns may no longer match MainWindow.xaml/.xaml.cs");

        var missing = referenced.Except(defined).OrderBy(k => k, StringComparer.Ordinal).ToList();
        Assert.True(missing.Count == 0,
            $"{missing.Count} key(s) referenced in MainWindow.xaml/.xaml.cs are not defined in Strings.en-US.xaml: {string.Join(", ", missing)}");
    }
}

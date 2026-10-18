#nullable enable
using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using N_m3u8DL_RE_GUI.Tests.Fixtures;
using Xunit;

namespace N_m3u8DL_RE_GUI.Tests.Unit.Services;

/// <summary>
/// Tests validating that all WPF localization ResourceDictionaries (en-US, zh-CN, zh-TW)
/// exist, parse cleanly as XML/XAML, and maintain 100% key parity with no missing or orphaned keys.
/// </summary>
public class LanguageDictionaryTests
{
    private static readonly string[] RequiredLanguageCodes = ["en-US", "zh-CN", "zh-TW"];
    private static readonly XNamespace XNamespace = "http://schemas.microsoft.com/winfx/2006/xaml";

    private static string GetLanguagesDirectory()
    {
        var langDir = Path.Combine(ProjectSource.SolutionRoot(), "N_m3u8DL_RE_GUI", "Resources", "Languages");
        if (!Directory.Exists(langDir))
            throw new DirectoryNotFoundException($"Languages directory not found at '{langDir}'");

        return langDir;
    }

    [Theory]
    [InlineData("en-US")]
    [InlineData("zh-CN")]
    [InlineData("zh-TW")]
    public void LanguageDictionaryFile_ShouldExistAndBeValidXml(string cultureCode)
    {
        var langDir = GetLanguagesDirectory();
        var filePath = Path.Combine(langDir, $"Strings.{cultureCode}.xaml");

        Assert.True(File.Exists(filePath), $"ResourceDictionary file missing: {filePath}");

        var content = File.ReadAllText(filePath);
        Assert.False(string.IsNullOrWhiteSpace(content), $"File is empty: {filePath}");

        var doc = XDocument.Parse(content);
        Assert.NotNull(doc.Root);
        Assert.Equal("ResourceDictionary", doc.Root.Name.LocalName);
    }

    [Fact]
    public void MasterDictionary_EnUs_ShouldContainExpectedKeyCount()
    {
        var langDir = GetLanguagesDirectory();
        var masterPath = Path.Combine(langDir, "Strings.en-US.xaml");
        var doc = XDocument.Parse(File.ReadAllText(masterPath));

        var keys = doc.Root!.Elements()
            .Select(e => e.Attribute(XNamespace + "Key")?.Value)
            .Where(k => !string.IsNullOrEmpty(k))
            .ToList();

        // Must contain at least all core localization keys
        Assert.True(keys.Count >= 50, $"Expected at least 50 localization keys, but found {keys.Count}");
        Assert.Contains("Str_AppTitle", keys);
        Assert.Contains("Str_GuiLanguage", keys);
        Assert.Contains("Str_CliLanguage", keys);
        Assert.Contains("Str_Download", keys);
        Assert.Contains("Str_Stop", keys);
    }

    [Theory]
    [InlineData("zh-CN")]
    [InlineData("zh-TW")]
    public void LocalizedDictionaries_ShouldHave100PercentKeyParityWithMaster(string cultureCode)
    {
        var langDir = GetLanguagesDirectory();
        var masterPath = Path.Combine(langDir, "Strings.en-US.xaml");
        var targetPath = Path.Combine(langDir, $"Strings.{cultureCode}.xaml");

        var masterDoc = XDocument.Parse(File.ReadAllText(masterPath));
        var targetDoc = XDocument.Parse(File.ReadAllText(targetPath));

        var masterKeys = masterDoc.Root!.Elements()
            .Select(e => e.Attribute(XNamespace + "Key")?.Value)
            .Where(k => !string.IsNullOrEmpty(k))
            .ToHashSet();

        var targetKeys = targetDoc.Root!.Elements()
            .Select(e => e.Attribute(XNamespace + "Key")?.Value)
            .Where(k => !string.IsNullOrEmpty(k))
            .ToHashSet();

        // Find missing keys
        var missingInTarget = masterKeys.Except(targetKeys).ToList();
        Assert.True(
            missingInTarget.Count == 0,
            $"Strings.{cultureCode}.xaml is missing {missingInTarget.Count} keys from en-US: {string.Join(", ", missingInTarget)}");

        // Find extra orphaned keys
        var extraInTarget = targetKeys.Except(masterKeys).ToList();
        Assert.True(
            extraInTarget.Count == 0,
            $"Strings.{cultureCode}.xaml has {extraInTarget.Count} orphaned keys not in en-US: {string.Join(", ", extraInTarget)}");

        // Key counts must match identically
        Assert.Equal(masterKeys.Count, targetKeys.Count);
    }

    [Theory]
    [InlineData("en-US")]
    [InlineData("zh-CN")]
    [InlineData("zh-TW")]
    public void AllEntries_ShouldHaveNonEmptyStringValues(string cultureCode)
    {
        var langDir = GetLanguagesDirectory();
        var filePath = Path.Combine(langDir, $"Strings.{cultureCode}.xaml");
        var doc = XDocument.Parse(File.ReadAllText(filePath));

        foreach (var element in doc.Root!.Elements())
        {
            var key = element.Attribute(XNamespace + "Key")?.Value;
            Assert.False(string.IsNullOrEmpty(key), "Found element with missing or empty x:Key");
            Assert.False(string.IsNullOrWhiteSpace(element.Value), $"Key '{key}' in Strings.{cultureCode}.xaml has empty value");
        }
    }
}

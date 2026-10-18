#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Xml.Linq;
using WpfApplication = System.Windows.Application;

namespace N_m3u8DL_RE_GUI.Services;

/// <summary>
/// Service providing dynamic WPF ResourceDictionary swapping for live UI language changes,
/// with in-memory fallback for test and headless environments.
/// </summary>
public class LanguageService : ILanguageService
{
    private static readonly XNamespace XamlNamespace = "http://schemas.microsoft.com/winfx/2006/xaml";

    private static readonly List<LanguageInfo> _supportedLanguages =
    [
        new("en-US", "English", "English"),
        new("zh-CN", "Simplified Chinese", "简体中文"),
        new("zh-TW", "Traditional Chinese", "繁體中文")
    ];

    private volatile IReadOnlyDictionary<string, string> _inMemoryStrings = new Dictionary<string, string>(StringComparer.Ordinal);
    private string _currentLanguage = "en-US";

    /// <summary>
    /// Static default singleton instance of <see cref="ILanguageService"/>.
    /// </summary>
    public static LanguageService Default { get; } = new();

    /// <inheritdoc/>
    public IReadOnlyList<LanguageInfo> SupportedLanguages => _supportedLanguages.AsReadOnly();

    /// <inheritdoc/>
    public string CurrentLanguage => _currentLanguage;

    /// <inheritdoc/>
    public event EventHandler? LanguageChanged;

    public LanguageService()
    {
        SetLanguageInternal("en-US", raiseEvent: false);
    }

    /// <inheritdoc/>
    public void SetLanguage(string cultureCode)
    {
        SetLanguageInternal(cultureCode, raiseEvent: true);
    }

    /// <inheritdoc/>
    public string GetString(string key, string? fallback = null)
    {
        if (string.IsNullOrEmpty(key))
            return fallback ?? string.Empty;

        // 1. Try resolving from active WPF Application Resources (if on UI thread or safe)
        if (WpfApplication.Current != null)
        {
            try
            {
                if (WpfApplication.Current.Resources.Contains(key))
                {
                    var val = WpfApplication.Current.Resources[key]?.ToString();
                    if (!string.IsNullOrEmpty(val))
                        return val;
                }
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"Failed to read key '{key}' from Application.Current.Resources: {ex.Message}");
            }
        }

        // 2. Try resolving from immutable in-memory cached dictionary (thread-safe)
        if (_inMemoryStrings.TryGetValue(key, out var inMemVal))
        {
            return inMemVal;
        }

        // 3. Return fallback or the key itself
        return fallback ?? key;
    }

    private void SetLanguageInternal(string cultureCode, bool raiseEvent)
    {
        var matched = _supportedLanguages.FirstOrDefault(l =>
            string.Equals(l.Code, cultureCode, StringComparison.OrdinalIgnoreCase));

        _currentLanguage = matched?.Code ?? "en-US";

        // Load strings into in-memory dictionary cache
        LoadDictionaryToMemory(_currentLanguage);

        // Update WPF MergedDictionaries if running in WPF Application context
        UpdateWpfResourceDictionary(_currentLanguage);

        if (raiseEvent)
        {
            LanguageChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private void UpdateWpfResourceDictionary(string cultureCode)
    {
        if (WpfApplication.Current == null)
            return;

        if (WpfApplication.Current.Dispatcher != null && !WpfApplication.Current.Dispatcher.CheckAccess())
        {
            WpfApplication.Current.Dispatcher.Invoke(() => UpdateWpfResourceDictionary(cultureCode));
            return;
        }

        try
        {
            var uri = new Uri($"pack://application:,,,/Resources/Languages/Strings.{cultureCode}.xaml", UriKind.RelativeOrAbsolute);
            var newDict = new ResourceDictionary { Source = uri };

            var existing = WpfApplication.Current.Resources.MergedDictionaries
                .FirstOrDefault(d => d.Source != null && d.Source.OriginalString.Contains("Strings."));

            if (existing != null)
            {
                var index = WpfApplication.Current.Resources.MergedDictionaries.IndexOf(existing);
                WpfApplication.Current.Resources.MergedDictionaries[index] = newDict;
            }
            else
            {
                WpfApplication.Current.Resources.MergedDictionaries.Add(newDict);
            }
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"Failed to swap WPF ResourceDictionary to '{cultureCode}': {ex.Message}");
        }
    }

    private void LoadDictionaryToMemory(string cultureCode)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);

        string? foundPath = null;
        var directPath = Path.Combine(AppContext.BaseDirectory, "Resources", "Languages", $"Strings.{cultureCode}.xaml");
        if (File.Exists(directPath))
        {
            foundPath = directPath;
        }
        else
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "N_m3u8DL_RE_GUI.sln")))
            {
                dir = dir.Parent;
            }

            if (dir != null)
            {
                var projectPath = Path.Combine(dir.FullName, "N_m3u8DL_RE_GUI", "Resources", "Languages", $"Strings.{cultureCode}.xaml");
                if (File.Exists(projectPath))
                {
                    foundPath = projectPath;
                }
            }
        }

        if (foundPath != null && File.Exists(foundPath))
        {
            try
            {
                var doc = XDocument.Parse(File.ReadAllText(foundPath));
                if (doc.Root != null)
                {
                    foreach (var element in doc.Root.Elements())
                    {
                        var key = element.Attribute(XamlNamespace + "Key")?.Value;
                        if (!string.IsNullOrEmpty(key))
                        {
                            map[key] = element.Value;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"Failed to parse XAML file at '{foundPath}': {ex.Message}");
            }
        }

        _inMemoryStrings = map;
    }
}

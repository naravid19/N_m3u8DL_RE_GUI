#nullable enable
using System;
using System.Collections.Generic;

namespace N_m3u8DL_RE_GUI.Services;

/// <summary>
/// Represents metadata for a supported UI language.
/// </summary>
/// <param name="Code">BCP 47 language tag (e.g. en-US, zh-CN, zh-TW).</param>
/// <param name="DisplayName">English name for the language.</param>
/// <param name="NativeName">Native localized name for the language.</param>
public record LanguageInfo(string Code, string DisplayName, string NativeName);

/// <summary>
/// Service responsible for managing active GUI localization resources,
/// querying localized strings, and live hot-reloading language dictionaries.
/// </summary>
public interface ILanguageService
{
    /// <summary>
    /// Gets the list of supported GUI languages.
    /// </summary>
    IReadOnlyList<LanguageInfo> SupportedLanguages { get; }

    /// <summary>
    /// Gets the current active language culture code (e.g., "en-US", "zh-CN", "zh-TW").
    /// </summary>
    string CurrentLanguage { get; }

    /// <summary>
    /// Switches the active GUI language and updates WPF ResourceDictionaries in real time.
    /// </summary>
    /// <param name="cultureCode">The BCP 47 culture code to activate.</param>
    void SetLanguage(string cultureCode);

    /// <summary>
    /// Resolves a localized string by its key from the active language dictionary.
    /// </summary>
    /// <param name="key">The resource key (e.g. "Str_AppTitle").</param>
    /// <param name="fallback">Fallback string if key is not found. Defaults to the key itself if null.</param>
    /// <returns>Localized string value.</returns>
    string GetString(string key, string? fallback = null);

    /// <summary>
    /// Event raised immediately after the active language is changed.
    /// </summary>
    event EventHandler? LanguageChanged;
}

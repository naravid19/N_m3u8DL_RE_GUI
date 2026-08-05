#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.Json;

namespace N_m3u8DL_RE_GUI.Services;

/// <summary>
/// JSON-based config service with automatic migration from legacy config.txt.
/// Implements the same <see cref="IConfigService"/> interface for backward compatibility
/// while storing data in a human-readable JSON format.
/// </summary>
public sealed class JsonConfigService : IConfigService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = null, // Preserve key names exactly as-is
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    private const string JsonConfigFileName = "config.json";
    private const string LegacyConfigFileName = "config.txt";

    /// <summary>
    /// Loads config from JSON. If the JSON file doesn't exist but a legacy
    /// config.txt does, automatically migrates the legacy format to JSON.
    /// </summary>
    public AppConfigState Load(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return new AppConfigState();

        var directory = Path.GetDirectoryName(path) ?? string.Empty;
        var jsonPath = Path.Combine(directory, JsonConfigFileName);
        var legacyPath = path; // Original path is the legacy config.txt path

        // Try loading from JSON first
        if (File.Exists(jsonPath))
            return LoadFromJson(jsonPath);

        // Fall back to legacy config.txt and auto-migrate
        if (File.Exists(legacyPath))
        {
            var legacyService = new ConfigService();
            var state = legacyService.Load(legacyPath);

            // Auto-migrate: save as JSON for next time
            SaveToJson(jsonPath, state);
            Debug.WriteLine($"Migrated legacy config to JSON: {jsonPath}");

            return state;
        }

        return new AppConfigState();
    }

    /// <summary>
    /// Saves config state as a JSON file alongside the legacy path.
    /// Also writes legacy config.txt for backward compatibility.
    /// </summary>
    public void Save(string path, AppConfigState state)
    {
        if (string.IsNullOrWhiteSpace(path))
            return;

        var directory = Path.GetDirectoryName(path) ?? string.Empty;
        var jsonPath = Path.Combine(directory, JsonConfigFileName);

        SaveToJson(jsonPath, state);

        // Also save legacy format for backward compatibility
        var legacyService = new ConfigService();
        legacyService.Save(path, state);
    }

    private static AppConfigState LoadFromJson(string jsonPath)
    {
        var state = new AppConfigState();

        try
        {
            var json = File.ReadAllText(jsonPath);
            var dict = JsonSerializer.Deserialize<Dictionary<string, string>>(json, JsonOptions);

            if (dict != null)
            {
                foreach (var kvp in dict)
                    state.Set(kvp.Key, kvp.Value);
            }
        }
        catch (JsonException ex)
        {
            Debug.WriteLine($"JSON config parse error: {ex.Message}");
        }
        catch (IOException ex)
        {
            Debug.WriteLine($"JSON config IO error: {ex.Message}");
        }

        return state;
    }

    private static void SaveToJson(string jsonPath, AppConfigState state)
    {
        try
        {
            var dict = new Dictionary<string, string>(state.Entries);
            var json = JsonSerializer.Serialize(dict, JsonOptions);
            File.WriteAllText(jsonPath, json);
        }
        catch (JsonException ex)
        {
            Debug.WriteLine($"JSON config serialize error: {ex.Message}");
        }
        catch (IOException ex)
        {
            Debug.WriteLine($"JSON config save IO error: {ex.Message}");
        }
    }
}

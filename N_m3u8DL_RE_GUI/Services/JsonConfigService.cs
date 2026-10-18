#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using N_m3u8DL_RE_GUI.Core;

namespace N_m3u8DL_RE_GUI.Services;

/// <summary>
/// JSON-based config service with automatic migration from legacy config.txt
/// and Windows DPAPI protection for sensitive configuration fields.
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
    /// Config keys whose values are encrypted at rest with Windows DPAPI.
    /// Includes the legacy Chinese key names that MainWindowConfigMapper persists,
    /// and the legacy "IV" duplicate of CustomHLSIv. Renaming persisted keys would
    /// orphan existing user configs, so the set carries both spellings.
    /// </summary>
    private static readonly HashSet<string> SecretKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "Headers",
        "请求头",
        "Proxy",
        "代理",
        "CustomHLSKey",
        "CustomHLSIv",
        "IV",
        "Key"
    };

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
            Trace.WriteLine($"Migrated legacy config to JSON: {jsonPath}");

            return state;
        }

        return new AppConfigState();
    }

    /// <summary>
    /// Saves config state as a JSON file alongside the legacy path.
    /// Also writes sanitized legacy config.txt for backward compatibility.
    /// </summary>
    public bool Save(string path, AppConfigState state)
    {
        if (string.IsNullOrWhiteSpace(path))
            return false;

        var directory = Path.GetDirectoryName(path) ?? string.Empty;
        var jsonPath = Path.Combine(directory, JsonConfigFileName);

        bool jsonSaved = SaveToJson(jsonPath, state);

        // Save sanitized legacy format (excluding secrets) for backward compatibility
        var sanitizedState = new AppConfigState();
        foreach (var entry in state.Entries)
        {
            if (!SecretKeys.Contains(entry.Key))
            {
                sanitizedState.Set(entry.Key, entry.Value);
            }
        }
        var legacyService = new ConfigService();
        bool legacySaved = legacyService.Save(path, sanitizedState);

        return jsonSaved && legacySaved;
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
                {
                    string value = kvp.Value;
                    if (SecretKeys.Contains(kvp.Key))
                    {
                        value = UnprotectSecret(value);
                    }
                    state.Set(kvp.Key, value);
                }
            }
        }
        catch (JsonException ex)
        {
            Trace.WriteLine($"JSON config parse error: {ex.Message}");
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"JSON config load error: {ex.Message}");
        }

        return state;
    }

    private static bool SaveToJson(string jsonPath, AppConfigState state)
    {
        try
        {
            var dict = new Dictionary<string, string>();
            foreach (var entry in state.Entries)
            {
                string value = entry.Value;
                if (SecretKeys.Contains(entry.Key) && !string.IsNullOrEmpty(value))
                {
                    value = ProtectSecret(value);
                }
                dict[entry.Key] = value;
            }

            var json = JsonSerializer.Serialize(dict, JsonOptions);
            AtomicFile.WriteAllText(jsonPath, json);
            return true;
        }
        catch (Exception ex)
        {
            // Trace, not Debug: Debug.WriteLine is compiled out of Release, so
            // a disk-full or permission failure left no trace whatsoever while
            // the user closed the app believing their settings were saved.
            Trace.WriteLine($"JSON config save failed: {ex}");
            return false;
        }
    }

    private static string ProtectSecret(string value)
    {
        if (string.IsNullOrEmpty(value) || value.StartsWith("dpapi:", StringComparison.Ordinal))
            return value;

        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return value;

        try
        {
            byte[] plaintextBytes = Encoding.UTF8.GetBytes(value);
            byte[] protectedBytes = ProtectedData.Protect(plaintextBytes, null, DataProtectionScope.CurrentUser);
            return "dpapi:" + Convert.ToBase64String(protectedBytes);
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"DPAPI protect failed: {ex.Message}");
            return value;
        }
    }

    private static string UnprotectSecret(string value)
    {
        if (string.IsNullOrEmpty(value) || !value.StartsWith("dpapi:", StringComparison.Ordinal))
            return value;

        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return value;

        try
        {
            string base64 = value.Substring(6);
            byte[] protectedBytes = Convert.FromBase64String(base64);
            byte[] plaintextBytes = ProtectedData.Unprotect(protectedBytes, null, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(plaintextBytes);
        }
        catch (Exception ex)
        {
            // Return the ciphertext untouched rather than an empty string. ProtectSecret
            // no-ops on values already prefixed "dpapi:", so the blob survives the next
            // save and stays recoverable on the machine that encrypted it. Returning
            // string.Empty here silently deleted the user's secret on the next save.
            // ponytail: the raw blob is visible in the textbox; a "could not decrypt"
            // placeholder needs UI state this service does not own.
            Trace.WriteLine($"DPAPI unprotect failed, preserving ciphertext: {ex.Message}");
            return value;
        }
    }
}

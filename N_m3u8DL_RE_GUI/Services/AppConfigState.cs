#nullable enable
using System;
using System.Collections.Generic;
using System.Text;

namespace N_m3u8DL_RE_GUI.Services;

/// <summary>
/// Parsed key/value state for legacy config.txt.
/// </summary>
public sealed class AppConfigState
{
    private readonly Dictionary<string, string> _entries = new(StringComparer.Ordinal);

    public IReadOnlyDictionary<string, string> Entries => _entries;

    public string Get(string key)
    {
        return _entries.TryGetValue(key, out var value) ? value : string.Empty;
    }

    public void Set(string key, string? value)
    {
        if (string.IsNullOrWhiteSpace(key))
            return;

        _entries[key] = value ?? string.Empty;
    }

    public bool GetBool(string key, bool defaultValue = false)
    {
        var raw = Get(key);
        if (raw == "1")
            return true;
        if (raw == "0")
            return false;
        return bool.TryParse(raw, out var value) ? value : defaultValue;
    }

    public int? GetInt(string key)
    {
        var raw = Get(key);
        return int.TryParse(raw, out var value) ? value : null;
    }

    public string GetDecodedBase64(string key)
    {
        var raw = Get(key);
        if (string.IsNullOrWhiteSpace(raw))
            return string.Empty;

        try
        {
            return Encoding.UTF8.GetString(Convert.FromBase64String(raw));
        }
        catch (FormatException)
        {
            return string.Empty;
        }
    }

    public void SetEncodedBase64(string key, string? value)
    {
        var safeValue = value ?? string.Empty;
        var encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes(safeValue));
        Set(key, encoded);
    }
}

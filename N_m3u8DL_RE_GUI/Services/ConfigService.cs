#nullable enable
using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using N_m3u8DL_RE_GUI.Core;

namespace N_m3u8DL_RE_GUI.Services;

/// <summary>
/// Reads/writes legacy config.txt using safe parsing.
/// </summary>
public class ConfigService : IConfigService
{
    public AppConfigState Load(string path)
    {
        var state = new AppConfigState();
        if (string.IsNullOrWhiteSpace(path))
            return state;

        if (!File.Exists(path))
            return state;

        string content;
        try
        {
            content = File.ReadAllText(path);
        }
        catch (IOException)
        {
            Trace.WriteLine($"Config load failed due to IO error: {path}");
            return state;
        }
        catch (UnauthorizedAccessException)
        {
            Trace.WriteLine($"Config load failed due to access error: {path}");
            return state;
        }
        catch (ArgumentException)
        {
            Trace.WriteLine($"Config load failed due to invalid path: {path}");
            return state;
        }
        catch (NotSupportedException)
        {
            Trace.WriteLine($"Config load failed due to unsupported path: {path}");
            return state;
        }

        if (string.IsNullOrWhiteSpace(content))
            return state;

        var segments = content.Split(';');
        foreach (var segment in segments)
        {
            if (string.IsNullOrWhiteSpace(segment))
                continue;

            var separatorIndex = segment.IndexOf('=');
            if (separatorIndex <= 0)
                continue;

            var key = segment[..separatorIndex].Trim();
            var value = LegacyConfigCodec.UnescapeValue(segment[(separatorIndex + 1)..]);
            if (key.Length == 0)
                continue;

            state.Set(key, value);
        }

        return state;
    }

    public bool Save(string path, AppConfigState state)
    {
        if (string.IsNullOrWhiteSpace(path))
            return false;

        var builder = new StringBuilder();
        foreach (var pair in state.Entries)
        {
            if (builder.Length > 0)
                builder.Append(';');

            builder.Append(pair.Key).Append('=').Append(LegacyConfigCodec.EscapeValue(pair.Value));
        }

        try
        {
            AtomicFile.WriteAllText(path, builder.ToString());
            return true;
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"Config save failed: {ex}");
            return false;
        }
    }
}

#nullable enable
using System;
using System.Collections.Generic;
using System.IO;

namespace N_m3u8DL_RE_GUI.Core;

/// <summary>
/// Validation and classification rules for drag-and-drop paths.
/// </summary>
public static class DropInputRules
{
    private static readonly HashSet<string> UrlInputExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".m3u8",
        ".txt",
        ".json",
        ".mpd"
    };

    private static readonly HashSet<string> AutoTitleExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".m3u8",
        ".json",
        ".mpd"
    };

    public static bool IsSupportedUrlInputPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return false;

        if (Directory.Exists(path))
            return true;

        if (!File.Exists(path))
            return false;

        var extension = Path.GetExtension(path);
        return UrlInputExtensions.Contains(extension);
    }

    public static bool ShouldAutoFillTitleFromFileName(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return false;

        var extension = Path.GetExtension(path);
        return AutoTitleExtensions.Contains(extension);
    }

    public static bool IsValidMuxImportPath(string? path)
    {
        return !string.IsNullOrWhiteSpace(path) && File.Exists(path);
    }

    public static bool IsValidKeyFilePath(string? path, long expectedBytes = 16)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return false;

        try
        {
            return new FileInfo(path).Length == expectedBytes;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
        catch (ArgumentException)
        {
            return false;
        }
        catch (NotSupportedException)
        {
            return false;
        }
    }
}

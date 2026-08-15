#nullable enable
using System;
using System.IO;
using System.Text.RegularExpressions;

namespace N_m3u8DL_RE_GUI.Core;

/// <summary>
/// Parses batch input lines for .txt source lists.
/// Supported formats:
/// 1) http(s)://... or local file path
/// 2) [title],http(s)://... or [title],local file path
/// </summary>
public static class BatchInputParser
{
    private static readonly Regex TitleUrlSeparator = new(
        @",\s*(https?://|file://|[A-Za-z]:[\\/]|/|\S+\.(m3u8|mpd|m3u|json|xml))",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static bool TryParse(string? rawLine, out BatchInputEntry? entry)
    {
        entry = null;
        var line = rawLine?.Trim();
        if (string.IsNullOrEmpty(line) || line.StartsWith("#", StringComparison.Ordinal) || line.StartsWith("//", StringComparison.Ordinal))
            return false;

        var separator = TitleUrlSeparator.Match(line);
        if (separator.Success)
        {
            var title = line[..separator.Index];
            var url = line[(separator.Index + 1)..].TrimStart();
            entry = new BatchInputEntry(Url: url, Title: title, HasCustomTitle: true);
            return true;
        }

        if (IsUrlOrFilePath(line))
        {
            entry = new BatchInputEntry(Url: line, Title: string.Empty, HasCustomTitle: false);
            return true;
        }

        return false;
    }

    private static bool IsUrlOrFilePath(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return false;

        if (input.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            input.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
            input.StartsWith("file://", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (Path.IsPathRooted(input) || input.Contains('\\') || input.Contains('/'))
            return true;

        if (input.EndsWith(".m3u8", StringComparison.OrdinalIgnoreCase) ||
            input.EndsWith(".mpd", StringComparison.OrdinalIgnoreCase) ||
            input.EndsWith(".m3u", StringComparison.OrdinalIgnoreCase) ||
            input.EndsWith(".json", StringComparison.OrdinalIgnoreCase) ||
            input.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return false;
    }
}

public sealed record BatchInputEntry(string Url, string Title, bool HasCustomTitle);

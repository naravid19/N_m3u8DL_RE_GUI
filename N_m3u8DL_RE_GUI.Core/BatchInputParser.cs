#nullable enable
using System;
using System.Text.RegularExpressions;

namespace N_m3u8DL_RE_GUI.Core;

/// <summary>
/// Parses batch input lines for .txt source lists.
/// Supported formats:
/// 1) http(s)://...
/// 2) title,http(s)://...
/// </summary>
public static class BatchInputParser
{
    private static readonly Regex TitleUrlSeparator = new(
        @",\s*https?://",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static bool TryParse(string? rawLine, out BatchInputEntry? entry)
    {
        entry = null;
        var line = rawLine?.Trim();
        if (string.IsNullOrEmpty(line) || line.StartsWith("#", StringComparison.Ordinal))
            return false;

        if (line.StartsWith("http", StringComparison.OrdinalIgnoreCase))
        {
            entry = new BatchInputEntry(Url: line, Title: string.Empty, HasCustomTitle: false);
            return true;
        }

        var separator = TitleUrlSeparator.Match(line);
        if (!separator.Success)
            return false;

        var title = line[..separator.Index];
        var url = line[(separator.Index + 1)..].TrimStart();
        entry = new BatchInputEntry(Url: url, Title: title, HasCustomTitle: true);
        return true;
    }
}

public sealed record BatchInputEntry(string Url, string Title, bool HasCustomTitle);

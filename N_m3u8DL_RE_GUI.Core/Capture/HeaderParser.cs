#nullable enable
using System;
using System.Collections.Generic;

namespace N_m3u8DL_RE_GUI.Core.Capture;

/// <summary>
/// Parses raw header text (newline-delimited, pipe-delimited, or -H flags) into a case-insensitive dictionary.
/// </summary>
public static class HeaderParser
{
    public static Dictionary<string, string> Parse(string? rawText)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(rawText))
            return result;

        // Split by newlines or pipe delimiter
        var lines = rawText.Split(new[] { "\r\n", "\r", "\n", "|" }, StringSplitOptions.RemoveEmptyEntries);

        foreach (var rawLine in lines)
        {
            var line = rawLine.Trim();
            if (string.IsNullOrWhiteSpace(line))
                continue;

            // Strip leading -H or --header flags if present
            if (line.StartsWith("-H ", StringComparison.OrdinalIgnoreCase))
                line = line.Substring(3).Trim();
            else if (line.StartsWith("--header ", StringComparison.OrdinalIgnoreCase))
                line = line.Substring(9).Trim();

            // Strip surrounding quotes
            if ((line.StartsWith("\"") && line.EndsWith("\"")) ||
                (line.StartsWith("'") && line.EndsWith("'")))
            {
                if (line.Length >= 2)
                    line = line.Substring(1, line.Length - 2).Trim();
            }

            int colonIdx = line.IndexOf(':');
            if (colonIdx <= 0 || colonIdx >= line.Length - 1)
                continue;

            string name = line.Substring(0, colonIdx).Trim();
            string value = line.Substring(colonIdx + 1).Trim();

            if (!string.IsNullOrEmpty(name) && !string.IsNullOrEmpty(value))
            {
                result[name] = value;
            }
        }

        return result;
    }
}

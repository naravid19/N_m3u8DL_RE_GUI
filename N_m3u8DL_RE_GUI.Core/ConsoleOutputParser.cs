#nullable enable
using System.Text.RegularExpressions;

namespace N_m3u8DL_RE_GUI.Core;

/// <summary>
/// Turns raw redirected console output from N_m3u8DL-RE into text fit for the GUI log
/// and a progress percentage. Pure functions — no streams, no state.
/// </summary>
public static class ConsoleOutputParser
{
    // CSI sequences: ESC [ <params> <final byte>. Covers colour, erase-line and cursor moves.
    private static readonly Regex AnsiPattern = new(
        @"\u001b\[[0-9;?]*[A-Za-z]",
        RegexOptions.Compiled);

    // Last percentage on the line is the freshest one on a redrawn progress row.
    private static readonly Regex PercentPattern = new(
        @"(?<!\d)(\d{1,3})(?:\.\d+)?%",
        RegexOptions.Compiled | RegexOptions.RightToLeft);

    public static string StripAnsi(string line) =>
        string.IsNullOrEmpty(line) ? string.Empty : AnsiPattern.Replace(line, string.Empty);

    /// <summary>Returns 0-100, or null when the line carries no usable percentage.</summary>
    public static int? TryExtractPercent(string line)
    {
        if (string.IsNullOrEmpty(line))
            return null;

        var match = PercentPattern.Match(StripAnsi(line));
        if (!match.Success)
            return null;

        return int.TryParse(match.Groups[1].Value, out var percent) && percent >= 0 && percent <= 100
            ? percent
            : null;
    }

    /// <summary>Strips escapes and surrounding whitespace; empty when nothing remains.</summary>
    public static string Clean(string rawLine) => StripAnsi(rawLine ?? string.Empty).Trim();
}

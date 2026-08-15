#nullable enable
using System;
using System.Text;
using System.Text.RegularExpressions;

namespace N_m3u8DL_RE_GUI.Core;

/// <summary>
/// Streaming-safe HTML title handling. Pure functions so the read loop in UtilityService
/// stays thin and every rule here is testable without a socket.
/// </summary>
public static class HtmlTitleExtractor
{
    private const string ClosingTag = "</title>";

    /// <summary>Overlap kept between chunks: one char short of the tag length.</summary>
    private static readonly int CarrySize = ClosingTag.Length - 1;

    private static readonly Regex TitlePattern = new(
        @"<title[^>]*>([^<]+)</title>",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    static HtmlTitleExtractor()
    {
        // .NET Core ships only Unicode code pages by default.
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    /// <summary>
    /// True once the closing title tag has been seen. Call once per chunk in order,
    /// threading the same <paramref name="carry"/> through; it holds the tail of the
    /// previous chunk so a tag split across a boundary is still found. O(chunk), not
    /// O(total) — the previous implementation re-scanned the whole buffer every chunk.
    /// </summary>
    public static bool ContainsClosingTitleTag(string chunk, ref string carry)
    {
        if (string.IsNullOrEmpty(chunk))
            return false;

        var window = carry.Length > 0 ? carry + chunk : chunk;
        if (window.Contains(ClosingTag, StringComparison.OrdinalIgnoreCase))
        {
            carry = string.Empty;
            return true;
        }

        carry = window.Length <= CarrySize ? window : window[^CarrySize..];
        return false;
    }

    /// <summary>Returns the cleaned title, or empty when the document has none.</summary>
    public static string Extract(string html)
    {
        if (string.IsNullOrEmpty(html))
            return string.Empty;

        var match = TitlePattern.Match(html);
        return match.Success ? Clean(match.Groups[1].Value) : string.Empty;
    }

    /// <summary>Strips known site suffixes and characters Windows forbids in filenames.</summary>
    public static string Clean(string title)
    {
        if (string.IsNullOrWhiteSpace(title))
            return string.Empty;

        title = Regex.Replace(title, "[-_\\s]*(\\u7231\\u5947\\u827A).*?$", "", RegexOptions.IgnoreCase);
        title = Regex.Replace(title, "[-_\\s]*(\\u817E\\u8BAF\\u89C6\\u9891).*?$", "", RegexOptions.IgnoreCase);
        title = Regex.Replace(title, @"[-_\s]*WeTV.*$", "", RegexOptions.IgnoreCase);
        title = Regex.Replace(title, "[-_\\s]*(\\u54D4\\u54E9\\u54D4\\u54E9).*?$", "", RegexOptions.IgnoreCase);
        title = Regex.Replace(title, "[-_\\s]*(\\u4F18\\u9177).*?$", "", RegexOptions.IgnoreCase);

        title = Regex.Replace(title, @"[<>:""/\\|?*]", "");
        return title.Trim();
    }

    /// <summary>
    /// Maps an HTTP Content-Type charset token to an Encoding, falling back to UTF-8 for
    /// anything missing or unrecognised.
    /// </summary>
    public static Encoding ResolveEncoding(string? charSet)
    {
        if (string.IsNullOrWhiteSpace(charSet))
            return Encoding.UTF8;

        var trimmed = charSet.Trim().Trim('"', '\'');
        try
        {
            return Encoding.GetEncoding(trimmed);
        }
        catch (ArgumentException)
        {
            return Encoding.UTF8;
        }
    }
}

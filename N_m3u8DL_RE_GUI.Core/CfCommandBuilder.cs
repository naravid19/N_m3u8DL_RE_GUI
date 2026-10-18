#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using N_m3u8DL_RE_GUI.Core.Capture;

namespace N_m3u8DL_RE_GUI.Core;

/// <summary>Inputs for one m3u8_cf_bypass.py invocation.</summary>
public sealed record CfCommandOptions(
    string PythonExe,
    string ScriptPath,
    string Url,
    string OutputName,
    string WorkDir,
    string SegDir,
    string Referer,
    string Cookie,
    string Impersonate,
    bool KeepSegments,
    string UserAgent = "",
    IReadOnlyList<CapturedHeader>? ExtraHeaders = null,
    int Threads = 0,
    bool Serve = false);

/// <summary>
/// Builds the Cloudflare-bypass command line and its .bat wrapper.
/// Pure string work, extracted from MainWindow so it can be tested.
/// </summary>
public static class CfCommandBuilder
{
    /// <summary>
    /// Computes a deterministic 8-character hex job ID based on URL and output filename.
    /// This enables seamless segment resuming when the same download is restarted.
    /// </summary>
    public static string ComputeJobId(string? url, string? outputName)
    {
        string rawKey = (url?.Trim() ?? string.Empty) + "|" + (outputName?.Trim() ?? string.Empty);
        using var sha = System.Security.Cryptography.SHA256.Create();
        var hashBytes = sha.ComputeHash(Encoding.UTF8.GetBytes(rawKey));
        return BitConverter.ToString(hashBytes).Replace("-", string.Empty).Substring(0, 8).ToLowerInvariant();
    }

    public static string BuildCommand(CfCommandOptions o) =>
        $"{Quote(o.PythonExe)} {BuildArguments(o)}";

    /// <summary>
    /// Everything BuildCommand emits after the python executable itself — the
    /// piece a direct ProcessStartInfo launch (no .bat, no pythonExe token)
    /// needs for the --serve proxy, where stdin/stdout are read directly
    /// rather than left for a human to read off a console.
    /// </summary>
    public static string BuildArguments(CfCommandOptions o)
    {
        var sb = new StringBuilder();
        sb.Append(Quote(o.ScriptPath));
        sb.Append($" {Quote(o.Url)}");
        sb.Append($" --referer {Quote(o.Referer)}");
        sb.Append($" -o {Quote(o.OutputName)}");
        sb.Append($" --work-dir {Quote(o.WorkDir)}");
        sb.Append($" --seg-dir {Quote(o.SegDir)}");
        sb.Append($" --impersonate {Quote(o.Impersonate)}");

        if (!string.IsNullOrEmpty(o.UserAgent))
            sb.Append($" --user-agent {Quote(o.UserAgent)}");

        if (!string.IsNullOrEmpty(o.Cookie))
            sb.Append($" --cookie {Quote(o.Cookie)}");

        if (o.ExtraHeaders != null)
        {
            foreach (var h in o.ExtraHeaders)
            {
                if (!string.IsNullOrWhiteSpace(h.Name) && !string.IsNullOrWhiteSpace(h.Value))
                {
                    sb.Append($" -H {Quote(h.Name + ": " + h.Value)}");
                }
            }
        }

        if (o.Threads > 0)
            sb.Append($" --threads {o.Threads}");

        if (o.Serve)
            sb.Append(" --serve");

        if (o.KeepSegments)
            sb.Append(" --keep-segs");

        return sb.ToString();
    }

    /// <summary>
    /// Builds a display-safe command line with sensitive values masked.
    /// Used for GUI parameter preview to prevent leaking credentials in
    /// screenshots — covers the Cookie and every extra captured header (e.g.
    /// Authorization, X-Api-Key), since only headers that pass HeaderPolicy
    /// reach ExtraHeaders in the first place and are just as sensitive.
    /// </summary>
    public static string BuildMaskedCommand(CfCommandOptions o)
    {
        var masked = o;

        if (!string.IsNullOrEmpty(o.Cookie))
            masked = masked with { Cookie = MaskValue(o.Cookie) };

        if (o.ExtraHeaders is { Count: > 0 })
            masked = masked with { ExtraHeaders = o.ExtraHeaders.Select(h => h with { Value = MaskValue(h.Value) }).ToList() };

        return BuildCommand(masked);
    }

    private static string MaskValue(string value) =>
        string.IsNullOrEmpty(value) ? string.Empty : $"...(masked, {value.Length} chars)";

    /// <summary>
    /// Returns the explicit referer when supplied, otherwise the input URL's
    /// scheme+authority with a trailing slash, otherwise empty.
    /// </summary>
    public static string DeriveReferer(string? explicitReferer, string? inputUrl)
    {
        var trimmed = explicitReferer?.Trim();
        if (!string.IsNullOrEmpty(trimmed))
            return trimmed;

        if (string.IsNullOrWhiteSpace(inputUrl))
            return string.Empty;

        return Uri.TryCreate(inputUrl.Trim(), UriKind.Absolute, out var uri)
            ? uri.GetLeftPart(UriPartial.Authority) + "/"
            : string.Empty;
    }

    /// <summary>
    /// Drops control characters (a pasted CR/LF has no place in an argument), then quotes
    /// through the same escaper ArgsBuilder uses — so a value ending in a backslash, such
    /// as a drive-root work dir, cannot escape its own closing quote.
    /// </summary>
    private static string Quote(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return "\"\"";

        var sb = new StringBuilder(value.Length);
        foreach (char c in value)
        {
            if (!char.IsControl(c))
            {
                sb.Append(c);
            }
        }
        return StringBuilderExtensions.QuoteForWindowsArgument(sb.ToString());
    }
}

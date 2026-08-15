#nullable enable
using System;
using System.Text;

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
    bool KeepSegments);

/// <summary>
/// Builds the Cloudflare-bypass command line and its .bat wrapper.
/// Pure string work, extracted from MainWindow so it can be tested.
/// </summary>
public static class CfCommandBuilder
{
    public static string BuildCommand(CfCommandOptions o)
    {
        var sb = new StringBuilder();
        sb.Append($"\"{Escape(o.PythonExe)}\"");
        sb.Append($" \"{Escape(o.ScriptPath)}\"");
        sb.Append($" \"{Escape(o.Url)}\"");
        sb.Append($" --referer \"{Escape(o.Referer)}\"");
        sb.Append($" -o \"{Escape(o.OutputName)}\"");
        sb.Append($" --work-dir \"{Escape(o.WorkDir)}\"");
        sb.Append($" --seg-dir \"{Escape(o.SegDir)}\"");
        sb.Append($" --impersonate \"{Escape(o.Impersonate)}\"");

        if (!string.IsNullOrEmpty(o.Cookie))
            sb.Append($" --cookie \"{Escape(o.Cookie)}\"");

        if (o.KeepSegments)
            sb.Append(" --keep-segs");

        return sb.ToString();
    }

    /// <summary>
    /// Wraps a command in a UTF-8 batch script. Percent signs are doubled because
    /// cmd.exe consumes %n as an argument reference — without this, every
    /// percent-encoded URL is corrupted before Python ever sees it.
    /// </summary>
    public static string BuildBatchScript(string command)
    {
        var sb = new StringBuilder();
        sb.AppendLine("@echo off");
        sb.AppendLine("title N_m3u8DL-RE (Cloudflare Bypass Mode)");
        sb.AppendLine("chcp 65001 >nul");
        sb.AppendLine("set PYTHONUTF8=1");
        sb.AppendLine(command.Replace("%", "%%"));
        sb.AppendLine("echo.");
        return sb.ToString();
    }

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

    private static string Escape(string? value) =>
        string.IsNullOrEmpty(value) ? string.Empty : value.Replace("\"", "\\\"");
}

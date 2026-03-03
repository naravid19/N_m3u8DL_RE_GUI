#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

namespace N_m3u8DL_RE_GUI.Core;

/// <summary>
/// Shared input/validation helpers used by GUI and services.
/// </summary>
public static class InputValidation
{
    private static readonly HashSet<string> StartupInputExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".m3u8",
        ".json",
        ".txt",
        ".mpd"
    };

    private static readonly Regex UrlRegex = new(
        @"(https?)://[-A-Za-z0-9+&@#/%?=~_|!:,.;]+[-A-Za-z0-9+&@#/%=~_|]",
        RegexOptions.Compiled | RegexOptions.Singleline);

    public static bool IsHttpUrl(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return false;

        return input.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
               input.StartsWith("https://", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsValidProxy(string? proxy)
    {
        if (string.IsNullOrWhiteSpace(proxy))
            return true;

        return proxy.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
               proxy.StartsWith("socks5://", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsLikelyValidInput(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return true;

        if (IsHttpUrl(input))
            return true;

        return File.Exists(input) || Directory.Exists(input);
    }

    public static bool IsSupportedStartupInputArgument(string? argument)
    {
        if (string.IsNullOrWhiteSpace(argument))
            return false;

        if (IsHttpUrl(argument))
            return true;

        if (Directory.Exists(argument))
            return true;

        var extension = Path.GetExtension(argument);
        return StartupInputExtensions.Contains(extension);
    }

    public static string ExtractFirstUrl(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        return UrlRegex.Match(text).Value;
    }
}

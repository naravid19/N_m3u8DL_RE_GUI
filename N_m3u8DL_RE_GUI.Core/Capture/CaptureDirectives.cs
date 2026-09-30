#nullable enable
using System;
using System.Collections.Generic;

namespace N_m3u8DL_RE_GUI.Core.Capture;

/// <summary>
/// Reads "# nre-key: value" (or "#nre-key: value") lines out of a pasted capture payload.
///
/// They ride along inside a cURL command or batch payload as shell comments, so the payload
/// stays a runnable command and an older build that knows nothing about
/// directives simply ignores them.
/// </summary>
public static class CaptureDirectives
{
    private const string DirectivePrefix = "nre-";

    public static IReadOnlyDictionary<string, string> Parse(string? payload)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(payload))
            return result;

        var lines = payload.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (!trimmed.StartsWith('#'))
                continue;

            // Strip '#' and leading whitespace
            var afterHash = trimmed[1..].TrimStart();
            if (!afterHash.StartsWith(DirectivePrefix, StringComparison.OrdinalIgnoreCase))
                continue;

            var colonIndex = afterHash.IndexOf(':');
            if (colonIndex <= DirectivePrefix.Length)
                continue;

            var key = afterHash[DirectivePrefix.Length..colonIndex].Trim();
            if (key.Length == 0)
                continue;

            var value = afterHash[(colonIndex + 1)..].Trim();
            // Expand escaped newlines in value (e.g. multi-line headers passed as \n)
            value = value.Replace(@"\n", "\n");

            result[key] = value;
        }

        return result;
    }
}

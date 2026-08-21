#nullable enable
using System;
using System.Collections.Generic;

namespace N_m3u8DL_RE_GUI.Core.Capture;

/// <summary>
/// Reads "# nre-key: value" lines out of a pasted capture payload.
///
/// They ride along inside a cURL command as shell comments, so the payload
/// stays a runnable command and an older build that knows nothing about
/// directives simply ignores them.
/// </summary>
public static class CaptureDirectives
{
    private const string Prefix = "# nre-";

    public static IReadOnlyDictionary<string, string> Parse(string? payload)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(payload))
            return result;

        var lines = payload.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (!trimmed.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase))
                continue;

            var colonIndex = trimmed.IndexOf(':');
            if (colonIndex <= Prefix.Length)
                continue;

            var key = trimmed[Prefix.Length..colonIndex].Trim();
            if (key.Length == 0)
                continue;

            var value = trimmed[(colonIndex + 1)..].Trim();
            result[key] = value;
        }

        return result;
    }
}

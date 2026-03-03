#nullable enable
using System;

namespace N_m3u8DL_RE_GUI.Core;

/// <summary>
/// Normalization helpers for mapping UI input into CLI-safe option values.
/// </summary>
public static class OptionValueNormalizer
{
    /// <summary>
    /// Normalize save directory while preserving drive roots such as "C:\".
    /// </summary>
    public static string? NormalizeSaveDir(string? saveDir)
    {
        if (string.IsNullOrWhiteSpace(saveDir))
            return null;

        var trimmed = saveDir.Trim();
        if (trimmed.Length == 0)
            return null;

        // Preserve drive root exactly (e.g. C:\ or D:/)
        if (trimmed.Length == 3 &&
            char.IsLetter(trimmed[0]) &&
            trimmed[1] == ':' &&
            (trimmed[2] == '\\' || trimmed[2] == '/'))
        {
            return $"{char.ToUpperInvariant(trimmed[0])}:\\";
        }

        // Keep UNC prefix while removing trailing separators.
        if (trimmed.StartsWith(@"\\", StringComparison.Ordinal))
            return trimmed.TrimEnd('\\');

        return trimmed.TrimEnd('\\', '/');
    }
}

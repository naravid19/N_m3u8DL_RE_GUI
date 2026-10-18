#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace N_m3u8DL_RE_GUI.Core.Abyss;

/// <summary>
/// How Abyss/Hydrax renditions are ordered and shown. Unattended runs (a browser
/// capture that starts by itself) take the first entry of <see cref="Ranked"/>.
/// </summary>
public static class AbyssSourceChoice
{
    /// <summary>Largest file first (the highest quality on offer); ties keep a stable, label-based order.</summary>
    public static IReadOnlyList<AbyssSource> Ranked(IEnumerable<AbyssSource> sources) =>
        sources
            .OrderByDescending(s => s.Size)
            .ThenBy(s => s.Label, StringComparer.OrdinalIgnoreCase)
            .ToList();

    /// <summary>One line for a list box, e.g. <c>1080p — 412.5 MB · h264</c>.</summary>
    public static string Describe(AbyssSource source)
    {
        var megabytes = (source.Size / (1024.0 * 1024.0)).ToString("F1", CultureInfo.InvariantCulture);
        var label = string.IsNullOrWhiteSpace(source.Label) ? "?" : source.Label;
        return string.IsNullOrWhiteSpace(source.Codec)
            ? $"{label} — {megabytes} MB"
            : $"{label} — {megabytes} MB · {source.Codec}";
    }
}

#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

namespace N_m3u8DL_RE_GUI.Core;

/// <summary>
/// Result of comparing an engine temp directory against its manifest.
/// </summary>
public sealed record SegmentAudit(
    string TmpDir,
    string SegmentsDir,
    int ManifestCount,
    int PresentCount,
    IReadOnlyList<int> MissingIndexes)
{
    public bool IsComplete => MissingIndexes.Count == 0 && ManifestCount > 0;
    public double Completeness => ManifestCount == 0 ? 0 : (double)PresentCount / ManifestCount;
}

/// <summary>
/// Audits an N_m3u8DL-RE temp directory (raw.m3u8 + segment files) and reports
/// which manifest segments are missing from disk. Pure file inspection — no
/// network access — so the GUI can show "1268 segments, 4 missing" before a
/// retry or a tolerant merge.
/// </summary>
public static class SegmentAuditService
{
    // Manifest segment lines reference the remote segment path; the ordinal
    // position in the playlist is the segment index the engine stores on disk
    // as <index>.ts / <index:D4>.ts.
    private static readonly Regex SegmentLinePattern = new(
        @"^\s*#EXTINF:\s*[\d.]+\s*,", RegexOptions.Compiled | RegexOptions.Multiline);

    /// <summary>
    /// Finds the temp directory for a run: an explicit <paramref name="tmpDir"/> when
    /// given, otherwise "&lt;saveDir&gt;/.nre-tmp" scanned for the newest subdirectory
    /// whose mtime is at or after <paramref name="startedUtc"/>. Returns null when
    /// nothing plausible exists.
    /// </summary>
    public static string? ResolveTmpDir(string? tmpDir, string? saveDir, DateTime startedUtc)
    {
        if (!string.IsNullOrWhiteSpace(tmpDir))
            return Directory.Exists(tmpDir) ? tmpDir : null;

        var baseDir = string.IsNullOrWhiteSpace(saveDir)
            ? Environment.CurrentDirectory
            : saveDir;
        var nreTmp = Path.Combine(baseDir, ".nre-tmp");
        if (!Directory.Exists(nreTmp))
            return null;

        string? newest = null;
        var newestWrite = DateTime.MinValue;
        try
        {
            foreach (var dir in Directory.EnumerateDirectories(nreTmp))
            {
                DateTime write;
                try { write = Directory.GetLastWriteTimeUtc(dir); }
                catch { continue; }
                if (write >= startedUtc.AddSeconds(-5) && write > newestWrite)
                {
                    newestWrite = write;
                    newest = dir;
                }
            }
        }
        catch { }

        return newest;
    }

    /// <summary>
    /// Audits the given temp directory. The engine stores the playlist at
    /// &lt;tmp&gt;/raw.m3u8 and segments under the first subdirectory (e.g. "0____").
    /// </summary>
    public static SegmentAudit? Audit(string tmpDir)
    {
        if (string.IsNullOrWhiteSpace(tmpDir) || !Directory.Exists(tmpDir))
            return null;

        var manifestPath = Path.Combine(tmpDir, "raw.m3u8");
        if (!File.Exists(manifestPath))
            return null;

        string? segmentsDir = null;
        foreach (var dir in Directory.EnumerateDirectories(tmpDir))
        {
            segmentsDir = dir;
            break;
        }

        var manifestCount = SegmentLinePattern.Matches(File.ReadAllText(manifestPath)).Count;
        if (manifestCount == 0)
            return new SegmentAudit(tmpDir, segmentsDir ?? string.Empty, 0, 0, Array.Empty<int>());

        var present = new HashSet<int>();
        if (segmentsDir != null)
        {
            foreach (var file in Directory.EnumerateFiles(segmentsDir, "*.ts"))
            {
                var stem = Path.GetFileNameWithoutExtension(file);
                if (int.TryParse(stem, out var index))
                    present.Add(index);
            }
        }

        var missing = new List<int>();
        for (var i = 0; i < manifestCount; i++)
        {
            if (!present.Contains(i))
                missing.Add(i);
        }

        return new SegmentAudit(
            tmpDir,
            segmentsDir ?? string.Empty,
            manifestCount,
            manifestCount - missing.Count,
            missing);
    }
}

/// <summary>
/// Builds an ffmpeg concat list from a segment directory, tolerating missing
/// segment files, and exposes the merge command for the GUI to run.
/// </summary>
public static class SegmentMergeService
{
    /// <summary>
    /// Writes an ffmpeg concat demuxer list for every "*.ts" in
    /// <paramref name="segmentsDir"/> (ordinal order) and returns its path, or null
    /// when no segments exist.
    /// </summary>
    public static string? BuildConcatList(string segmentsDir, string outputPath)
    {
        if (string.IsNullOrWhiteSpace(segmentsDir) || !Directory.Exists(segmentsDir))
            return null;

        var files = Directory.GetFiles(segmentsDir, "*.ts");
        if (files.Length == 0)
            return null;

        Array.Sort(files, string.CompareOrdinal);

        using var writer = new StreamWriter(outputPath, append: false, new System.Text.UTF8Encoding(false));
        foreach (var file in files)
        {
            var normalized = file.Replace('\\', '/');
            writer.Write("file '");
            writer.Write(normalized.Replace("'", @"'\''"));
            writer.WriteLine('\'');
        }

        return outputPath;
    }
}

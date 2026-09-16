#nullable enable
using N_m3u8DL_RE_GUI.Core;
using System;
using System.Collections.Generic;
using System.IO;
using Xunit;

namespace N_m3u8DL_RE_GUI.Tests.Unit.Core;

public class SegmentAuditServiceTests : IDisposable
{
    private readonly string _root;

    public SegmentAuditServiceTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "nre_audit_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { }
    }

    private string WriteTmpDir(int manifestSegments, int[] presentIndexes)
    {
        var tmp = Path.Combine(_root, "job_" + Guid.NewGuid().ToString("N")[..6]);
        var segs = Path.Combine(tmp, "0____");
        Directory.CreateDirectory(segs);

        var manifest = new System.Text.StringBuilder("#EXTM3U\n#EXT-X-TARGETDURATION:6\n");
        for (var i = 0; i < manifestSegments; i++)
        {
            manifest.Append("#EXTINF:6.000000,\n/seg-path/seg-").Append(i).Append(".ts\n");
            if (Array.IndexOf(presentIndexes, i) >= 0)
                File.WriteAllText(Path.Combine(segs, $"{i:D4}.ts"), "x");
        }
        File.WriteAllText(Path.Combine(tmp, "raw.m3u8"), manifest.ToString());
        return tmp;
    }

    [Fact]
    public void Audit_CompleteRun_ShouldReportNoMissing()
    {
        var tmp = WriteTmpDir(4, new[] { 0, 1, 2, 3 });
        var audit = SegmentAuditService.Audit(tmp);

        Assert.NotNull(audit);
        Assert.Equal(4, audit!.ManifestCount);
        Assert.Equal(4, audit.PresentCount);
        Assert.True(audit.IsComplete);
        Assert.Equal(1.0, audit.Completeness);
    }

    [Fact]
    public void Audit_PartialRun_ShouldListMissingIndexes()
    {
        var tmp = WriteTmpDir(10, new[] { 0, 1, 2, 4, 5, 6, 8, 9 });
        var audit = SegmentAuditService.Audit(tmp);

        Assert.NotNull(audit);
        Assert.Equal(10, audit!.ManifestCount);
        Assert.Equal(8, audit.PresentCount);
        Assert.Equal(new List<int> { 3, 7 }, audit.MissingIndexes);
        Assert.False(audit.IsComplete);
        Assert.Equal(0.8, audit.Completeness, precision: 5);
    }

    [Fact]
    public void Audit_EmptyOrInvalidDir_ShouldReturnNull()
    {
        Assert.Null(SegmentAuditService.Audit(_root)); // no raw.m3u8
        Assert.Null(SegmentAuditService.Audit(Path.Combine(_root, "does-not-exist")));
        Assert.Null(SegmentAuditService.Audit(""));
    }

    [Fact]
    public void ResolveTmpDir_ExplicitPath_ShouldBeUsedDirectly()
    {
        var tmp = WriteTmpDir(2, new[] { 0, 1 });
        Assert.Equal(tmp, SegmentAuditService.ResolveTmpDir(tmp, null, DateTime.UtcNow.AddDays(-1)));
    }

    [Fact]
    public void ResolveTmpDir_ScanFindsNewestSubdirectory()
    {
        var saveDir = Path.Combine(_root, "save");
        var nreTmp = Path.Combine(saveDir, ".nre-tmp");
        Directory.CreateDirectory(nreTmp);

        var older = Path.Combine(nreTmp, "old_run");
        var newer = Path.Combine(nreTmp, "new_run");
        Directory.CreateDirectory(older);
        Directory.CreateDirectory(newer);
        Directory.SetLastWriteTimeUtc(older, DateTime.UtcNow.AddDays(-2));
        Directory.SetLastWriteTimeUtc(newer, DateTime.UtcNow);

        Assert.Equal(newer, SegmentAuditService.ResolveTmpDir(null, saveDir, DateTime.UtcNow.AddHours(-1)));
    }

    [Fact]
    public void BuildConcatList_ShouldListSegmentsInOrdinalOrderAndTolerateGaps()
    {
        var segs = Path.Combine(_root, "segs");
        Directory.CreateDirectory(segs);
        foreach (var i in new[] { 2, 0, 3 }) // 1 is missing on purpose
            File.WriteAllText(Path.Combine(segs, $"{i:D4}.ts"), "x");

        var listPath = Path.Combine(_root, "concat.txt");
        Assert.NotNull(SegmentMergeService.BuildConcatList(segs, listPath));

        var lines = File.ReadAllLines(listPath);
        Assert.Equal(3, lines.Length);
        Assert.Contains("0000.ts", lines[0]);
        Assert.Contains("0002.ts", lines[1]);
        Assert.Contains("0003.ts", lines[2]);
    }

    [Fact]
    public void BuildConcatList_NoSegments_ShouldReturnNull()
    {
        var empty = Path.Combine(_root, "empty");
        Directory.CreateDirectory(empty);
        Assert.Null(SegmentMergeService.BuildConcatList(empty, Path.Combine(_root, "c.txt")));
    }
}

#nullable enable
using System;
using System.IO;
using N_m3u8DL_RE_GUI.Core.Resume;
using N_m3u8DL_RE_GUI.Services;
using Xunit;

namespace N_m3u8DL_RE_GUI.Tests.Unit.Services;

public class ResumeJobStoreTests : IDisposable
{
    private readonly string _testDir;
    private readonly string _recordPath;
    private readonly ResumeJobStore _store;

    public ResumeJobStoreTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), "NRE_ResumeTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_testDir);
        _recordPath = Path.Combine(_testDir, "active-job.json");
        _store = new ResumeJobStore(_recordPath);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_testDir))
            {
                Directory.Delete(_testDir, recursive: true);
            }
        }
        catch
        {
            // Ignore cleanup exceptions in tests
        }
    }

    [Fact]
    public void Begin_ThenTryFind_ReturnsTheJob()
    {
        var tmpDir = Path.Combine(_testDir, "tmp_ep4");
        Directory.CreateDirectory(tmpDir);
        File.WriteAllBytes(Path.Combine(tmpDir, "seg1.ts"), new byte[1024]);

        var job = _store.Begin("https://cdn.example.com/master.m3u8", "Episode 4", @"D:\Videos", tmpDir);
        Assert.NotNull(job);

        var found = _store.TryFindResumable();
        Assert.NotNull(found);
        Assert.Equal("Episode 4", found.SaveName);
        Assert.Equal(@"D:\Videos", found.SaveDir);
        Assert.Equal(tmpDir, found.TmpDir);
        Assert.Equal("cdn.example.com", found.SourceHost);
        Assert.Equal(1024, found.ExistingBytes);
    }

    [Fact]
    public void Complete_RemovesTheRecord()
    {
        var tmpDir = Path.Combine(_testDir, "tmp_complete");
        Directory.CreateDirectory(tmpDir);
        File.WriteAllBytes(Path.Combine(tmpDir, "seg1.ts"), new byte[512]);

        _store.Begin("https://cdn.example.com/master.m3u8", "Ep", _testDir, tmpDir);
        Assert.True(File.Exists(_recordPath));

        _store.Complete();
        Assert.False(File.Exists(_recordPath));
        Assert.Null(_store.TryFindResumable());
    }

    [Fact]
    public void TryFind_WithNoRecord_ReturnsNull()
    {
        Assert.Null(_store.TryFindResumable());
    }

    [Fact]
    public void TryFind_WithARecordButAnEmptyTmpDir_ReturnsNullAndCleansUp()
    {
        var emptyTmp = Path.Combine(_testDir, "empty_tmp");
        Directory.CreateDirectory(emptyTmp);

        _store.Begin("https://cdn.example.com/master.m3u8", "Ep", _testDir, emptyTmp);
        Assert.True(File.Exists(_recordPath));

        var found = _store.TryFindResumable();
        Assert.Null(found);
        Assert.False(File.Exists(_recordPath));
    }

    [Fact]
    public void TryFind_ReportsHowManyBytesAreAlreadyOnDisk()
    {
        var tmpDir = Path.Combine(_testDir, "tmp_bytes");
        Directory.CreateDirectory(tmpDir);
        File.WriteAllBytes(Path.Combine(tmpDir, "seg1.ts"), new byte[2000]);
        File.WriteAllBytes(Path.Combine(tmpDir, "seg2.ts"), new byte[3000]);

        _store.Begin("https://cdn.example.com/master.m3u8", "Ep", _testDir, tmpDir);

        var found = _store.TryFindResumable();
        Assert.NotNull(found);
        Assert.Equal(5000, found.ExistingBytes);
    }

    [Fact]
    public void TryFind_WithACorruptRecord_ReturnsNullRatherThanThrowing()
    {
        File.WriteAllText(_recordPath, "{ corrupt json !!");

        var found = _store.TryFindResumable();
        Assert.Null(found);
    }

    [Fact]
    public void TryFind_WithAMissingTmpDirectory_ReturnsNull()
    {
        var missingDir = Path.Combine(_testDir, "non_existent_folder");

        _store.Begin("https://cdn.example.com/master.m3u8", "Ep", _testDir, missingDir);

        var found = _store.TryFindResumable();
        Assert.Null(found);
    }

    [Fact]
    public void Begin_StoresOnlyTheHostNeverTheFullUrl()
    {
        var tmpDir = Path.Combine(_testDir, "tmp_sec");
        Directory.CreateDirectory(tmpDir);
        File.WriteAllBytes(Path.Combine(tmpDir, "seg1.ts"), new byte[100]);

        var job = _store.Begin("https://cdn.example.com/v/master.m3u8?token=SECRET&auth=PASS", "Ep", _testDir, tmpDir);
        Assert.NotNull(job);
        Assert.Equal("cdn.example.com", job.SourceHost);

        var raw = File.ReadAllText(_recordPath);
        Assert.DoesNotContain("SECRET", raw);
        Assert.DoesNotContain("PASS", raw);
        Assert.DoesNotContain("token", raw);
    }

    [Fact]
    public void Begin_TwiceReplacesTheRecordRatherThanAccumulating()
    {
        var tmpDir1 = Path.Combine(_testDir, "tmp1");
        var tmpDir2 = Path.Combine(_testDir, "tmp2");
        Directory.CreateDirectory(tmpDir1);
        Directory.CreateDirectory(tmpDir2);
        File.WriteAllBytes(Path.Combine(tmpDir2, "seg.ts"), new byte[100]);

        _store.Begin("https://a.example.com/1", "Job 1", _testDir, tmpDir1);
        _store.Begin("https://b.example.com/2", "Job 2", _testDir, tmpDir2);

        var found = _store.TryFindResumable();
        Assert.NotNull(found);
        Assert.Equal("Job 2", found.SaveName);
        Assert.Equal("b.example.com", found.SourceHost);
    }

    [Fact]
    public void Discard_RemovesBothTheRecordAndThePartialFiles()
    {
        var tmpDir = Path.Combine(_testDir, "tmp_discard");
        Directory.CreateDirectory(tmpDir);
        File.WriteAllBytes(Path.Combine(tmpDir, "seg1.ts"), new byte[1024]);

        _store.Begin("https://cdn.example.com/m.m3u8", "Job", _testDir, tmpDir);
        Assert.True(File.Exists(_recordPath));
        Assert.True(Directory.Exists(tmpDir));

        var discarded = _store.Discard();
        Assert.True(discarded);
        Assert.False(File.Exists(_recordPath));
        Assert.False(Directory.Exists(tmpDir));
    }

    [Fact]
    public void Discard_WithAFileLockedByAnotherProcess_ReportsFailureAndKeepsTheRecord()
    {
        var tmpDir = Path.Combine(_testDir, "tmp_locked");
        Directory.CreateDirectory(tmpDir);
        var lockedFilePath = Path.Combine(tmpDir, "locked.ts");
        File.WriteAllBytes(lockedFilePath, new byte[1024]);

        _store.Begin("https://cdn.example.com/m.m3u8", "Job", _testDir, tmpDir);

        // Lock the file for reading and writing exclusively
        using var stream = new FileStream(lockedFilePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

        var discarded = _store.Discard();
        Assert.False(discarded);
        Assert.True(File.Exists(_recordPath));
        Assert.True(Directory.Exists(tmpDir));
    }

    [Fact]
    public void TwoDownloadsWithDifferentNamesNeverShareATempDirectory()
    {
        // The leak: a resumed job's directory persisted into the next download,
        // and del-after-done then wiped the first job's partial.
        var first = ResumePaths.ResolveTmpDir(null, null, null, @"D:\Videos", "Episode 4");
        var second = ResumePaths.ResolveTmpDir(null, null, null, @"D:\Videos", "Episode 5");

        Assert.NotEqual(first, second);
    }

    [Fact]
    public void ResumingOneJobDoesNotChangeWhereTheNextJobGoes()
    {
        var duringResume = ResumePaths.ResolveTmpDir(null, @"D:\Videos\.nre-tmp\Episode 4", "Episode 4", @"D:\Videos", "Episode 4");
        var next = ResumePaths.ResolveTmpDir(null, null, null, @"D:\Videos", "Episode 5");

        Assert.NotEqual(duringResume, next);
        Assert.Equal(ResumePaths.DeriveTmpDir(@"D:\Videos", "Episode 5"), next);
    }
}

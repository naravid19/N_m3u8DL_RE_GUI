#nullable enable
using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using N_m3u8DL_RE_GUI.Core;
using Xunit;

namespace N_m3u8DL_RE_GUI.Tests.Unit.Core;

public sealed class PythonLocatorTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "pylocator_" + Guid.NewGuid().ToString("N"));

    public PythonLocatorTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch { /* temp folder; the OS reclaims it */ }
    }

    private string Dir(params string[] parts)
    {
        var path = Path.Combine(new[] { _root }.Concat(parts).ToArray());
        Directory.CreateDirectory(path);
        return path;
    }

    private string Script(string name, int exitCode)
    {
        var path = Path.Combine(Dir("bin"), name + ".cmd");
        File.WriteAllText(path, $"@exit /b {exitCode}\r\n");
        return path;
    }

    [Fact]
    public void Candidates_ListInstalledCPythonsThenCondaThenPathNames_InThatOrder()
    {
        var programFiles = Dir("pf");
        var programFilesX86 = Dir("pf86");
        var localAppData = Dir("lad");
        var userProfile = Dir("user");
        Dir("pf", "Python", "Python312");
        Dir("pf86", "Python", "Python38");
        Dir("lad", "Programs", "Python", "Python311");
        Dir("user", "miniconda3");
        File.WriteAllText(Path.Combine(userProfile, "miniconda3", "python.exe"), "");

        var candidates = PythonLocator.BuildCandidates(localAppData, programFiles, programFilesX86, userProfile);

        Assert.Equal(new[]
        {
            Path.Combine(programFiles, "Python", "Python312", "python.exe"),
            Path.Combine(programFilesX86, "Python", "Python38", "python.exe"),
            Path.Combine(localAppData, "Programs", "Python", "Python311", "python.exe"),
            Path.Combine(userProfile, "miniconda3", "python.exe"),
            "py", "python", "python3",
        }, candidates);
    }

    [Fact]
    public void Candidates_SkipCondaFoldersThatDoNotExist_AndTolerateMissingRoots()
    {
        var candidates = PythonLocator.BuildCandidates(null, "", Path.Combine(_root, "nope"), Dir("user"));

        Assert.Equal(new[] { "py", "python", "python3" }, candidates);
    }

    [Fact]
    public async Task Find_ReturnsTheFirstInterpreterWhoseProbeExitsZero_SkippingFailuresAndMissingFiles()
    {
        var missing = Path.Combine(_root, "gone", "python.exe");
        var broken = Script("broken", 1);
        var working = Script("working", 0);
        var later = Script("later", 0);

        var found = await PythonLocator.FindWithCurlCffiAsync(
            new[] { missing, broken, working, later }, TimeSpan.FromSeconds(20), CancellationToken.None);

        Assert.Equal(working, found);
    }

    [Fact]
    public async Task Find_ReturnsNull_WhenNoCandidateCanImportCurlCffi()
    {
        var found = await PythonLocator.FindWithCurlCffiAsync(
            new[] { Script("broken", 1), Path.Combine(_root, "gone", "python.exe") },
            TimeSpan.FromSeconds(20), CancellationToken.None);

        Assert.Null(found);
    }

    [Fact]
    public async Task Find_HonoursCancellation()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            PythonLocator.FindWithCurlCffiAsync(new[] { Script("working", 0) }, TimeSpan.FromSeconds(20), cts.Token));
    }
}

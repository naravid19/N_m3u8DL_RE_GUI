#nullable enable
using N_m3u8DL_RE_GUI.Core;
using Xunit;

namespace N_m3u8DL_RE_GUI.Tests;

public class DropInputRulesTests
{
    [Fact]
    public void IsSupportedUrlInputPath_ShouldAcceptSupportedFilesAndDirectory()
    {
        var tempDir = Directory.CreateTempSubdirectory("drop-rules");
        var m3u8 = Path.Combine(tempDir.FullName, "video.m3u8");
        var txt = Path.Combine(tempDir.FullName, "list.TXT");
        var json = Path.Combine(tempDir.FullName, "meta.json");
        var mpd = Path.Combine(tempDir.FullName, "dash.mpd");
        File.WriteAllText(m3u8, string.Empty);
        File.WriteAllText(txt, string.Empty);
        File.WriteAllText(json, string.Empty);
        File.WriteAllText(mpd, string.Empty);

        try
        {
            Assert.True(DropInputRules.IsSupportedUrlInputPath(m3u8));
            Assert.True(DropInputRules.IsSupportedUrlInputPath(txt));
            Assert.True(DropInputRules.IsSupportedUrlInputPath(json));
            Assert.True(DropInputRules.IsSupportedUrlInputPath(mpd));
            Assert.True(DropInputRules.IsSupportedUrlInputPath(tempDir.FullName));
        }
        finally
        {
            tempDir.Delete(recursive: true);
        }
    }

    [Fact]
    public void IsSupportedUrlInputPath_ShouldRejectUnsupportedOrMissingPath()
    {
        var tempDir = Directory.CreateTempSubdirectory("drop-rules-reject");
        var unsupported = Path.Combine(tempDir.FullName, "file.srt");
        File.WriteAllText(unsupported, string.Empty);
        try
        {
            Assert.False(DropInputRules.IsSupportedUrlInputPath(unsupported));
            Assert.False(DropInputRules.IsSupportedUrlInputPath(Path.Combine(tempDir.FullName, "missing.m3u8")));
            Assert.False(DropInputRules.IsSupportedUrlInputPath(string.Empty));
        }
        finally
        {
            tempDir.Delete(recursive: true);
        }
    }

    [Theory]
    [InlineData("video.m3u8", true)]
    [InlineData("meta.json", true)]
    [InlineData("dash.mpd", true)]
    [InlineData("list.txt", false)]
    [InlineData("sub.srt", false)]
    public void ShouldAutoFillTitleFromFileName_ShouldMatchLegacyBehavior(string fileName, bool expected)
    {
        Assert.Equal(expected, DropInputRules.ShouldAutoFillTitleFromFileName(fileName));
    }

    [Fact]
    public void IsValidMuxImportPath_ShouldRequireExistingFile()
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            Assert.True(DropInputRules.IsValidMuxImportPath(tempFile));
            Assert.False(DropInputRules.IsValidMuxImportPath(tempFile + ".missing"));
            Assert.False(DropInputRules.IsValidMuxImportPath(string.Empty));
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public void IsValidKeyFilePath_ShouldRequireExactLength()
    {
        var tempDir = Directory.CreateTempSubdirectory("drop-rules-key");
        var validKey = Path.Combine(tempDir.FullName, "valid.key");
        var invalidKey = Path.Combine(tempDir.FullName, "invalid.key");
        File.WriteAllBytes(validKey, new byte[16]);
        File.WriteAllBytes(invalidKey, new byte[15]);

        try
        {
            Assert.True(DropInputRules.IsValidKeyFilePath(validKey));
            Assert.False(DropInputRules.IsValidKeyFilePath(invalidKey));
            Assert.False(DropInputRules.IsValidKeyFilePath(Path.Combine(tempDir.FullName, "missing.key")));
        }
        finally
        {
            tempDir.Delete(recursive: true);
        }
    }
}

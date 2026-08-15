#nullable enable
using N_m3u8DL_RE_GUI.Core;
using Xunit;

namespace N_m3u8DL_RE_GUI.Tests.Unit.Core;

public class DownloadOptionsTests
{
    [Fact]
    public void DownloadOptions_DefaultValues_ShouldBeCorrect()
    {
        var options = new DownloadOptions();

        Assert.Null(options.ExePath);
        Assert.Null(options.Input);
        Assert.Null(options.SaveDir);
        Assert.Null(options.SaveName);
        Assert.Null(options.Headers);
        Assert.Null(options.BaseUrl);
        Assert.Null(options.Key);
        Assert.True(options.UseSystemProxy);
        Assert.True(options.DelAfterDone);
        Assert.False(options.SkipMerge);
        Assert.False(options.BinaryMerge);
        Assert.False(options.MuxAfterDone);
        Assert.False(options.NoLog);
        Assert.True(options.WriteMetaJson);
        Assert.True(options.CheckSegmentsCount);
        Assert.True(options.AutoSubtitleFix);
    }

    [Fact]
    public void DownloadOptions_SettingProperties_ShouldRetainValues()
    {
        var options = new DownloadOptions
        {
            ExePath = @"C:\tools\N_m3u8DL-RE.exe",
            Input = "https://surrit.com/33ece07f-3229-41eb-b189-ec2485619e02/360p/video.m3u8",
            SaveDir = @"D:\Videos",
            SaveName = "SurritVideo",
            Headers = "Referer: https://missav123.com/",
            ThreadCount = 16,
            DelAfterDone = false
        };

        Assert.Equal(@"C:\tools\N_m3u8DL-RE.exe", options.ExePath);
        Assert.Equal("https://surrit.com/33ece07f-3229-41eb-b189-ec2485619e02/360p/video.m3u8", options.Input);
        Assert.Equal(@"D:\Videos", options.SaveDir);
        Assert.Equal("SurritVideo", options.SaveName);
        Assert.Equal("Referer: https://missav123.com/", options.Headers);
        Assert.Equal(16, options.ThreadCount);
        Assert.False(options.DelAfterDone);
    }
}

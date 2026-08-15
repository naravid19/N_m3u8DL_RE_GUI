#nullable enable
using N_m3u8DL_RE_GUI.Core;
using Xunit;

#pragma warning disable CS0618 // Obsolete members are under test on purpose.

namespace N_m3u8DL_RE_GUI.Tests.Unit.Core;

/// <summary>
/// Covers <see cref="DownloadOptions.HasTimeRange"/> and every [Obsolete] compatibility
/// shim. These shims are still reachable from saved configs and third-party callers, so
/// they need to stay wired to the right modern property.
/// </summary>
public class DownloadOptionsLegacyTests
{
    [Theory]
    [InlineData(null, null, false)]
    [InlineData("", "", false)]
    [InlineData("   ", "   ", false)]
    [InlineData("00:00:00", "00:00:00", false)]
    [InlineData("00:00:00", "00:10:00", true)]
    [InlineData("00:01:00", "00:00:00", true)]
    [InlineData("00:01:00", "00:02:00", true)]
    [InlineData("00:01:00", null, false)]
    [InlineData(null, "00:02:00", false)]
    public void HasTimeRange_ShouldRequireBothEndpointsAndANonDefaultValue(
        string? start, string? end, bool expected)
    {
        var options = new DownloadOptions { RangeStart = start, RangeEnd = end };

        Assert.Equal(expected, options.HasTimeRange);
    }

    [Fact]
    public void MaxThreads_ShouldAliasThreadCount()
    {
        var options = new DownloadOptions { MaxThreads = 32 };

        Assert.Equal(32, options.ThreadCount);
        options.ThreadCount = 4;
        Assert.Equal(4, options.MaxThreads);
    }

    [Fact]
    public void RetryCount_ShouldAliasDownloadRetryCount()
    {
        var options = new DownloadOptions { RetryCount = 9 };

        Assert.Equal(9, options.DownloadRetryCount);
        options.DownloadRetryCount = 1;
        Assert.Equal(1, options.RetryCount);
    }

    [Fact]
    public void Timeout_ShouldAliasHttpRequestTimeout()
    {
        var options = new DownloadOptions { Timeout = 42 };

        Assert.Equal(42, options.HttpRequestTimeout);
        options.HttpRequestTimeout = 7;
        Assert.Equal(7, options.Timeout);
    }

    [Fact]
    public void DeleteAfterDone_ShouldAliasDelAfterDone()
    {
        var options = new DownloadOptions { DeleteAfterDone = false };

        Assert.False(options.DelAfterDone);
        options.DelAfterDone = true;
        Assert.True(options.DeleteAfterDone);
    }

    [Fact]
    public void DisableDate_ShouldAliasNoDateInfo()
    {
        var options = new DownloadOptions { DisableDate = true };

        Assert.True(options.NoDateInfo);
    }

    [Fact]
    public void DisableProxy_ShouldInvertUseSystemProxy()
    {
        var options = new DownloadOptions();

        Assert.True(options.UseSystemProxy);
        Assert.False(options.DisableProxy);

        options.DisableProxy = true;
        Assert.False(options.UseSystemProxy);

        options.UseSystemProxy = true;
        Assert.False(options.DisableProxy);
    }

    [Fact]
    public void ParseOnly_ShouldAliasSkipDownload()
    {
        var options = new DownloadOptions { ParseOnly = true };

        Assert.True(options.SkipDownload);
    }

    [Fact]
    public void DisableMerge_ShouldAliasSkipMerge()
    {
        var options = new DownloadOptions { DisableMerge = true };

        Assert.True(options.SkipMerge);
    }

    [Fact]
    public void DisableCheck_ShouldInvertCheckSegmentsCount()
    {
        var options = new DownloadOptions();

        Assert.True(options.CheckSegmentsCount);
        Assert.False(options.DisableCheck);

        options.DisableCheck = true;
        Assert.False(options.CheckSegmentsCount);
    }

    [Fact]
    public void AutoSubFix_ShouldAliasAutoSubtitleFix()
    {
        var options = new DownloadOptions { AutoSubFix = false };

        Assert.False(options.AutoSubtitleFix);
    }

    [Fact]
    public void IV_ShouldAliasCustomHLSIv()
    {
        var options = new DownloadOptions { IV = "00112233445566778899aabbccddeeff" };

        Assert.Equal("00112233445566778899aabbccddeeff", options.CustomHLSIv);
    }

    [Fact]
    public void MuxJson_ShouldAliasMuxImport()
    {
        var options = new DownloadOptions { MuxJson = @"C:\meta.json" };

        Assert.Equal(@"C:\meta.json", options.MuxImport);
    }

    [Fact]
    public void AudioOnly_Setter_ShouldSelectBestAudioAndDropVideo()
    {
        var options = new DownloadOptions { AudioOnly = true };

        Assert.Equal("best", options.SelectAudio);
        Assert.Equal("all", options.DropVideo);
        Assert.True(options.AudioOnly);
    }

    [Fact]
    public void AudioOnly_Setter_WithFalse_ShouldBeANoOp()
    {
        var options = new DownloadOptions { SelectAudio = "best", DropVideo = "all" };

        options.AudioOnly = false;

        // The setter deliberately only acts on `true`, so the flags survive.
        Assert.Equal("best", options.SelectAudio);
        Assert.Equal("all", options.DropVideo);
    }

    [Fact]
    public void AudioOnly_Getter_ShouldRecogniseBothDropSpellings()
    {
        // MainWindow writes DropVideo = ".*"; the legacy setter writes "all". Both mean
        // "drop every video track", so the getter must accept either.
        Assert.True(new DownloadOptions { SelectAudio = "best", DropVideo = ".*" }.AudioOnly);
        Assert.True(new DownloadOptions { SelectAudio = "best", DropVideo = "all" }.AudioOnly);
        Assert.False(new DownloadOptions { SelectAudio = "best", DropVideo = "1080p" }.AudioOnly);
        Assert.False(new DownloadOptions { SelectAudio = null, DropVideo = ".*" }.AudioOnly);
    }

    [Fact]
    public void DefaultInstance_ShouldMatchDocumentedCliDefaults()
    {
        var options = new DownloadOptions();

        Assert.Equal(Environment.ProcessorCount, options.ThreadCount);
        Assert.Equal(3, options.DownloadRetryCount);
        Assert.Equal(100, options.HttpRequestTimeout);
        Assert.Equal(16, options.LiveTakeCount);
        Assert.Equal("MP4DECRYPT", options.DecryptionEngine);
        Assert.Equal("SRT", options.SubFormat);
        Assert.Equal("INFO", options.LogLevel);
        Assert.True(options.UseSystemProxy);
        Assert.True(options.WriteMetaJson);
        Assert.True(options.CheckSegmentsCount);
        Assert.True(options.DelAfterDone);
        Assert.True(options.AutoSubtitleFix);
        Assert.True(options.LiveKeepSegments);
        Assert.False(options.BypassCloudflare);
        Assert.Null(options.ExePath);
    }

    [Fact]
    public void DefaultInstance_ShouldSuppressEveryOptionThatMatchesTheCliDefault()
    {
        var options = new DownloadOptions { Input = "https://example.com/a.m3u8" };

        var args = ArgsBuilder.Build(options);

        Assert.StartsWith("\"https://example.com/a.m3u8\"", args);
        foreach (var suppressed in new[]
                 {
                     "--thread-count", "--download-retry-count", "--http-request-timeout",
                     "--live-take-count", "--log-level", "--decryption-engine",
                     "--write-meta-json", "--check-segments-count", "--live-keep-segments",
                     "--use-system-proxy", "--max-speed", "--custom-range"
                 })
        {
            Assert.DoesNotContain(suppressed, args);
        }
    }

    [Fact]
    public void DefaultInstance_ShouldStillEmitTheTwoDefaultsThatDivergeFromTheCli()
    {
        // DelAfterDone and AutoSubtitleFix default to true in the GUI model but are opt-in
        // flags on the CLI, so they are always written out.
        var args = ArgsBuilder.Build(new DownloadOptions { Input = "https://example.com/a.m3u8" });

        Assert.Contains("--del-after-done", args);
        Assert.Contains("--auto-subtitle-fix", args);
    }
}

#pragma warning restore CS0618

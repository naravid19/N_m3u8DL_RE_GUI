using N_m3u8DL_RE_GUI.Core;
using Xunit;

namespace N_m3u8DL_RE_GUI.Tests;

/// <summary>
/// Unit tests for ArgsBuilder.
/// </summary>
public class ArgsBuilderTests
{
    [Fact]
    public void Build_WithBasicOptions_ShouldGenerateCorrectArguments()
    {
        // Arrange
        var options = new DownloadOptions
        {
            Input = "https://example.com/video.m3u8",
            SaveDir = "C:\\Downloads",
            SaveName = "MyVideo",
            ThreadCount = 12
        };

        // Act
        var result = ArgsBuilder.Build(options);

        // Assert
        Assert.Contains("\"https://example.com/video.m3u8\"", result);
        Assert.Contains("--save-dir \"C:\\Downloads\"", result);
        Assert.Contains("--save-name \"MyVideo\"", result);
        Assert.Contains("--thread-count 12", result);
    }

    [Fact]
    public void Build_WithBooleanOptions_ShouldIncludeEnabledFlags()
    {
        // Arrange
        var options = new DownloadOptions
        {
            Input = "https://example.com/video.m3u8",
            DelAfterDone = true,
            AudioOnly = true,
            SkipMerge = true
        };

        // Act
        var result = ArgsBuilder.Build(options);

        // Assert
        Assert.Contains("--del-after-done", result);
        Assert.Contains("--select-audio \".*\"", result);
        Assert.Contains("--drop-video \".*\"", result);
        Assert.Contains("--skip-merge", result);
    }

    [Fact]
    public void Build_WithTimeRange_ShouldIncludeRangeParameter()
    {
        // Arrange
        var options = new DownloadOptions
        {
            Input = "https://example.com/video.m3u8",
            RangeStart = "00:05:00",
            RangeEnd = "00:10:00"
        };

        // Act
        var result = ArgsBuilder.Build(options);

        // Assert
        Assert.Contains("--custom-range \"00:05:00-00:10:00\"", result);
    }

    [Fact]
    public void Build_WithDefaultTimeRange_ShouldNotIncludeRangeParameter()
    {
        // Arrange
        var options = new DownloadOptions
        {
            Input = "https://example.com/video.m3u8",
            RangeStart = "00:00:00",
            RangeEnd = "00:00:00"
        };

        // Act
        var result = ArgsBuilder.Build(options);

        // Assert
        Assert.DoesNotContain("--custom-range", result);
    }

    [Fact]
    public void Build_WithSubtitleOptions_ShouldIncludeSubtitleParameters()
    {
        // Arrange
        var options = new DownloadOptions
        {
            Input = "https://example.com/video.m3u8",
            SubOnly = true,
            SubFormat = "VTT",
            AutoSubtitleFix = true
        };

        // Act
        var result = ArgsBuilder.Build(options);

        // Assert
        Assert.Contains("--sub-only", result);
        Assert.Contains("--sub-format VTT", result);
        Assert.Contains("--auto-subtitle-fix", result);
    }

    [Fact]
    public void Build_WithEmptyInput_ShouldReturnDefaultSettings()
    {
        // Arrange
        var options = new DownloadOptions
        {
            ThreadCount = 12,
            DownloadRetryCount = 3,
            HttpRequestTimeout = 100
        };

        // Act
        var result = ArgsBuilder.Build(options);

        // Assert
        Assert.Contains("--thread-count 12", result);
        Assert.Contains("--download-retry-count 3", result);
        Assert.Contains("--http-request-timeout 100", result);
    }

    [Fact]
    public void Build_WithNullInput_ShouldHandleGracefully()
    {
        // Arrange
        var options = new DownloadOptions 
        { 
            Input = null,
            ThreadCount = 12 
        };

        // Act
        var result = ArgsBuilder.Build(options);

        // Assert
        Assert.Contains("--thread-count 12", result);
    }

    [Fact]
    public void Build_WithSubOnlyAndSubFormat_ShouldIncludeBoth()
    {
        // Arrange
        var options = new DownloadOptions
        {
            Input = "https://example.com/video.m3u8",
            SubOnly = true,
            SubFormat = "VTT"
        };

        // Act
        var result = ArgsBuilder.Build(options);

        // Assert
        Assert.Contains("--sub-only", result);
        Assert.Contains("--sub-format VTT", result);
    }

    [Fact]
    public void Build_WithSubFormatButNotSubOnly_ShouldNotIncludeSubFormat()
    {
        // Arrange
        var options = new DownloadOptions
        {
            Input = "https://example.com/video.m3u8",
            SubOnly = false,
            SubFormat = "VTT"
        };

        // Act
        var result = ArgsBuilder.Build(options);

        // Assert
        Assert.DoesNotContain("--sub-only", result);
        Assert.DoesNotContain("--sub-format", result);
    }

    [Fact]
    public void Build_WithAllBooleanFlags_ShouldIncludeAllFlags()
    {
        // Arrange
        var options = new DownloadOptions
        {
            Input = "https://example.com/video.m3u8",
            DelAfterDone = true,
            NoDateInfo = true,
            UseSystemProxy = false,
            SkipDownload = true,
            SkipMerge = true,
            BinaryMerge = true,
            AudioOnly = true,
            CheckSegmentsCount = false,
            ConcurrentDownload = true,
            SubOnly = true,
            AutoSubtitleFix = true,
            AutoSelect = true
        };

        // Act
        var result = ArgsBuilder.Build(options);

        // Assert
        Assert.Contains("--del-after-done", result);
        Assert.Contains("--no-date-info", result);
        Assert.Contains("--use-system-proxy false", result);
        Assert.Contains("--skip-download", result);
        Assert.Contains("--skip-merge", result);
        Assert.Contains("--binary-merge", result);
        Assert.Contains("--select-audio \".*\"", result);
        Assert.Contains("--check-segments-count false", result);
        Assert.Contains("--concurrent-download", result);
        Assert.Contains("--sub-only", result);
        Assert.Contains("--auto-subtitle-fix", result);
        Assert.Contains("--auto-select", result);
    }

    [Fact]
    public void Build_WithLiveStreamingOptions_ShouldIncludeLiveFlags()
    {
        // Arrange
        var options = new DownloadOptions
        {
            Input = "https://example.com/live.m3u8",
            LivePerformAsVod = true,
            LiveRealTimeMerge = true,
            LiveKeepSegments = false,
            LiveRecordLimit = "01:00:00"
        };

        // Act
        var result = ArgsBuilder.Build(options);

        // Assert
        Assert.Contains("--live-perform-as-vod", result);
        Assert.Contains("--live-real-time-merge", result);
        Assert.Contains("--live-keep-segments false", result);
        Assert.Contains("--live-record-limit \"01:00:00\"", result);
    }

    [Fact]
    public void Build_WithMuxOptions_ShouldIncludeMuxFlags()
    {
        // Arrange
        var options = new DownloadOptions
        {
            Input = "https://example.com/video.m3u8",
            MuxAfterDone = true,
            MuxFormat = "mp4",
            Muxer = "ffmpeg",
            MuxKeepFiles = true
        };

        // Act
        var result = ArgsBuilder.Build(options);

        // Assert
        Assert.Contains("--mux-after-done format=mp4:muxer=ffmpeg:keep=true", result);
    }

    [Fact]
    public void Build_WithMaxSpeed_ShouldIncludeSpeedLimit()
    {
        // Arrange
        var options = new DownloadOptions
        {
            Input = "https://example.com/video.m3u8",
            MaxSpeed = "15M"
        };

        // Act
        var result = ArgsBuilder.Build(options);

        // Assert
        Assert.Contains("--max-speed 15M", result);
    }
}
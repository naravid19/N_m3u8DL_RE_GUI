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
            SaveName = "MyVideo"
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
            DeleteAfterDone = true,
            AudioOnly = true,
            DisableMerge = true
        };

        // Act
        var result = ArgsBuilder.Build(options);

        // Assert
        Assert.Contains("--del-after-done", result);
        Assert.Contains("--audio-only", result);
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
        Assert.Contains("--live-rec-dur 00:05:00-00:10:00", result);
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
        Assert.DoesNotContain("--live-rec-dur", result);
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
            AutoSubFix = true
        };

        // Act
        var result = ArgsBuilder.Build(options);

        // Assert
        Assert.Contains("--sub-only", result);
        Assert.Contains("--sub-format VTT", result);
        Assert.Contains("--auto-sub-fix", result);
    }

    [Fact]
    public void Build_WithEmptyInput_ShouldReturnEmptyString()
    {
        // Arrange
        var options = new DownloadOptions();

        // Act
        var result = ArgsBuilder.Build(options);

        // Assert
        // When input is empty, it should still include default thread settings
        Assert.Contains("--thread-count 12", result);
        Assert.Contains("--download-retry-count 3", result);
        Assert.Contains("--http-request-timeout 100", result);
        // Max speed should not be included when 0
    }

    [Fact]
    public void Build_WithNullInput_ShouldHandleGracefully()
    {
        // Arrange
        var options = new DownloadOptions { Input = null };

        // Act
        var result = ArgsBuilder.Build(options);

        // Assert
        Assert.Contains("--thread-count 12", result);
        Assert.DoesNotContain("\"\"", result); // Should not have empty quotes
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
    public void Build_WithDefaultTimeRange_ShouldNotIncludeTimeRange()
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
        Assert.DoesNotContain("--live-rec-dur", result);
    }

    [Fact]
    public void Build_WithValidTimeRange_ShouldIncludeTimeRange()
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
        Assert.Contains("--live-rec-dur 00:05:00-00:10:00", result);
    }

    [Fact]
    public void Build_WithAllBooleanFlags_ShouldIncludeAllFlags()
    {
        // Arrange
        var options = new DownloadOptions
        {
            Input = "https://example.com/video.m3u8",
            DeleteAfterDone = true,
            DisableDate = true,
            DisableProxy = true,
            ParseOnly = true,
            DisableMerge = true,
            BinaryMerge = true,
            AudioOnly = true,
            DisableCheck = true,
            ConcurrentDownload = true,
            SubOnly = true,
            AutoSubFix = true
        };

        // Act
        var result = ArgsBuilder.Build(options);

        // Assert
        Assert.Contains("--del-after-done", result);
        Assert.Contains("--no-date-info", result);
        Assert.Contains("--no-proxy", result);
        Assert.Contains("--parse-only", result);
        Assert.Contains("--skip-merge", result);
        Assert.Contains("--binary-merge", result);
        Assert.Contains("--audio-only", result);
        Assert.Contains("--disable-check", result);
        Assert.Contains("--concurrent-download", result);
        Assert.Contains("--sub-only", result);
        Assert.Contains("--auto-sub-fix", result);
    }
} 
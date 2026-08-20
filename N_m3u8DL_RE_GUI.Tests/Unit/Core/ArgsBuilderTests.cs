using N_m3u8DL_RE_GUI.Core;
using Xunit;

namespace N_m3u8DL_RE_GUI.Tests.Unit.Core;

/// <summary>
/// Unit tests for ArgsBuilder.
/// </summary>
public class ArgsBuilderTests
{
    private static int NonDefaultThreadCount => Environment.ProcessorCount == 1 ? 2 : 1;

    [Fact]
    public void Build_WithBasicOptions_ShouldGenerateCorrectArguments()
    {
        // Arrange
        var options = new DownloadOptions
        {
            Input = "https://example.com/video.m3u8",
            SaveDir = "C:\\Downloads",
            SaveName = "MyVideo",
            ThreadCount = NonDefaultThreadCount
        };

        // Act
        var result = ArgsBuilder.Build(options);

        // Assert
        Assert.Contains("\"https://example.com/video.m3u8\"", result);
        Assert.Contains("--save-dir \"C:\\Downloads\"", result);
        Assert.Contains("--save-name \"MyVideo\"", result);
        Assert.Contains($"--thread-count {NonDefaultThreadCount}", result);
    }

    [Fact]
    public void Build_WithMultipleHeaders_ShouldGenerateMultipleHFlags()
    {
        var options = new DownloadOptions
        {
            Input = "https://example.com/video.m3u8",
            Headers = "Cookie: session=123|User-Agent: Mozilla/5.0|Origin: https://example.com"
        };

        var result = ArgsBuilder.Build(options);

        Assert.Contains("-H \"Cookie: session=123\"", result);
        Assert.Contains("-H \"User-Agent: Mozilla/5.0\"", result);
        Assert.Contains("-H \"Origin: https://example.com\"", result);
    }

    [Fact]
    public void Build_WithMultipleKeys_ShouldGenerateMultipleKeyFlags()
    {
        var options = new DownloadOptions
        {
            Input = "https://example.com/video.m3u8",
            Key = "key1:val1|key2:val2"
        };

        var result = ArgsBuilder.Build(options);

        Assert.Contains("--key \"key1:val1\"", result);
        Assert.Contains("--key \"key2:val2\"", result);
    }

    [Fact]
    public void Build_WithDecryptionOptions_ShouldIncludeDecryptionFlags()
    {
        var options = new DownloadOptions
        {
            Input = "https://example.com/video.m3u8",
            DecryptionEngine = "SHAKA_PACKAGER",
            DecryptionBinaryPath = @"C:\Tools\shaka.exe",
            MP4RealTimeDecryption = true,
            CustomHLSMethod = "AES_128",
            CustomHLSKey = "my-key",
            CustomHLSIv = "my-iv",
            KeyTextFile = @"C:\keys\keys.txt"
        };

        var result = ArgsBuilder.Build(options);

        Assert.Contains("--decryption-engine SHAKA_PACKAGER", result);
        Assert.Contains("--decryption-binary-path \"C:\\Tools\\shaka.exe\"", result);
        Assert.Contains("--mp4-real-time-decryption", result);
        Assert.Contains("--custom-hls-method \"AES_128\"", result);
        Assert.Contains("--custom-hls-key \"my-key\"", result);
        Assert.Contains("--custom-hls-iv \"my-iv\"", result);
        Assert.Contains("--key-text-file \"C:\\keys\\keys.txt\"", result);
    }

    [Fact]
    public void Build_WithBooleanOptions_ShouldIncludeEnabledFlags()
    {
        // Arrange
        var options = new DownloadOptions
        {
            Input = "https://example.com/video.m3u8",
            DelAfterDone = true,
            SelectAudio = "best",
            DropVideo = "true",
            SkipMerge = true
        };

        // Act
        var result = ArgsBuilder.Build(options);

        // Assert
        Assert.Contains("--del-after-done", result);
        Assert.Contains("--select-audio \"best\"", result);
        Assert.Contains("--drop-video \"true\"", result);
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
            ThreadCount = NonDefaultThreadCount,
            DownloadRetryCount = 5,
            HttpRequestTimeout = 200
        };

        // Act
        var result = ArgsBuilder.Build(options);

        // Assert
        Assert.Contains($"--thread-count {NonDefaultThreadCount}", result);
        Assert.Contains("--download-retry-count 5", result);
        Assert.Contains("--http-request-timeout 200", result);
    }

    [Fact]
    public void Build_WithNullInput_ShouldHandleGracefully()
    {
        // Arrange
        var options = new DownloadOptions 
        { 
            Input = null,
            ThreadCount = NonDefaultThreadCount
        };

        // Act
        var result = ArgsBuilder.Build(options);

        // Assert
        Assert.Contains($"--thread-count {NonDefaultThreadCount}", result);
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
            SelectAudio = "best",
            DropVideo = "true",
            CheckSegmentsCount = false,
            ConcurrentDownload = true,
            SubOnly = true,
            AutoSubtitleFix = true,
            AutoSelect = true,
            UseFFmpegConcatDemuxer = true,
            AllowHlsMultiExtMap = true,
            NoLog = true,
            AppendUrlParams = true,
            DisableUpdateCheck = true
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
        Assert.Contains("--select-audio \"best\"", result);
        Assert.Contains("--check-segments-count false", result);
        Assert.Contains("--concurrent-download", result);
        Assert.Contains("--sub-only", result);
        Assert.Contains("--auto-subtitle-fix", result);
        Assert.Contains("--auto-select", result);
        Assert.Contains("--use-ffmpeg-concat-demuxer", result);
        Assert.Contains("--allow-hls-multi-ext-map", result);
        Assert.Contains("--no-log", result);
        Assert.Contains("--append-url-params", result);
        Assert.Contains("--disable-update-check", result);
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
            LivePipeMux = true,
            LiveFixVttByAudio = true,
            LiveRecordLimit = "01:00:00",
            LiveWaitTime = 15,
            LiveTakeCount = 32
        };

        // Act
        var result = ArgsBuilder.Build(options);

        // Assert
        Assert.Contains("--live-perform-as-vod", result);
        Assert.Contains("--live-real-time-merge", result);
        Assert.Contains("--live-keep-segments false", result);
        Assert.Contains("--live-pipe-mux", result);
        Assert.Contains("--live-fix-vtt-by-audio", result);
        Assert.Contains("--live-record-limit \"01:00:00\"", result);
        Assert.Contains("--live-wait-time 15", result);
        Assert.Contains("--live-take-count 32", result);
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
    public void Build_WithMuxSkipSubtitle_ShouldIncludeSkipSubInMuxFlags()
    {
        // Arrange
        var options = new DownloadOptions
        {
            Input = "https://example.com/video.m3u8",
            MuxAfterDone = true,
            MuxFormat = "mkv",
            Muxer = "mkvmerge",
            MuxSkipSubtitle = true
        };

        // Act
        var result = ArgsBuilder.Build(options);

        // Assert
        Assert.Contains("--mux-after-done format=mkv:muxer=mkvmerge:skip_sub=true", result);
    }

    [Fact]
    public void Build_WithMuxImport_ShouldIncludeMuxImportFlag()
    {
        var options = new DownloadOptions
        {
            Input = "https://example.com/video.m3u8",
            MuxImport = @"C:\subs\episode01.srt"
        };

        var result = ArgsBuilder.Build(options);

        Assert.Contains("--mux-import \"C:\\subs\\episode01.srt\"", result);
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

    [Fact]
    public void Build_WithParityOptions_ShouldIncludeNewArguments()
    {
        // Arrange
        var options = new DownloadOptions
        {
            Input = "https://example.com/video.m3u8",
            TmpDir = "C:\\Temp\\Segments",
            CustomHLSKey = "ABCD1234",
            UrlProcessorArgs = "--token abc",
            TaskStartAt = "20260301101530",
            ForceAnsiConsole = true,
            NoAnsiColor = true
        };

        // Act
        var result = ArgsBuilder.Build(options);

        // Assert
        Assert.Contains("--tmp-dir \"C:\\Temp\\Segments\"", result);
        Assert.Contains("--custom-hls-key \"ABCD1234\"", result);
        Assert.Contains("--urlprocessor-args \"--token abc\"", result);
        Assert.Contains("--task-start-at \"20260301101530\"", result);
        Assert.Contains("--force-ansi-console", result);
        Assert.Contains("--no-ansi-color", result);
    }

    [Fact]
    public void NormalizeSaveDir_ShouldPreserveDriveRoot()
    {
        Assert.Equal("C:\\", OptionValueNormalizer.NormalizeSaveDir("C:\\"));
    }

    [Fact]
    public void NormalizeSaveDir_ShouldTrimTrailingSeparatorsForNormalPath()
    {
        Assert.Equal("C:\\Downloads", OptionValueNormalizer.NormalizeSaveDir("C:\\Downloads\\"));
    }

    [Fact]
    public void Build_WithRootSaveDir_ShouldEscapeTrailingBackslashInQuotedArgument()
    {
        var options = new DownloadOptions
        {
            Input = "https://example.com/video.m3u8",
            SaveDir = @"C:\"
        };

        var result = ArgsBuilder.Build(options);

        Assert.Contains("--save-dir \"C:\\\\\"", result);
    }

    [Fact]
    public void Build_WithQuotedValue_ShouldEscapeInnerDoubleQuotes()
    {
        var options = new DownloadOptions
        {
            Input = "https://example.com/video.m3u8",
            SaveName = "My \"Video\""
        };

        var result = ArgsBuilder.Build(options);

        Assert.Contains("--save-name \"My \\\"Video\\\"\"", result);
    }

    [Fact]
    public void Build_WithLogFilePath_ShouldIncludeLogFilePathArgument()
    {
        var options = new DownloadOptions
        {
            Input = "https://example.com/video.m3u8",
            LogFilePath = @"C:\Logs\download.log"
        };

        var result = ArgsBuilder.Build(options);

        Assert.Contains("--log-file-path", result);
        Assert.Contains(@"C:\Logs\download.log", result);
    }

    [Fact]
    public void Build_WithEmptyLogFilePath_ShouldNotIncludeLogFilePathArgument()
    {
        var options = new DownloadOptions
        {
            Input = "https://example.com/video.m3u8",
            LogFilePath = ""
        };

        var result = ArgsBuilder.Build(options);

        Assert.DoesNotContain("--log-file-path", result);
    }

    [Fact]
    public void Build_WithMkvmergeBinaryPath_ShouldIncludeMkvmergeBinaryPathArgument()
    {
        var options = new DownloadOptions
        {
            Input = "https://example.com/video.m3u8",
            MkvmergeBinaryPath = @"C:\Tools\mkvmerge.exe"
        };

        var result = ArgsBuilder.Build(options);

        Assert.Contains("--mkvmerge-binary-path", result);
        Assert.Contains(@"C:\Tools\mkvmerge.exe", result);
    }

    [Fact]
    public void Build_HeadersSeparatedByNewline_ProducesOneFlagEach()
    {
        var options = new DownloadOptions
        {
            Input = "https://example.com/a.m3u8",
            Headers = "Referer: https://example.com/\nUser-Agent: Mozilla/5.0"
        };

        var args = ArgsBuilder.Build(options);

        Assert.Contains("-H \"Referer: https://example.com/\"", args);
        Assert.Contains("-H \"User-Agent: Mozilla/5.0\"", args);
    }

    [Fact]
    public void Build_HeadersSeparatedByPipe_StillWorkForExistingConfigs()
    {
        var options = new DownloadOptions
        {
            Input = "https://example.com/a.m3u8",
            Headers = "Referer: https://example.com/|User-Agent: Mozilla/5.0"
        };

        var args = ArgsBuilder.Build(options);

        Assert.Contains("-H \"Referer: https://example.com/\"", args);
        Assert.Contains("-H \"User-Agent: Mozilla/5.0\"", args);
    }

    [Fact]
    public void Build_CrLfSeparatedHeaders_DoNotProduceEmptyFlags()
    {
        var options = new DownloadOptions
        {
            Input = "https://example.com/a.m3u8",
            Headers = "Referer: https://example.com/\r\nUser-Agent: Mozilla/5.0"
        };

        var args = ArgsBuilder.Build(options);

        Assert.DoesNotContain("-H \"\"", args);
        Assert.Equal(2, args.Split("-H ").Length - 1);
    }
}

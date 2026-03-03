#nullable enable
namespace N_m3u8DL_RE_GUI.Core;

/// <summary>
/// Download configuration options for N_m3u8DL-RE.
/// Maps to all CLI options from the original tool.
/// </summary>
public class DownloadOptions
{
    // ============================================
    // Basic Settings
    // ============================================
    public string? Input { get; set; }
    public string? SaveDir { get; set; }
    public string? SaveName { get; set; }
    public string? SavePattern { get; set; }
    public string? TmpDir { get; set; }
    public string? BaseUrl { get; set; }
    public string? Headers { get; set; }
    
    // ============================================
    // Network Settings
    // ============================================
    public string? Proxy { get; set; }
    public bool UseSystemProxy { get; set; } = true;
    
    // ============================================
    // Thread & Performance Settings
    // ============================================
    public int ThreadCount { get; set; } = Environment.ProcessorCount;
    public int DownloadRetryCount { get; set; } = 3;
    public int HttpRequestTimeout { get; set; } = 100;
    public string? MaxSpeed { get; set; } // Format: "15M" or "100K"
    
    // ============================================
    // Time Range Settings
    // ============================================
    public string? RangeStart { get; set; }
    public string? RangeEnd { get; set; }
    
    /// <summary>
    /// Check if time range is set (not default 00:00:00).
    /// </summary>
    public bool HasTimeRange => 
        !string.IsNullOrWhiteSpace(RangeStart) && 
        !string.IsNullOrWhiteSpace(RangeEnd) &&
        (RangeStart != "00:00:00" || RangeEnd != "00:00:00");
    
    // ============================================
    // Encryption Settings
    // ============================================
    public string? Key { get; set; }
    public string? KeyTextFile { get; set; }
    public string? CustomHLSMethod { get; set; }
    public string? CustomHLSKey { get; set; }
    public string? CustomHLSIv { get; set; }
    public string DecryptionEngine { get; set; } = "MP4DECRYPT"; // MP4DECRYPT, SHAKA_PACKAGER, FFMPEG
    public string? DecryptionBinaryPath { get; set; }
    public bool MP4RealTimeDecryption { get; set; }
    
    // ============================================
    // Output & Merge Settings
    // ============================================
    public bool SkipMerge { get; set; }
    public bool SkipDownload { get; set; }
    public bool BinaryMerge { get; set; }
    public bool UseFFmpegConcatDemuxer { get; set; }
    public bool DelAfterDone { get; set; } = true;
    public bool NoDateInfo { get; set; }
    public bool WriteMetaJson { get; set; } = true;
    public bool CheckSegmentsCount { get; set; } = true;
    
    // ============================================
    // Stream Selection Settings
    // ============================================
    public bool AutoSelect { get; set; }
    public bool ConcurrentDownload { get; set; }
    public string? SelectVideo { get; set; }  // -sv --select-video
    public string? SelectAudio { get; set; }  // -sa --select-audio
    public string? SelectSubtitle { get; set; }  // -ss --select-subtitle
    public string? DropVideo { get; set; }  // -dv --drop-video
    public string? DropAudio { get; set; }  // -da --drop-audio
    public string? DropSubtitle { get; set; }  // -ds --drop-subtitle
    
    // ============================================
    // Subtitle Settings
    // ============================================
    public bool SubOnly { get; set; }
    public string SubFormat { get; set; } = "SRT"; // SRT, VTT
    public bool AutoSubtitleFix { get; set; } = true;
    
    // ============================================
    // Mux Settings
    // ============================================
    public bool MuxAfterDone { get; set; }
    public string? MuxFormat { get; set; } // mkv, mp4
    public string? Muxer { get; set; } // ffmpeg, mkvmerge
    public string? MuxBinPath { get; set; }
    public bool MuxKeepFiles { get; set; }
    public bool MuxSkipSubtitle { get; set; }
    public string? MuxImport { get; set; } // External files to import during muxing
    public string? FFmpegBinaryPath { get; set; }
    
    // ============================================
    // Live Streaming Settings
    // ============================================
    public bool LivePerformAsVod { get; set; }
    public bool LiveRealTimeMerge { get; set; }
    public bool LiveKeepSegments { get; set; } = true;
    public bool LivePipeMux { get; set; }
    public bool LiveFixVttByAudio { get; set; }
    public string? LiveRecordLimit { get; set; } // Format: HH:mm:ss
    public int? LiveWaitTime { get; set; } // Seconds
    public int LiveTakeCount { get; set; } = 16;
    
    // ============================================
    // Advanced Settings
    // ============================================
    public string LogLevel { get; set; } = "INFO"; // DEBUG, ERROR, INFO, OFF, WARN
    public string? UILanguage { get; set; } // en-US, zh-CN, zh-TW
    public string? UrlProcessorArgs { get; set; }
    public string? AdKeyword { get; set; }
    public string? TaskStartAt { get; set; } // Format: yyyyMMddHHmmss
    public bool AppendUrlParams { get; set; }
    public bool NoLog { get; set; }
    public bool AllowHlsMultiExtMap { get; set; }
    public bool ForceAnsiConsole { get; set; }
    public bool NoAnsiColor { get; set; }
    public bool DisableUpdateCheck { get; set; }
    
    // ============================================
    // Legacy Properties (for backward compatibility)
    // ============================================
    [Obsolete("Use ThreadCount instead")]
    public int MaxThreads { get => ThreadCount; set => ThreadCount = value; }
    
    [Obsolete("Use DownloadRetryCount instead")]
    public int RetryCount { get => DownloadRetryCount; set => DownloadRetryCount = value; }
    
    [Obsolete("Use HttpRequestTimeout instead")]
    public int Timeout { get => HttpRequestTimeout; set => HttpRequestTimeout = value; }
    
    [Obsolete("Use DelAfterDone instead")]
    public bool DeleteAfterDone { get => DelAfterDone; set => DelAfterDone = value; }
    
    [Obsolete("Use NoDateInfo instead")]
    public bool DisableDate { get => NoDateInfo; set => NoDateInfo = value; }
    
    [Obsolete("Use !UseSystemProxy instead")]
    public bool DisableProxy { get => !UseSystemProxy; set => UseSystemProxy = !value; }
    
    [Obsolete("Use SkipDownload instead")]
    public bool ParseOnly { get => SkipDownload; set => SkipDownload = value; }
    
    [Obsolete("Use SkipMerge instead")]
    public bool DisableMerge { get => SkipMerge; set => SkipMerge = value; }
    
    [Obsolete("Use SelectAudio + DropVideo pattern instead")]
    public bool AudioOnly { get; set; }
    
    [Obsolete("Use !CheckSegmentsCount instead")]
    public bool DisableCheck { get => !CheckSegmentsCount; set => CheckSegmentsCount = !value; }
    
    [Obsolete("Use AutoSubtitleFix instead")]
    public bool AutoSubFix { get => AutoSubtitleFix; set => AutoSubtitleFix = value; }
    
    [Obsolete("Use CustomHLSIv instead")]
    public string? IV { get => CustomHLSIv; set => CustomHLSIv = value; }
    
    [Obsolete("Use MuxImport instead")]
    public string? MuxJson { get => MuxImport; set => MuxImport = value; }
}
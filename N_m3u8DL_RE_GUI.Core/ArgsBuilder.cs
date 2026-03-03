#nullable enable
using System.Text;

namespace N_m3u8DL_RE_GUI.Core;

/// <summary>
/// Build command-line arguments for N_m3u8DL-RE.
/// </summary>
public static class ArgsBuilder
{
    /// <summary>
    /// Build command-line arguments from download options.
    /// </summary>
    /// <param name="options">Download configuration options</param>
    /// <returns>Formatted command-line arguments string</returns>
    public static string Build(DownloadOptions options)
    {
        var sb = new StringBuilder();
        
        // ============================================
        // Input URL (required)
        // ============================================
        sb.AppendQuoted(options.Input);
        
        // ============================================
        // Basic Settings
        // ============================================
        sb.AppendIfNotEmpty("--tmp-dir", options.TmpDir);
        sb.AppendIfNotEmpty("--save-dir", options.SaveDir);
        sb.AppendIfNotEmpty("--save-name", options.SaveName);
        sb.AppendIfNotEmpty("--save-pattern", options.SavePattern);
        sb.AppendIfNotEmpty("--base-url", options.BaseUrl);
        
        // Headers - RE supports multiple -H "key: value"
        if (!string.IsNullOrWhiteSpace(options.Headers))
        {
            var headers = options.Headers.Split('|');
            foreach (var header in headers)
            {
                if (!string.IsNullOrWhiteSpace(header))
                {
                    sb.Append(" -H").AppendQuoted(header.Trim());
                }
            }
        }
        
        // ============================================
        // Thread & Performance Settings
        // ============================================
        // Only emit if different from N_m3u8DL-RE defaults
        if (options.ThreadCount > 0 && options.ThreadCount != Environment.ProcessorCount)
            sb.Append($" --thread-count {options.ThreadCount}");
        
        if (options.DownloadRetryCount > 0 && options.DownloadRetryCount != 3)
            sb.Append($" --download-retry-count {options.DownloadRetryCount}");
        
        if (options.HttpRequestTimeout > 0 && options.HttpRequestTimeout != 100)
            sb.Append($" --http-request-timeout {options.HttpRequestTimeout}");
        
        // Max Speed - format like "15M" or "100K"
        if (!string.IsNullOrWhiteSpace(options.MaxSpeed) && options.MaxSpeed != "0")
            sb.Append($" --max-speed {options.MaxSpeed}");
        
        // ============================================
        // Network Settings
        // ============================================
        sb.AppendIfNotEmpty("--custom-proxy", options.Proxy);
        
        if (!options.UseSystemProxy)
            sb.Append(" --use-system-proxy false");
        
        // ============================================
        // Time Range Settings
        // ============================================
        if (options.HasTimeRange)
        {
            sb.Append($" --custom-range \"{options.RangeStart}-{options.RangeEnd}\"");
        }
        
        // ============================================
        // Encryption Settings
        // ============================================
        if (!string.IsNullOrWhiteSpace(options.Key))
        {
            // Support multiple keys separated by |
            var keys = options.Key.Split('|');
            foreach (var key in keys)
            {
                if (!string.IsNullOrWhiteSpace(key))
                {
                    sb.Append(" --key").AppendQuoted(key.Trim());
                }
            }
        }
        
        sb.AppendIfNotEmpty("--key-text-file", options.KeyTextFile);
        sb.AppendIfNotEmpty("--custom-hls-method", options.CustomHLSMethod);
        sb.AppendIfNotEmpty("--custom-hls-key", options.CustomHLSKey);
        sb.AppendIfNotEmpty("--custom-hls-iv", options.CustomHLSIv);
        
        if (options.DecryptionEngine != "MP4DECRYPT")
            sb.Append($" --decryption-engine {options.DecryptionEngine}");
        
        sb.AppendIfNotEmpty("--decryption-binary-path", options.DecryptionBinaryPath);
        sb.AppendIfTrue("--mp4-real-time-decryption", options.MP4RealTimeDecryption);
        
        // ============================================
        // Output & Merge Settings
        // ============================================
        sb.AppendIfTrue("--skip-merge", options.SkipMerge);
        sb.AppendIfTrue("--skip-download", options.SkipDownload);
        sb.AppendIfTrue("--binary-merge", options.BinaryMerge);
        sb.AppendIfTrue("--use-ffmpeg-concat-demuxer", options.UseFFmpegConcatDemuxer);
        sb.AppendIfTrue("--del-after-done", options.DelAfterDone);
        sb.AppendIfTrue("--no-date-info", options.NoDateInfo);
        
        if (!options.WriteMetaJson)
            sb.Append(" --write-meta-json false");
        
        if (!options.CheckSegmentsCount)
            sb.Append(" --check-segments-count false");
        
        sb.AppendIfNotEmpty("--ffmpeg-binary-path", options.FFmpegBinaryPath);
        
        // ============================================
        // Stream Selection Settings
        // ============================================
        sb.AppendIfTrue("--auto-select", options.AutoSelect);
        sb.AppendIfTrue("--concurrent-download", options.ConcurrentDownload);
        
        // Advanced stream selection
        sb.AppendIfNotEmpty("--select-video", options.SelectVideo);
        sb.AppendIfNotEmpty("--select-audio", options.SelectAudio);
        sb.AppendIfNotEmpty("--select-subtitle", options.SelectSubtitle);
        sb.AppendIfNotEmpty("--drop-video", options.DropVideo);
        sb.AppendIfNotEmpty("--drop-audio", options.DropAudio);
        sb.AppendIfNotEmpty("--drop-subtitle", options.DropSubtitle);
        
        // Note: AudioOnly checkbox is now mapped to SelectAudio + DropVideo
        // directly in BuildArgsRE(), so no legacy conversion needed here.
        
        // ============================================
        // Subtitle Settings
        // ============================================
        if (options.SubOnly)
        {
            sb.Append(" --sub-only");
            if (!string.IsNullOrWhiteSpace(options.SubFormat))
                sb.Append($" --sub-format {options.SubFormat.ToUpper()}");
        }
        
        sb.AppendIfTrue("--auto-subtitle-fix", options.AutoSubtitleFix);
        
        // ============================================
        // Mux Settings
        // ============================================
        if (options.MuxAfterDone && !string.IsNullOrWhiteSpace(options.MuxFormat))
        {
            var muxOptions = new StringBuilder();
            muxOptions.Append($"format={options.MuxFormat.ToLower()}");
            
            if (!string.IsNullOrWhiteSpace(options.Muxer))
                muxOptions.Append($":muxer={options.Muxer.ToLower()}");
            
            if (!string.IsNullOrWhiteSpace(options.MuxBinPath))
                muxOptions.Append($":bin_path=\"{options.MuxBinPath}\"");
            
            if (options.MuxKeepFiles)
                muxOptions.Append(":keep=true");
            
            if (options.MuxSkipSubtitle)
                muxOptions.Append(":skip_sub=true");
            
            sb.Append($" --mux-after-done {muxOptions}");
        }
        
        // Mux Import - external files
        if (!string.IsNullOrWhiteSpace(options.MuxImport))
        {
            sb.Append(" --mux-import").AppendQuoted(options.MuxImport);
        }
        
        // ============================================
        // Live Streaming Settings
        // ============================================
        sb.AppendIfTrue("--live-perform-as-vod", options.LivePerformAsVod);
        sb.AppendIfTrue("--live-real-time-merge", options.LiveRealTimeMerge);
        
        if (!options.LiveKeepSegments)
            sb.Append(" --live-keep-segments false");
        
        sb.AppendIfTrue("--live-pipe-mux", options.LivePipeMux);
        sb.AppendIfTrue("--live-fix-vtt-by-audio", options.LiveFixVttByAudio);
        sb.AppendIfNotEmpty("--live-record-limit", options.LiveRecordLimit);
        
        if (options.LiveWaitTime.HasValue)
            sb.Append($" --live-wait-time {options.LiveWaitTime.Value}");
        
        if (options.LiveTakeCount != 16) // Only add if not default
            sb.Append($" --live-take-count {options.LiveTakeCount}");
        
        // ============================================
        // Advanced Settings
        // ============================================
        if (options.LogLevel != "INFO")
            sb.Append($" --log-level {options.LogLevel}");
        
        sb.AppendIfNotEmpty("--ui-language", options.UILanguage);
        sb.AppendIfNotEmpty("--urlprocessor-args", options.UrlProcessorArgs);
        sb.AppendIfNotEmpty("--ad-keyword", options.AdKeyword);
        sb.AppendIfNotEmpty("--task-start-at", options.TaskStartAt);
        sb.AppendIfTrue("--append-url-params", options.AppendUrlParams);
        sb.AppendIfTrue("--no-log", options.NoLog);
        sb.AppendIfTrue("--allow-hls-multi-ext-map", options.AllowHlsMultiExtMap);
        sb.AppendIfTrue("--force-ansi-console", options.ForceAnsiConsole);
        sb.AppendIfTrue("--no-ansi-color", options.NoAnsiColor);
        sb.AppendIfTrue("--disable-update-check", options.DisableUpdateCheck);
        
        return sb.ToString().Trim();
    }
}

/// <summary>
/// Extension methods for StringBuilder.
/// </summary>
public static class StringBuilderExtensions
{
    private static string QuoteForWindowsArgument(string value)
    {
        // Follow CommandLineToArgvW-compatible escaping rules.
        var quoted = new StringBuilder(value.Length + 2);
        quoted.Append('"');
        var pendingBackslashes = 0;

        foreach (var ch in value)
        {
            if (ch == '\\')
            {
                pendingBackslashes++;
                continue;
            }

            if (ch == '"')
            {
                quoted.Append('\\', pendingBackslashes * 2 + 1);
                quoted.Append('"');
                pendingBackslashes = 0;
                continue;
            }

            if (pendingBackslashes > 0)
            {
                quoted.Append('\\', pendingBackslashes);
                pendingBackslashes = 0;
            }

            quoted.Append(ch);
        }

        if (pendingBackslashes > 0)
            quoted.Append('\\', pendingBackslashes * 2);

        quoted.Append('"');
        return quoted.ToString();
    }

    /// <summary>
    /// Append a quoted string to StringBuilder.
    /// </summary>
    public static StringBuilder AppendQuoted(this StringBuilder sb, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return sb;

        return sb.Append(' ').Append(QuoteForWindowsArgument(value));
    }

    /// <summary>
    /// Append a flag and quoted value if the value is not empty.
    /// </summary>
    public static StringBuilder AppendIfNotEmpty(this StringBuilder sb, string flag, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            sb.Append($" {flag}").AppendQuoted(value);
        }
        return sb;
    }

    /// <summary>
    /// Append a flag if the condition is true.
    /// </summary>
    public static StringBuilder AppendIfTrue(this StringBuilder sb, string flag, bool condition)
    {
        if (condition)
        {
            sb.Append($" {flag}");
        }
        return sb;
    }
}

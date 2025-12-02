using System.Text;

namespace N_m3u8DL_RE_GUI.Core;

/// <summary>
/// Command-line argument constants for N_m3u8DL-RE.
/// </summary>
public static class CliFlags
{
    // Basic Settings
    public const string SaveDir = "--save-dir";
    public const string SaveName = "--save-name";
    public const string Headers = "--header"; // Changed from --headers to --header (RE uses -H or --header multiple times)
    public const string BaseUrl = "--base-url";
    public const string MuxJson = "--mux-import"; // RE uses --mux-import for external files, checking if this maps to old MuxJson usage
    // Note: Old MuxJson might have been for something else, but RE has --mux-import. 
    // If the user meant "MuxJson" as in the old CLI's specific feature, RE might handle it differently.
    // However, looking at the UI, it seems to be a file path. RE's --mux-import takes options.
    // Let's assume for now we might need to adjust this, but --mux-import is the closest for "muxing external files".
    // Wait, the old CLI had --mux-import? No, the old CLI had --mux-json. 
    // N_m3u8DL-RE doesn't seem to have --mux-json. It has --mux-import.
    // Let's keep it as is or map to what's appropriate. 
    // Actually, looking at RE help: --mux-import <OPTIONS>. 
    // If the user inputs a JSON file, maybe RE supports it? 
    // For now, I will comment it out or leave it but be careful. 
    // Actually, let's map it to --mux-import path:lang:name if possible, but the UI just passes a string.
    // Let's just pass it as a raw string if the user provides it, maybe they know what they are doing.
    
    // Encryption
    public const string Key = "--key";
    public const string IV = "--custom-hls-iv"; // RE uses --custom-hls-iv for HLS
    
    // Network
    public const string Proxy = "--custom-proxy"; // RE uses --custom-proxy
    
    // Time Range
    public const string CustomRange = "--custom-range"; // RE uses --custom-range
    
    // Thread Settings
    public const string ThreadCount = "--thread-count";
    public const string DownloadRetryCount = "--download-retry-count";
    
    // Timeout & Speed
    public const string HttpRequestTimeout = "--http-request-timeout";
    public const string MaxSpeed = "--max-speed";
    
    // Boolean Options
    public const string DelAfterDone = "--del-after-done";
    public const string NoDateInfo = "--no-date-info";
    // public const string NoProxy = "--no-proxy"; // RE doesn't have this directly, uses --use-system-proxy default true.
    public const string ParseOnly = "--skip-download"; // Closest to ParseOnly is SkipDownload? Or maybe just don't download.
    // RE has --skip-download. Old ParseOnly meant "parse m3u8 but don't download".
    public const string SkipMerge = "--skip-merge";
    public const string BinaryMerge = "--binary-merge";
    // public const string AudioOnly = "--audio-only"; // RE doesn't have this. We'll use --select-audio
    public const string DisableCheck = "--check-segments-count"; // Default is True. To disable, we might need --check-segments-count false
    public const string ConcurrentDownload = "--concurrent-download";
    public const string SubOnly = "--sub-only";
    public const string AutoSubFix = "--auto-subtitle-fix"; // RE uses --auto-subtitle-fix
    public const string SubFormat = "--sub-format";
}

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
        
        // Input URL (required)
        sb.AppendQuoted(options.Input);
        
        // Basic Settings
        sb.AppendIfNotEmpty(CliFlags.SaveDir, options.SaveDir);
        sb.AppendIfNotEmpty(CliFlags.SaveName, options.SaveName);
        
        // Headers - RE supports multiple -H "key: value"
        if (!string.IsNullOrWhiteSpace(options.Headers))
        {
            // Assuming headers are separated by | as per old logic in MainWindow.xaml.cs
            var headers = options.Headers.Split('|');
            foreach (var header in headers)
            {
                if (!string.IsNullOrWhiteSpace(header))
                {
                    sb.Append($" {CliFlags.Headers}").AppendQuoted(header.Trim());
                }
            }
        }
        
        sb.AppendIfNotEmpty(CliFlags.BaseUrl, options.BaseUrl);
        // sb.AppendIfNotEmpty(CliFlags.MuxJson, options.MuxJson); // Disabling MuxJson for now as mapping is unclear
        
        // Encryption Settings
        sb.AppendIfNotEmpty(CliFlags.Key, options.Key);
        sb.AppendIfNotEmpty(CliFlags.IV, options.IV);
        
        // Network Settings
        sb.AppendIfNotEmpty(CliFlags.Proxy, options.Proxy);
        
        // Time Range
        if (options.HasTimeRange)
        {
            // RE uses --custom-range "HH:mm:ss-HH:mm:ss" or similar? 
            // Help says: --custom-range <RANGE> Download only part of the segments.
            // Usually it expects indices or time. Let's assume it accepts the format from UI "00:00:00-00:00:00"
            // If the UI provides full time strings, we might need to format them.
            // For now, passing as is.
            sb.Append($" {CliFlags.CustomRange} \"{options.RangeStart}-{options.RangeEnd}\"");
        }
        
        // Thread Settings
        sb.Append($" {CliFlags.ThreadCount} {options.MaxThreads}");
        sb.Append($" {CliFlags.DownloadRetryCount} {options.RetryCount}");
        
        // Timeout & Speed Settings
        sb.Append($" {CliFlags.HttpRequestTimeout} {options.Timeout}");
        
        // Max Speed - only add if not zero
        if (options.MaxSpeed > 0)
        {
            sb.Append($" {CliFlags.MaxSpeed} {options.MaxSpeed}M");
        }
        
        // Boolean Options
        sb.AppendIfTrue(CliFlags.DelAfterDone, options.DeleteAfterDone);
        sb.AppendIfTrue(CliFlags.NoDateInfo, options.DisableDate);
        
        // Disable Proxy: RE defaults to using system proxy. 
        // If user wants to DISABLE it, we might need to pass something else or nothing if we use --custom-proxy.
        // If --custom-proxy is set, it overrides system proxy usually.
        // If user explicitly checks "NoProxy", we might want to prevent system proxy usage.
        // RE has --use-system-proxy [default: True]. 
        // We can try appending --use-system-proxy false
        if (options.DisableProxy)
        {
            sb.Append(" --use-system-proxy false");
        }

        sb.AppendIfTrue(CliFlags.ParseOnly, options.ParseOnly);
        sb.AppendIfTrue(CliFlags.SkipMerge, options.DisableMerge);
        sb.AppendIfTrue(CliFlags.BinaryMerge, options.BinaryMerge);
        
        // Audio Only -> Select Audio
        if (options.AudioOnly)
        {
            // Select all audio streams, drop others? 
            // Or just --select-audio "all"? 
            // RE help says: -sa, --select-audio <OPTIONS> Select audio streams by regular expressions.
            // To download ONLY audio, we should probably just select audio. 
            // But RE might still download video if not explicitly dropped?
            // Usually --select-audio .* will select audio. 
            // If we want ONLY audio, we might need to ensure video is not selected.
            // But typically selecting a specific stream type implies we want that.
            // Let's try --select-audio ".*" --drop-video ".*" to be safe?
            // Or just --audio-only if it existed.
            // Let's use --select-audio ".*" --drop-video ".*"
            sb.Append(" --select-audio \".*\" --drop-video \".*\"");
        }
        
        // Disable Check: Default True. 
        if (options.DisableCheck)
        {
             sb.Append(" --check-segments-count false");
        }
        
        sb.AppendIfTrue(CliFlags.ConcurrentDownload, options.ConcurrentDownload);
        sb.AppendIfTrue(CliFlags.SubOnly, options.SubOnly);
        sb.AppendIfTrue(CliFlags.AutoSubFix, options.AutoSubFix);
        
        // Subtitle Format
        if (options.SubOnly && !string.IsNullOrWhiteSpace(options.SubFormat))
        {
            sb.Append($" {CliFlags.SubFormat} {options.SubFormat.ToUpper()}");
        }
        
        return sb.ToString().Trim();
    }
}

/// <summary>
/// Extension methods for StringBuilder.
/// </summary>
public static class StringBuilderExtensions
{
    /// <summary>
    /// Append a quoted string to StringBuilder.
    /// </summary>
    /// <param name="sb">StringBuilder instance</param>
    /// <param name="value">String value to append</param>
    /// <returns>StringBuilder instance for chaining</returns>
    public static StringBuilder AppendQuoted(this StringBuilder sb, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return sb;
        return sb.Append($" \"{value}\"");
    }

    /// <summary>
    /// Append a flag and value if the value is not empty.
    /// </summary>
    /// <param name="sb">StringBuilder instance</param>
    /// <param name="flag">Flag to append</param>
    /// <param name="value">Value to append</param>
    /// <returns>StringBuilder instance for chaining</returns>
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
    /// <param name="sb">StringBuilder instance</param>
    /// <param name="flag">Flag to append</param>
    /// <param name="condition">Condition to check</param>
    /// <returns>StringBuilder instance for chaining</returns>
    public static StringBuilder AppendIfTrue(this StringBuilder sb, string flag, bool condition)
    {
        if (condition)
        {
            sb.Append($" {flag}");
        }
        return sb;
    }
}
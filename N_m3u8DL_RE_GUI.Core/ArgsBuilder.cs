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
    public const string Headers = "--headers";
    public const string BaseUrl = "--base-url";
    public const string MuxJson = "--mux-json";
    
    // Encryption
    public const string Key = "--key";
    public const string IV = "--iv";
    
    // Network
    public const string Proxy = "--proxy";
    
    // Time Range
    public const string LiveRecDur = "--live-rec-dur";
    
    // Thread Settings
    public const string MaxThreads = "--max-threads";
    public const string MinThreads = "--min-threads";
    public const string RetryCount = "--retry-count";
    
    // Timeout & Speed
    public const string Timeout = "--timeout";
    public const string StopSpeed = "--stop-speed";
    public const string MaxSpeed = "--max-speed";
    
    // Boolean Options
    public const string DelAfterDone = "--del-after-done";
    public const string DisableDate = "--disable-date";
    public const string NoProxy = "--no-proxy";
    public const string ParseOnly = "--parse-only";
    public const string FastStart = "--fast-start";
    public const string DisableMerge = "--disable-merge";
    public const string BinaryMerge = "--binary-merge";
    public const string AudioOnly = "--audio-only";
    public const string DisableCheck = "--disable-check";
    public const string ConcurrentDownload = "--concurrent-download";
    public const string SubOnly = "--sub-only";
    public const string AutoSubFix = "--auto-sub-fix";
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
        sb.AppendIfNotEmpty(CliFlags.Headers, options.Headers);
        sb.AppendIfNotEmpty(CliFlags.BaseUrl, options.BaseUrl);
        sb.AppendIfNotEmpty(CliFlags.MuxJson, options.MuxJson);
        
        // Encryption Settings
        sb.AppendIfNotEmpty(CliFlags.Key, options.Key);
        sb.AppendIfNotEmpty(CliFlags.IV, options.IV);
        
        // Network Settings
        sb.AppendIfNotEmpty(CliFlags.Proxy, options.Proxy);
        
        // Time Range
        if (options.HasTimeRange)
        {
            sb.Append($" {CliFlags.LiveRecDur} {options.RangeStart}-{options.RangeEnd}");
        }
        
        // Thread Settings
        sb.Append($" {CliFlags.MaxThreads} {options.MaxThreads}");
        sb.Append($" {CliFlags.MinThreads} {options.MinThreads}");
        sb.Append($" {CliFlags.RetryCount} {options.RetryCount}");
        
        // Timeout & Speed Settings
        sb.Append($" {CliFlags.Timeout} {options.Timeout}");
        sb.Append($" {CliFlags.StopSpeed} {options.StopSpeed}");
        sb.Append($" {CliFlags.MaxSpeed} {options.MaxSpeed}");
        
        // Boolean Options
        sb.AppendIfTrue(CliFlags.DelAfterDone, options.DeleteAfterDone);
        sb.AppendIfTrue(CliFlags.DisableDate, options.DisableDate);
        sb.AppendIfTrue(CliFlags.NoProxy, options.DisableProxy);
        sb.AppendIfTrue(CliFlags.ParseOnly, options.ParseOnly);
        sb.AppendIfTrue(CliFlags.FastStart, options.FastStart);
        sb.AppendIfTrue(CliFlags.DisableMerge, options.DisableMerge);
        sb.AppendIfTrue(CliFlags.BinaryMerge, options.BinaryMerge);
        sb.AppendIfTrue(CliFlags.AudioOnly, options.AudioOnly);
        sb.AppendIfTrue(CliFlags.DisableCheck, options.DisableCheck);
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
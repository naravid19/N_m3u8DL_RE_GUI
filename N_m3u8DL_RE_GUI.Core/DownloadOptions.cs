namespace N_m3u8DL_RE_GUI.Core;

/// <summary>
/// Download configuration options for N_m3u8DL-RE.
/// </summary>
public class DownloadOptions
{
    // Basic Settings
    public string? Input { get; set; }
    public string? SaveDir { get; set; }
    public string? SaveName { get; set; }
    public string? Headers { get; set; }
    public string? BaseUrl { get; set; }
    public string? MuxJson { get; set; }
    
    // Encryption
    public string? Key { get; set; }
    public string? IV { get; set; }
    
    // Network
    public string? Proxy { get; set; }
    
    // Time Range
    public string? RangeStart { get; set; }
    public string? RangeEnd { get; set; }
    
    /// <summary>
    /// Get time range as TimeSpan if valid.
    /// </summary>
    public (TimeSpan? Start, TimeSpan? End) GetTimeRange()
    {
        var start = TimeSpan.TryParse(RangeStart, out var startTime) ? startTime : (TimeSpan?)null;
        var end = TimeSpan.TryParse(RangeEnd, out var endTime) ? endTime : (TimeSpan?)null;
        return (start, end);
    }
    
    /// <summary>
    /// Check if time range is set (not default 00:00:00).
    /// </summary>
    public bool HasTimeRange => 
        !string.IsNullOrWhiteSpace(RangeStart) && 
        !string.IsNullOrWhiteSpace(RangeEnd) &&
        (RangeStart != "00:00:00" || RangeEnd != "00:00:00");
    
    // Thread Settings
    public int MaxThreads { get; set; } = 32;
    public int MinThreads { get; set; } = 16;
    public int RetryCount { get; set; } = 15;
    
    // Timeout & Speed
    public int Timeout { get; set; } = 10;
    public int StopSpeed { get; set; } = 0;
    public int MaxSpeed { get; set; } = 0;
    
    // Boolean Options
    public bool DeleteAfterDone { get; set; }
    public bool DisableDate { get; set; }
    public bool DisableProxy { get; set; }
    public bool ParseOnly { get; set; }
    public bool FastStart { get; set; }
    public bool DisableMerge { get; set; }
    public bool BinaryMerge { get; set; }
    public bool AudioOnly { get; set; }
    public bool DisableCheck { get; set; }
    public bool ConcurrentDownload { get; set; }
    public bool SubOnly { get; set; }
    public bool AutoSubFix { get; set; }
    
    // Subtitle Format
    public string SubFormat { get; set; } = "SRT";
} 
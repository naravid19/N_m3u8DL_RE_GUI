#nullable enable
namespace N_m3u8DL_RE_GUI.Tests.Fixtures;

/// <summary>
/// Shared constants and test URLs for unit and integration testing.
/// </summary>
public static class TestConstants
{
    // =========================================================================
    // Real Test URLs provided for Live/Integration Validation
    // =========================================================================
    
    /// <summary>
    /// Cloudflare-protected / Surrit video stream URL.
    /// </summary>
    public const string CfStreamUrl = "https://surrit.com/33ece07f-3229-41eb-b189-ec2485619e02/360p/video.m3u8";

    /// <summary>
    /// Referrer URL for Cloudflare / MissAV stream.
    /// </summary>
    public const string CfReferrerUrl = "https://missav123.com/";

    /// <summary>
    /// Normal open M3U8 video stream URL (AnimeIndy HLS).
    /// </summary>
    public const string NormalM3u8Url = "https://hls.animeindy.com:8443/vid/MN8fWZAdg/video.mp4/playlist.m3u8";

    // =========================================================================
    // Dummy Test Constants
    // =========================================================================
    
    public const string SampleM3u8Url = "https://example.com/stream/video.m3u8";
    public const string SampleMpdUrl = "https://example.com/stream/manifest.mpd";
    public const string SampleSaveDir = @"C:\Downloads\TestOutput";
    public const string SampleSaveName = "TestVideo_01";
}

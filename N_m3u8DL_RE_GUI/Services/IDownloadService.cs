using N_m3u8DL_RE_GUI.Core;

namespace N_m3u8DL_RE_GUI.Services;

/// <summary>
/// Interface for download service operations.
/// </summary>
public interface IDownloadService
{
    /// <summary>
    /// Start download process with given options.
    /// </summary>
    /// <param name="options">Download configuration</param>
    /// <param name="progressCallback">Progress callback</param>
    /// <param name="logCallback">Log callback</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Download result</returns>
    Task<bool> StartDownloadAsync(
        DownloadOptions options, 
        IProgress<int>? progressCallback = null,
        Action<string>? logCallback = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Stop current download process.
    /// </summary>
    void StopDownload();

    /// <summary>
    /// Check if download is currently running.
    /// </summary>
    bool IsDownloading { get; }
} 
#nullable enable
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
    /// <param name="progressCallback">Receives 0-100 as parsed from the child process output.</param>
    /// <param name="logCallback">Log callback</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Download result</returns>
    Task<bool> StartDownloadAsync(
        DownloadOptions options, 
        IProgress<int>? progressCallback = null,
        Action<string>? logCallback = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Start an arbitrary process (e.g. batch script or Python script) with process tree tracking.
    /// </summary>
    Task<bool> StartProcessAsync(
        string fileName,
        string arguments,
        Action<string>? logCallback = null,
        IProgress<int>? progressCallback = null,
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
#nullable enable
using N_m3u8DL_RE_GUI.Core;
using System.Diagnostics;

namespace N_m3u8DL_RE_GUI.Services;

/// <summary>
/// Implementation of download service using N_m3u8DL-RE executable.
/// </summary>
public class DownloadService : IDownloadService
{
    private Process? _currentProcess;
    private CancellationTokenSource? _cancellationTokenSource;
    private readonly object _lockObject = new();

    public bool IsDownloading
    {
        get
        {
            lock (_lockObject)
            {
                return _currentProcess != null && !_currentProcess.HasExited;
            }
        }
    }

    public async Task<bool> StartDownloadAsync(
        DownloadOptions options,
        IProgress<int>? progressCallback = null,
        Action<string>? logCallback = null,
        CancellationToken cancellationToken = default)
    {
        if (IsDownloading)
        {
            logCallback?.Invoke("Download is already in progress. Please wait for it to complete.");
            return false;
        }

        if (string.IsNullOrWhiteSpace(options.Input))
        {
            logCallback?.Invoke("Please enter a URL to download.");
            return false;
        }

        // Use options.ExePath if specified, otherwise fall back to default in working directory
        var exePath = string.IsNullOrWhiteSpace(options.ExePath) ? "N_m3u8DL-RE.exe" : options.ExePath;
        if (!System.IO.File.Exists(exePath))
        {
            logCallback?.Invoke($"File not found: {exePath}");
            logCallback?.Invoke("Please download N_m3u8DL-RE.exe from: https://github.com/nilaoda/N_m3u8DL-RE/releases");
            return false;
        }

        try
        {
            _cancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

            logCallback?.Invoke("Starting download...");

            var args = ArgsBuilder.Build(options);
            logCallback?.Invoke($"Command: {exePath} {args}");

            var startInfo = new ProcessStartInfo
            {
                FileName = exePath,
                Arguments = args,
                UseShellExecute = true // Launch visible console window showing N_m3u8DL-RE interactive progress UI
            };

            lock (_lockObject)
            {
                _currentProcess = new Process { StartInfo = startInfo };
            }

            if (!_currentProcess.Start())
            {
                logCallback?.Invoke("Failed to start the program.");
                return false;
            }

            try
            {
                await _currentProcess.WaitForExitAsync(_cancellationTokenSource.Token);
            }
            catch (OperationCanceledException)
            {
                logCallback?.Invoke("Download was cancelled.");
                return false;
            }

            var success = _currentProcess.ExitCode == 0;
            logCallback?.Invoke(success ? "Download completed!" : $"Download failed (Exit Code: {_currentProcess.ExitCode})");

            return success;
        }
        catch (OperationCanceledException)
        {
            logCallback?.Invoke("Download was cancelled.");
            return false;
        }
        catch (Exception ex)
        {
            logCallback?.Invoke($"Error occurred: {ex.Message}");
            return false;
        }
        finally
        {
            lock (_lockObject)
            {
                _currentProcess?.Dispose();
                _currentProcess = null;
            }
            _cancellationTokenSource?.Dispose();
            _cancellationTokenSource = null;
        }
    }

    public async Task<bool> StartProcessAsync(
        string fileName,
        string arguments,
        Action<string>? logCallback = null,
        CancellationToken cancellationToken = default)
    {
        if (IsDownloading)
        {
            logCallback?.Invoke("A process is already in progress. Please wait for it to complete.");
            return false;
        }

        if (string.IsNullOrWhiteSpace(fileName))
        {
            logCallback?.Invoke("Process target file path is required.");
            return false;
        }

        try
        {
            _cancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

            var startInfo = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                UseShellExecute = true // Ensures terminal windows or batch scripts render properly
            };

            lock (_lockObject)
            {
                _currentProcess = new Process { StartInfo = startInfo };
            }

            if (!_currentProcess.Start())
            {
                logCallback?.Invoke($"Failed to start process: {fileName}");
                return false;
            }

            try
            {
                await _currentProcess.WaitForExitAsync(_cancellationTokenSource.Token);
            }
            catch (OperationCanceledException)
            {
                logCallback?.Invoke("Process execution was cancelled.");
                return false;
            }

            var success = _currentProcess.ExitCode == 0;
            logCallback?.Invoke(success ? "Process finished successfully!" : $"Process exited with code: {_currentProcess.ExitCode}");

            return success;
        }
        catch (OperationCanceledException)
        {
            logCallback?.Invoke("Process execution was cancelled.");
            return false;
        }
        catch (Exception ex)
        {
            logCallback?.Invoke($"Process execution error: {ex.Message}");
            return false;
        }
        finally
        {
            lock (_lockObject)
            {
                _currentProcess?.Dispose();
                _currentProcess = null;
            }
            _cancellationTokenSource?.Dispose();
            _cancellationTokenSource = null;
        }
    }

    public void StopDownload()
    {
        lock (_lockObject)
        {
            if (_currentProcess != null && !_currentProcess.HasExited)
            {
                try
                {
                    // Kill the entire process tree to also terminate child processes
                    // (ffmpeg, mp4decrypt, etc.) spawned by N_m3u8DL-RE
                    _currentProcess.Kill(entireProcessTree: true);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Failed to stop download process: {ex.Message}");
                }
            }
        }

        _cancellationTokenSource?.Cancel();
    }
}

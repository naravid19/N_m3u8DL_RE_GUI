#nullable enable
using N_m3u8DL_RE_GUI.Core;
using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace N_m3u8DL_RE_GUI.Services;

/// <summary>
/// Implementation of download service using N_m3u8DL-RE executable or arbitrary processes.
/// Owns process lifecycle and process-tree cancellation safety.
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

    public Task<bool> StartDownloadAsync(
        DownloadOptions options,
        IProgress<int>? progressCallback = null,
        Action<string>? logCallback = null,
        CancellationToken cancellationToken = default)
    {
        if (IsDownloading)
        {
            logCallback?.Invoke("Download is already in progress. Please wait for it to complete.");
            return Task.FromResult(false);
        }

        if (string.IsNullOrWhiteSpace(options.Input))
        {
            logCallback?.Invoke("Please enter a URL to download.");
            return Task.FromResult(false);
        }

        // Use options.ExePath if specified, otherwise fall back to default in working directory
        var exePath = string.IsNullOrWhiteSpace(options.ExePath) ? "N_m3u8DL-RE.exe" : options.ExePath;
        if (!System.IO.File.Exists(exePath))
        {
            logCallback?.Invoke($"File not found: {exePath}");
            logCallback?.Invoke("Please download N_m3u8DL-RE.exe from: https://github.com/nilaoda/N_m3u8DL-RE/releases");
            return Task.FromResult(false);
        }

        logCallback?.Invoke("Starting download...");
        var args = ArgsBuilder.Build(options);
        logCallback?.Invoke($"Command: {exePath} {args}");

        var startInfo = new ProcessStartInfo
        {
            FileName = exePath,
            Arguments = args,
            UseShellExecute = true // Launch visible console window showing N_m3u8DL-RE interactive progress UI
        };

        return StartTrackedProcessAsync(startInfo, logCallback, cancellationToken);
    }

    public Task<bool> StartProcessAsync(
        string fileName,
        string arguments,
        Action<string>? logCallback = null,
        CancellationToken cancellationToken = default)
    {
        if (IsDownloading)
        {
            logCallback?.Invoke("A process is already in progress. Please wait for it to complete.");
            return Task.FromResult(false);
        }

        if (string.IsNullOrWhiteSpace(fileName))
        {
            logCallback?.Invoke("Process target file path is required.");
            return Task.FromResult(false);
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            UseShellExecute = true // Ensures terminal windows or batch scripts render properly
        };

        return StartTrackedProcessAsync(startInfo, logCallback, cancellationToken);
    }

    /// <summary>
    /// Extract single, synchronized process lifecycle logic for both entry points.
    /// Manages process tracking, cancellation token lifetime, and cleanup.
    /// </summary>
    private async Task<bool> StartTrackedProcessAsync(
        ProcessStartInfo startInfo,
        Action<string>? logCallback,
        CancellationToken cancellationToken)
    {
        Process? process = null;
        CancellationTokenSource? cts = null;

        lock (_lockObject)
        {
            if (_currentProcess != null && !_currentProcess.HasExited)
            {
                logCallback?.Invoke("A process is already in progress. Please wait for it to complete.");
                return false;
            }

            cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _cancellationTokenSource = cts;

            process = new Process { StartInfo = startInfo };
            _currentProcess = process;
        }

        try
        {
            if (!process.Start())
            {
                logCallback?.Invoke($"Failed to start process: {startInfo.FileName}");
                return false;
            }

            try
            {
                await process.WaitForExitAsync(cts.Token);
            }
            catch (OperationCanceledException)
            {
                logCallback?.Invoke("Process execution was cancelled.");
                return false;
            }

            var success = process.ExitCode == 0;
            logCallback?.Invoke(success ? "Process finished successfully!" : $"Process exited with code: {process.ExitCode}");
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
                if (_currentProcess == process)
                {
                    _currentProcess = null;
                }
                if (_cancellationTokenSource == cts)
                {
                    _cancellationTokenSource = null;
                }
            }

            try { process.Dispose(); } catch { }
            try { cts.Dispose(); } catch { }
        }
    }

    public void StopDownload()
    {
        Process? procToKill = null;
        CancellationTokenSource? ctsToCancel = null;

        lock (_lockObject)
        {
            procToKill = _currentProcess;
            ctsToCancel = _cancellationTokenSource;
        }

        if (ctsToCancel != null)
        {
            try
            {
                ctsToCancel.Cancel();
            }
            catch (ObjectDisposedException) { }
            catch (Exception ex)
            {
                Debug.WriteLine($"Token cancel error: {ex.Message}");
            }
        }

        if (procToKill != null)
        {
            try
            {
                if (!procToKill.HasExited)
                {
                    // Kill the entire process tree to also terminate child processes
                    // (ffmpeg, mp4decrypt, python, etc.)
                    procToKill.Kill(entireProcessTree: true);
                }
            }
            catch (InvalidOperationException) { }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to stop process tree: {ex.Message}");
            }
        }
    }
}

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

    private static bool SafeIsRunning(Process? process)
    {
        if (process == null) return false;
        try
        {
            return !process.HasExited;
        }
        catch
        {
            return false;
        }
    }

    public bool IsDownloading
    {
        get
        {
            lock (_lockObject)
            {
                return SafeIsRunning(_currentProcess);
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

        var exePath = string.IsNullOrWhiteSpace(options.ExePath) ? "N_m3u8DL-RE.exe" : options.ExePath;
        if (!System.IO.File.Exists(exePath))
        {
            logCallback?.Invoke($"File not found: {exePath}");
            logCallback?.Invoke("Please download N_m3u8DL-RE.exe from: https://github.com/nilaoda/N_m3u8DL-RE/releases");
            return false;
        }

        logCallback?.Invoke("Starting download...");
        var args = ArgsBuilder.Build(options);
        logCallback?.Invoke($"Command: {exePath} {args}");

        var startInfo = new ProcessStartInfo
        {
            FileName = exePath,
            Arguments = args,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = System.Text.Encoding.UTF8,
            StandardErrorEncoding = System.Text.Encoding.UTF8
        };

        var exitCode = await StartTrackedProcessWithExitCodeAsync(startInfo, logCallback, progressCallback, cancellationToken);
        return exitCode == 0;
    }

    public async Task<bool> StartProcessAsync(
        string fileName,
        string arguments,
        Action<string>? logCallback = null,
        IProgress<int>? progressCallback = null,
        CancellationToken cancellationToken = default)
    {
        var exitCode = await StartProcessWithExitCodeAsync(fileName, arguments, logCallback, progressCallback, cancellationToken);
        return exitCode == 0;
    }

    public Task<int> StartProcessWithExitCodeAsync(
        string fileName,
        string arguments,
        Action<string>? logCallback = null,
        IProgress<int>? progressCallback = null,
        CancellationToken cancellationToken = default)
    {
        if (IsDownloading)
        {
            logCallback?.Invoke("A process is already in progress. Please wait for it to complete.");
            return Task.FromResult(-1);
        }

        if (string.IsNullOrWhiteSpace(fileName))
        {
            logCallback?.Invoke("Process target file path is required.");
            return Task.FromResult(-1);
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = System.Text.Encoding.UTF8,
            StandardErrorEncoding = System.Text.Encoding.UTF8
        };

        return StartTrackedProcessWithExitCodeAsync(startInfo, logCallback, progressCallback, cancellationToken);
    }

    private async Task<int> StartTrackedProcessWithExitCodeAsync(
        ProcessStartInfo startInfo,
        Action<string>? logCallback,
        IProgress<int>? progressCallback,
        CancellationToken cancellationToken)
    {
        Process? process = null;
        CancellationTokenSource? cts = null;

        lock (_lockObject)
        {
            if (SafeIsRunning(_currentProcess))
            {
                logCallback?.Invoke("A process is already in progress. Please wait for it to complete.");
                return -1;
            }

            cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _cancellationTokenSource = cts;

            process = new Process { StartInfo = startInfo };
            _currentProcess = process;
        }

        try
        {
            process.OutputDataReceived += (_, e) => Forward(e.Data, logCallback, progressCallback);
            process.ErrorDataReceived += (_, e) => Forward(e.Data, logCallback, progressCallback);

            if (!process.Start())
            {
                logCallback?.Invoke($"Failed to start process: {startInfo.FileName}");
                return -1;
            }

            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            try
            {
                await process.WaitForExitAsync(cts.Token);
            }
            catch (OperationCanceledException)
            {
                logCallback?.Invoke("Process execution was cancelled.");
                return -1;
            }

            var exitCode = process.ExitCode;
            logCallback?.Invoke(exitCode == 0
                ? "Process finished successfully!"
                : $"Process exited with code: {exitCode}");

            if (exitCode == 0)
                progressCallback?.Report(100);

            return exitCode;
        }
        catch (OperationCanceledException)
        {
            logCallback?.Invoke("Process execution was cancelled.");
            return -1;
        }
        catch (Exception ex)
        {
            logCallback?.Invoke($"Process execution error: {ex.Message}");
            return -1;
        }
        finally
        {
            lock (_lockObject)
            {
                if (_currentProcess == process) _currentProcess = null;
                if (_cancellationTokenSource == cts) _cancellationTokenSource = null;
            }

            if (process != null)
            {
                try
                {
                    if (!process.HasExited)
                        process.Kill(entireProcessTree: true);
                }
                catch { }
                try { process.Dispose(); } catch { }
            }

            try { cts?.Dispose(); } catch { }
        }
    }

    private static void Forward(string? raw, Action<string>? logCallback, IProgress<int>? progressCallback)
    {
        if (raw == null) return;   // null marks end of stream

        var percent = ConsoleOutputParser.TryExtractPercent(raw);
        if (percent.HasValue)
            progressCallback?.Report(percent.Value);

        var cleaned = ConsoleOutputParser.Clean(raw);
        if (cleaned.Length > 0)
            logCallback?.Invoke(cleaned);
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
                Trace.WriteLine($"Token cancel error: {ex.Message}");
            }
        }

        if (procToKill != null)
        {
            try
            {
                if (SafeIsRunning(procToKill))
                {
                    // Kill the entire process tree to also terminate child processes
                    // (ffmpeg, mp4decrypt, python, etc.)
                    procToKill.Kill(entireProcessTree: true);
                }
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"Failed to stop process tree: {ex.Message}");
            }
        }
    }
}

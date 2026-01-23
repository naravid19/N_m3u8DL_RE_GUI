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

        // Check if N_m3u8DL-RE.exe exists
        var exePath = "N_m3u8DL-RE.exe";
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
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                StandardOutputEncoding = System.Text.Encoding.UTF8,
                StandardErrorEncoding = System.Text.Encoding.UTF8
            };

            lock (_lockObject)
            {
                _currentProcess = new Process { StartInfo = startInfo };
            }

            _currentProcess.OutputDataReceived += (sender, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data))
                {
                    logCallback?.Invoke(e.Data);
                    
                    // Parse progress from output
                    if (e.Data.Contains("%"))
                    {
                        var progressMatch = System.Text.RegularExpressions.Regex.Match(e.Data, @"(\d+(?:\.\d+)?)%");
                        if (progressMatch.Success && float.TryParse(progressMatch.Groups[1].Value, out float progress))
                        {
                            progressCallback?.Report((int)progress);
                        }
                    }
                }
            };

            _currentProcess.ErrorDataReceived += (sender, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data))
                {
                    logCallback?.Invoke($"ERROR: {e.Data}");
                }
            };

            if (!_currentProcess.Start())
            {
                logCallback?.Invoke("Failed to start the program.");
                return false;
            }

            _currentProcess.BeginOutputReadLine();
            _currentProcess.BeginErrorReadLine();

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

    public void StopDownload()
    {
        lock (_lockObject)
        {
            if (_currentProcess != null && !_currentProcess.HasExited)
            {
                try
                {
                    _currentProcess.Kill();
                }
                catch (Exception)
                {
                    // Ignore exceptions when killing process
                }
            }
        }

        _cancellationTokenSource?.Cancel();
    }
} 
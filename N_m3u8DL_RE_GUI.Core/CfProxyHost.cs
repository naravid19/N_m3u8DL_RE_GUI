#nullable enable
using System;
using System.Diagnostics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace N_m3u8DL_RE_GUI.Core;

public enum CfProxyStartStatus
{
    Ready,
    StartFailed,
    NotReady,
    Cancelled
}

/// <param name="Error">The reason when <see cref="CfProxyStartStatus.StartFailed"/> has one; otherwise null.</param>
public sealed record CfProxyStartResult(CfProxyStartStatus Status, CfProxyHost? Host, string? Error);

/// <summary>
/// Owns the lifetime of <c>m3u8_cf_bypass.py --serve</c>: starts it, waits for the
/// <c>PROXY_READY</c> handshake on stdout, keeps stdout drained afterwards and shuts
/// it down (stdin EOF, then a kill) when disposed. Lives outside the window so the
/// process protocol can be tested without a UI.
/// </summary>
public sealed class CfProxyHost : IAsyncDisposable
{
    private const string ReadyPrefix = "PROXY_READY ";

    /// <summary>How long the process gets to exit on its own after stdin is closed.</summary>
    public static readonly TimeSpan DefaultShutdownGrace = TimeSpan.FromSeconds(5);

    private readonly Process _process;
    private readonly CancellationTokenRegistration _killOnCancel;
    private readonly TimeSpan _shutdownGrace;

    /// <summary>The loopback URL N_m3u8DL-RE should download from.</summary>
    public string ProxyUrl { get; }

    private CfProxyHost(Process process, string proxyUrl, CancellationTokenRegistration killOnCancel, TimeSpan shutdownGrace)
    {
        _process = process;
        ProxyUrl = proxyUrl;
        _killOnCancel = killOnCancel;
        _shutdownGrace = shutdownGrace;
    }

    /// <summary>
    /// Start info for a process that speaks the proxy protocol: stdin/stdout/stderr are
    /// redirected (stdout is a machine channel, not a console) and Python is told to use UTF-8.
    /// </summary>
    public static ProcessStartInfo BuildStartInfo(string fileName, string arguments)
    {
        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = new UTF8Encoding(false),
            StandardErrorEncoding = new UTF8Encoding(false),
        };
        psi.EnvironmentVariables["PYTHONUTF8"] = "1";
        psi.EnvironmentVariables["PYTHONIOENCODING"] = "utf-8";
        return psi;
    }

    /// <summary>
    /// Starts the process and waits up to <paramref name="readyTimeout"/> for
    /// <c>PROXY_READY &lt;url&gt;</c>. Cancelling <paramref name="cancellationToken"/> also kills the
    /// process for as long as the returned host lives, so a Stop button reaches it mid-download.
    /// <paramref name="log"/> receives stdout lines until the handshake and every stderr line; it
    /// is called from thread-pool threads.
    /// </summary>
    public static async Task<CfProxyStartResult> StartAsync(
        ProcessStartInfo startInfo,
        Action<string> log,
        TimeSpan readyTimeout,
        CancellationToken cancellationToken,
        TimeSpan? shutdownGrace = null)
    {
        Process? process;
        try
        {
            process = Process.Start(startInfo);
        }
        catch (Exception ex)
        {
            return new CfProxyStartResult(CfProxyStartStatus.StartFailed, null, ex.Message);
        }

        if (process is null)
            return new CfProxyStartResult(CfProxyStartStatus.StartFailed, null, null);

        var grace = shutdownGrace ?? DefaultShutdownGrace;
        var killOnCancel = cancellationToken.Register(() => TryKill(process));

        process.ErrorDataReceived += (_, e) =>
        {
            if (!string.IsNullOrEmpty(e.Data))
                log(e.Data);
        };
        process.BeginErrorReadLine();

        var proxyUrl = await ReadProxyUrlAsync(process, log, readyTimeout, cancellationToken).ConfigureAwait(false);
        if (proxyUrl is null)
        {
            await using var failed = new CfProxyHost(process, string.Empty, killOnCancel, grace);
            return new CfProxyStartResult(
                cancellationToken.IsCancellationRequested ? CfProxyStartStatus.Cancelled : CfProxyStartStatus.NotReady,
                null,
                null);
        }

        // Nothing else reads stdout after the handshake, and the proxy keeps writing
        // (Logger.debug). Once the OS pipe buffer fills it blocks in print() and every
        // request routed through it stalls. Read and discard.
        _ = Task.Run(async () =>
        {
            try
            {
                while (await process.StandardOutput.ReadLineAsync().ConfigureAwait(false) is not null) { }
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"Cloudflare proxy stdout drain ended: {ex.Message}");
            }
        });

        return new CfProxyStartResult(CfProxyStartStatus.Ready, new CfProxyHost(process, proxyUrl, killOnCancel, grace), null);
    }

    private static async Task<string?> ReadProxyUrlAsync(Process process, Action<string> log, TimeSpan timeout, CancellationToken cancellationToken)
    {
        using var readyCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        readyCts.CancelAfter(timeout);

        while (!readyCts.IsCancellationRequested)
        {
            var lineTask = process.StandardOutput.ReadLineAsync();
            var delayTask = Task.Delay(Timeout.Infinite, readyCts.Token);
            var completed = await Task.WhenAny(lineTask, delayTask).ConfigureAwait(false);
            if (completed != lineTask)
                return null; // timed out or cancelled

            var line = lineTask.Result;
            if (line is null)
                return null; // stdout closed before the proxy announced readiness

            log(line);
            if (line.StartsWith(ReadyPrefix, StringComparison.Ordinal))
                return line.Substring(ReadyPrefix.Length).Trim();
        }

        return null;
    }

    /// <summary>Closes stdin so the proxy's own EOF watcher exits it cleanly; kills it if it does not exit in time.</summary>
    public async ValueTask DisposeAsync()
    {
        _killOnCancel.Dispose();
        try
        {
            if (!_process.HasExited)
            {
                try { _process.StandardInput.Close(); } catch { /* already gone */ }

                using var exitCts = new CancellationTokenSource(_shutdownGrace);
                try
                {
                    await _process.WaitForExitAsync(exitCts.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    TryKill(_process);
                }
            }
        }
        finally
        {
            try { _process.Dispose(); } catch { /* ignore */ }
        }
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
        catch
        {
            // Already exited or inaccessible — nothing more to do.
        }
    }
}

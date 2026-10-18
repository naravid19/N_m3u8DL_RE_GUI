#nullable enable
using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using N_m3u8DL_RE_GUI.Core;
using Xunit;

namespace N_m3u8DL_RE_GUI.Tests.Unit.Core;

/// <summary>
/// Drives CfProxyHost against a PowerShell script that speaks the same protocol as
/// <c>m3u8_cf_bypass.py --serve</c>: PROXY_READY on stdout, then run until stdin closes.
/// </summary>
public class CfProxyHostTests
{
    private const string Ready = "PROXY_READY http://127.0.0.1:9/u/tok/http/host/manifest.m3u8";
    private static readonly TimeSpan ReadyWait = TimeSpan.FromSeconds(30);

    private static ProcessStartInfo Script(string body) =>
        CfProxyHost.BuildStartInfo("powershell.exe", $"-NoProfile -NonInteractive -Command \"{body}\"");

    [Fact]
    public async Task Ready_ReturnsTheUrlAfterTheHandshake_AndLogsTheHandshakeLines()
    {
        var lines = new ConcurrentQueue<string>();

        var result = await CfProxyHost.StartAsync(
            Script($"Write-Output 'starting'; Write-Output '{Ready}'; [Console]::In.ReadLine() | Out-Null"),
            lines.Enqueue, ReadyWait, CancellationToken.None, TimeSpan.FromSeconds(5));

        Assert.Equal(CfProxyStartStatus.Ready, result.Status);
        await using var host = result.Host!;
        Assert.Equal("http://127.0.0.1:9/u/tok/http/host/manifest.m3u8", host.ProxyUrl);
        Assert.Contains("starting", lines);
        Assert.Contains(Ready, lines);
    }

    [Fact]
    public async Task StderrLinesAreForwardedToTheLog()
    {
        var lines = new ConcurrentQueue<string>();

        var result = await CfProxyHost.StartAsync(
            Script($"[Console]::Error.WriteLine('proxy-stderr-line'); Write-Output '{Ready}'; [Console]::In.ReadLine() | Out-Null"),
            lines.Enqueue, ReadyWait, CancellationToken.None, TimeSpan.FromSeconds(5));

        Assert.Equal(CfProxyStartStatus.Ready, result.Status);
        await using var host = result.Host!;
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!lines.Contains("proxy-stderr-line") && DateTime.UtcNow < deadline)
            await Task.Delay(50);
        Assert.Contains("proxy-stderr-line", lines);
    }

    [Fact]
    public async Task StdoutIsDrainedAfterTheHandshake_SoAChattyProxyStillExitsWhenStdinCloses()
    {
        // 30k lines is far beyond an OS pipe buffer. Undrained, the script blocks in Write-Output
        // and never reaches ReadLine, so closing stdin cannot end it and only the kill would.
        var result = await CfProxyHost.StartAsync(
            Script($"Write-Output '{Ready}'; 1..30000 | ForEach-Object {{ Write-Output ('debug ' + $_) }}; [Console]::In.ReadLine() | Out-Null"),
            _ => { }, ReadyWait, CancellationToken.None, TimeSpan.FromSeconds(15));

        Assert.Equal(CfProxyStartStatus.Ready, result.Status);
        var stopwatch = Stopwatch.StartNew();
        await result.Host!.DisposeAsync();

        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(12),
            $"shutdown took {stopwatch.Elapsed.TotalSeconds:F1}s: the proxy was killed, not closed cleanly");
    }

    [Fact]
    public async Task NoHandshakeBeforeTheTimeout_IsNotReady()
    {
        var result = await CfProxyHost.StartAsync(
            Script("Start-Sleep -Seconds 60"),
            _ => { }, TimeSpan.FromSeconds(1.5), CancellationToken.None, TimeSpan.FromMilliseconds(300));

        Assert.Equal(CfProxyStartStatus.NotReady, result.Status);
        Assert.Null(result.Host);
    }

    [Fact]
    public async Task StdoutClosingBeforeTheHandshake_IsNotReady()
    {
        var result = await CfProxyHost.StartAsync(
            Script("exit 3"),
            _ => { }, ReadyWait, CancellationToken.None, TimeSpan.FromMilliseconds(300));

        Assert.Equal(CfProxyStartStatus.NotReady, result.Status);
        Assert.Null(result.Host);
    }

    [Fact]
    public async Task CancellingWhileWaiting_IsCancelledNotNotReady()
    {
        using var cts = new CancellationTokenSource();
        cts.CancelAfter(TimeSpan.FromSeconds(1.5));

        var result = await CfProxyHost.StartAsync(
            Script("Start-Sleep -Seconds 60"),
            _ => { }, ReadyWait, cts.Token, TimeSpan.FromMilliseconds(300));

        Assert.Equal(CfProxyStartStatus.Cancelled, result.Status);
        Assert.Null(result.Host);
    }

    [Fact]
    public async Task ACommandThatCannotStart_ReportsWhy()
    {
        var result = await CfProxyHost.StartAsync(
            CfProxyHost.BuildStartInfo(@"C:\definitely\not\here\python.exe", "x"),
            _ => { }, ReadyWait, CancellationToken.None);

        Assert.Equal(CfProxyStartStatus.StartFailed, result.Status);
        Assert.False(string.IsNullOrWhiteSpace(result.Error));
        Assert.Null(result.Host);
    }

    [Fact]
    public async Task TheRealBypassScript_AnnouncesReadyAndExitsCleanlyWhenTheHostIsDisposed()
    {
        // The protocol the fake scripts above imitate, against the real thing. Needs a Python
        // with curl_cffi, the same dependency the feature itself has; without one there is
        // nothing to prove, so the test returns instead of failing on a bare machine.
        var python = await PythonLocator.FindWithCurlCffiAsync();
        if (python is null)
            return;

        var script = System.IO.Path.Combine(N_m3u8DL_RE_GUI.Tests.Fixtures.ProjectSource.SolutionRoot(), "m3u8_cf_bypass.py");
        var result = await CfProxyHost.StartAsync(
            CfProxyHost.BuildStartInfo(python, $"\"{script}\" \"http://127.0.0.1:9/manifest.m3u8\" --serve"),
            _ => { }, ReadyWait, CancellationToken.None);

        Assert.Equal(CfProxyStartStatus.Ready, result.Status);
        Assert.StartsWith("http://127.0.0.1:", result.Host!.ProxyUrl);
        var stopwatch = Stopwatch.StartNew();
        await result.Host.DisposeAsync();
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(5), "the proxy did not exit when stdin closed");
    }

    [Fact]
    public void StartInfoRedirectsAllThreeStreamsAndForcesUtf8()
    {
        var psi = CfProxyHost.BuildStartInfo("python.exe", "m3u8_cf_bypass.py --serve");

        Assert.True(psi.RedirectStandardInput);
        Assert.True(psi.RedirectStandardOutput);
        Assert.True(psi.RedirectStandardError);
        Assert.True(psi.CreateNoWindow);
        Assert.False(psi.UseShellExecute);
        Assert.Equal("1", psi.EnvironmentVariables["PYTHONUTF8"]);
        Assert.Equal("utf-8", psi.EnvironmentVariables["PYTHONIOENCODING"]);
    }
}

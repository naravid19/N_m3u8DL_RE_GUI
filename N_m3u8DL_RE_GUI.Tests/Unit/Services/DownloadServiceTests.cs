#nullable enable
using System.Collections.Generic;
using System.Threading.Tasks;
using N_m3u8DL_RE_GUI.Core;
using N_m3u8DL_RE_GUI.Services;
using Xunit;

namespace N_m3u8DL_RE_GUI.Tests.Unit.Services;

public class DownloadServiceTests
{
    [Fact]
    public void IsDownloading_Initially_ShouldBeFalse()
    {
        var service = new DownloadService();
        Assert.False(service.IsDownloading);
    }

    [Fact]
    public void StopDownload_WhenNotDownloading_ShouldNotThrow()
    {
        var service = new DownloadService();
        var exception = Record.Exception(() => service.StopDownload());
        Assert.Null(exception);
    }

    [Fact]
    public void StopDownload_MultipleCalls_ShouldBeIdempotent()
    {
        var service = new DownloadService();
        service.StopDownload();
        service.StopDownload();
        service.StopDownload();
        Assert.False(service.IsDownloading);
    }

    [Fact]
    public async Task StartDownloadAsync_WithEmptyInput_ShouldReturnFalseAndLogMessage()
    {
        var service = new DownloadService();
        var options = new DownloadOptions { Input = "" };
        var logs = new List<string>();

        var result = await service.StartDownloadAsync(options, logCallback: msg => logs.Add(msg));

        Assert.False(result);
        Assert.NotEmpty(logs);
        Assert.Contains(logs, l => l.Contains("Please enter a URL"));
    }

    [Fact]
    public async Task StartDownloadAsync_WithNonExistentExePath_ShouldReturnFalseAndLogMessage()
    {
        var service = new DownloadService();
        var options = new DownloadOptions 
        { 
            Input = "https://surrit.com/33ece07f-3229-41eb-b189-ec2485619e02/360p/video.m3u8",
            ExePath = @"C:\non_existent_path\fake_downloader.exe" 
        };
        var logs = new List<string>();

        var result = await service.StartDownloadAsync(options, logCallback: msg => logs.Add(msg));

        Assert.False(result);
        Assert.NotEmpty(logs);
        Assert.Contains(logs, l => l.Contains("File not found") || l.Contains("Please download"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public async Task StartProcessAsync_WithEmptyOrNullFileName_ShouldReturnFalseAndLogMessage(string? fileName)
    {
        var service = new DownloadService();
        var logs = new List<string>();

        var result = await service.StartProcessAsync(fileName!, "", logCallback: msg => logs.Add(msg));

        Assert.False(result);
        Assert.NotEmpty(logs);
        Assert.Contains(logs, l => l.Contains("Process target file path is required"));
    }

    [Fact]
    public async Task StartProcessAsync_WhenStopped_ReturnsFalseWithoutThrowing()
    {
        var service = new DownloadService();
        var task = service.StartProcessAsync("cmd.exe", "/c ping -n 30 127.0.0.1 > nul");
        await WaitUntilAsync(() => service.IsDownloading);

        service.StopDownload();

        Assert.False(await task);
        Assert.False(service.IsDownloading);
    }

    [Fact]
    public async Task StartProcessAsync_WhenAlreadyRunning_ShouldRejectSecondCall()
    {
        var service = new DownloadService();
        var task1 = service.StartProcessAsync("cmd.exe", "/c ping -n 30 127.0.0.1 > nul");
        await WaitUntilAsync(() => service.IsDownloading);

        var logs = new List<string>();
        var result2 = await service.StartProcessAsync("cmd.exe", "/c ping 127.0.0.1", logCallback: msg => logs.Add(msg));

        Assert.False(result2);
        Assert.Contains(logs, l => l.Contains("already in progress"));

        service.StopDownload();
        await task1;
    }

    [Fact]
    public async Task StartProcessAsync_ShouldForwardChildStdoutToTheLogCallback()
    {
        var service = new DownloadService();
        var lines = new List<string>();

        var ok = await service.StartProcessAsync(
            "cmd.exe",
            "/c echo hello-from-child",
            message => { lock (lines) lines.Add(message); });

        Assert.True(ok);
        Assert.Contains(lines, l => l.Contains("hello-from-child"));
    }

    [Fact]
    public async Task StartProcessAsync_ShouldReportPercentagesToTheProgressCallback()
    {
        var service = new DownloadService();
        var reported = new List<int>();
        var progress = new System.Progress<int>(p => { lock (reported) reported.Add(p); });

        await service.StartProcessAsync(
            "cmd.exe",
            "/c echo working 40% && echo working 90%",
            logCallback: null,
            progressCallback: progress);

        // Progress<T> marshals asynchronously; give the callbacks a moment to land.
        await Task.Delay(300);
        lock (reported)
        {
            Assert.Contains(40, reported);
            Assert.Contains(90, reported);
        }
    }

    [Fact]
    public async Task StartProcessAsync_ShouldReportNonZeroExitCodeInTheLog()
    {
        var service = new DownloadService();
        var lines = new List<string>();

        var ok = await service.StartProcessAsync(
            "cmd.exe",
            "/c exit 3",
            message => { lock (lines) lines.Add(message); });

        Assert.False(ok);
        Assert.Contains(lines, l => l.Contains("3"));
    }

    private static async Task WaitUntilAsync(System.Func<bool> condition, int timeoutMs = 5000)
    {
        var start = System.DateTime.UtcNow;
        while (!condition())
        {
            if ((System.DateTime.UtcNow - start).TotalMilliseconds > timeoutMs)
                throw new System.TimeoutException("WaitUntilAsync timed out waiting for condition.");
            await Task.Delay(20);
        }
    }
}

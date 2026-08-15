#nullable enable
using System.Net;
using System.Net.Sockets;
using System.Text;
using N_m3u8DL_RE_GUI.Services;
using Xunit;

namespace N_m3u8DL_RE_GUI.Tests.Unit.Services;

/// <summary>
/// End-to-end coverage for <see cref="UtilityService.GetTitleFromUrlAsync"/> against a
/// loopback HTTP stub. This is the only path that exercises the streaming read loop, the
/// 256&#160;KB allocation cap, and the private CleanTitle sanitiser.
/// </summary>
public class UtilityServiceTitleTests
{
    [Fact]
    public async Task GetTitleFromUrlAsync_WithSimpleTitle_ShouldReturnTitleText()
    {
        using var server = StubHttpServer.WithHtml("<html><head><title>Episode 01</title></head><body/></html>");
        using var service = new UtilityService();

        var title = await service.GetTitleFromUrlAsync(server.Url);

        Assert.Equal("Episode 01", title);
    }

    [Fact]
    public async Task GetTitleFromUrlAsync_WithAttributesOnTitleTag_ShouldStillExtractText()
    {
        using var server = StubHttpServer.WithHtml("<title data-testid=\"t\" lang=\"th\">รายการที่ 5</title>");
        using var service = new UtilityService();

        var title = await service.GetTitleFromUrlAsync(server.Url);

        Assert.Equal("รายการที่ 5", title);
    }

    [Fact]
    public async Task GetTitleFromUrlAsync_ShouldTrimSurroundingWhitespace()
    {
        using var server = StubHttpServer.WithHtml("<title>\n    Spaced Title   \n</title>");
        using var service = new UtilityService();

        var title = await service.GetTitleFromUrlAsync(server.Url);

        Assert.Equal("Spaced Title", title);
    }

    [Fact]
    public async Task GetTitleFromUrlAsync_ShouldStripCharactersIllegalInWindowsFileNames()
    {
        using var server = StubHttpServer.WithHtml("<title>A:B/C?D*E|F\"G</title>");
        using var service = new UtilityService();

        var title = await service.GetTitleFromUrlAsync(server.Url);

        Assert.Equal("ABCDEFG", title);
        Assert.DoesNotContain(title, c => Path.GetInvalidFileNameChars().Contains(c));
    }

    [Theory]
    [InlineData("<title>My Video_哔哩哔哩</title>", "My Video")]
    [InlineData("<title>My Video - WeTV</title>", "My Video")]
    [InlineData("<title>My Video_腾讯视频</title>", "My Video")]
    [InlineData("<title>My Video_爱奇艺</title>", "My Video")]
    [InlineData("<title>My Video_优酷</title>", "My Video")]
    public async Task GetTitleFromUrlAsync_ShouldStripKnownSiteSuffixes(string html, string expected)
    {
        using var server = StubHttpServer.WithHtml(html);
        using var service = new UtilityService();

        var title = await service.GetTitleFromUrlAsync(server.Url);

        Assert.Equal(expected, title);
    }

    [Fact]
    public async Task GetTitleFromUrlAsync_WithNoTitleTag_ShouldReturnEmpty()
    {
        using var server = StubHttpServer.WithHtml("<html><body>no title here</body></html>");
        using var service = new UtilityService();

        Assert.Equal(string.Empty, await service.GetTitleFromUrlAsync(server.Url));
    }

    [Fact]
    public async Task GetTitleFromUrlAsync_WithEmptyTitleTag_ShouldReturnEmpty()
    {
        // <title></title> has no capture group match ([^<]+ requires at least one char).
        using var server = StubHttpServer.WithHtml("<title></title>");
        using var service = new UtilityService();

        Assert.Equal(string.Empty, await service.GetTitleFromUrlAsync(server.Url));
    }

    [Fact]
    public async Task GetTitleFromUrlAsync_WithErrorStatusCode_ShouldReturnEmptyWithoutThrowing()
    {
        using var server = StubHttpServer.WithRawResponse(
            "HTTP/1.1 404 Not Found\r\nContent-Length: 0\r\nConnection: close\r\n\r\n");
        using var service = new UtilityService();

        Assert.Equal(string.Empty, await service.GetTitleFromUrlAsync(server.Url));
    }

    [Fact]
    public async Task GetTitleFromUrlAsync_WithTitleBeforeTheCap_ShouldStopReadingEarly()
    {
        var html = "<html><head><title>Early Title</title></head><body>"
                   + new string('x', 400 * 1024)
                   + "</body></html>";
        using var server = StubHttpServer.WithHtml(html);
        using var service = new UtilityService();

        Assert.Equal("Early Title", await service.GetTitleFromUrlAsync(server.Url));
    }

    [Fact]
    public async Task GetTitleFromUrlAsync_WithTitlePastThe256KbCap_ShouldReturnEmpty()
    {
        // Documented limit: buffering stops just past 256 KB to bound allocations, so a
        // title further into the document is intentionally not found.
        var html = "<html><body>" + new string('x', 300 * 1024) + "</body>"
                   + "<head><title>Late Title</title></head></html>";
        using var server = StubHttpServer.WithHtml(html);
        using var service = new UtilityService();

        Assert.Equal(string.Empty, await service.GetTitleFromUrlAsync(server.Url));
    }

    [Fact]
    public async Task GetTitleFromUrlAsync_WhenCallerCancels_ShouldPropagateCancellation()
    {
        using var server = StubHttpServer.WithNeverEndingResponse();
        using var service = new UtilityService();
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(250));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => service.GetTitleFromUrlAsync(server.Url, cts.Token));
    }

    [Fact]
    public async Task GetTitleFromUrlAsync_WhenAlreadyCancelled_ShouldNotIssueRequest()
    {
        using var server = StubHttpServer.WithHtml("<title>Never Read</title>");
        using var service = new UtilityService();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => service.GetTitleFromUrlAsync(server.Url, cts.Token));
        Assert.Equal(0, server.RequestCount);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(@"C:\local\file.m3u8")]
    [InlineData("ftp://example.com/a.m3u8")]
    [InlineData("not a url at all")]
    public async Task GetTitleFromUrlAsync_WithNonHttpInput_ShouldReturnEmpty(string? input)
    {
        using var service = new UtilityService();

        Assert.Equal(string.Empty, await service.GetTitleFromUrlAsync(input!));
    }

    [Fact]
    public async Task GetTitleFromUrlAsync_WithUnreachableHost_ShouldReturnEmptyWithoutThrowing()
    {
        using var service = new UtilityService();

        // Port 1 on loopback refuses connections immediately.
        Assert.Equal(string.Empty, await service.GetTitleFromUrlAsync("http://127.0.0.1:1/index.html"));
    }

    [Fact]
    public async Task GetTitleFromUrlAsync_CalledConcurrently_ShouldReuseSharedClientWithoutError()
    {
        using var server = StubHttpServer.WithHtml("<title>Concurrent</title>");
        using var service = new UtilityService();

        var titles = await Task.WhenAll(
            Enumerable.Range(0, 8).Select(_ => service.GetTitleFromUrlAsync(server.Url)));

        Assert.All(titles, t => Assert.Equal("Concurrent", t));
    }

    // -------------------------------------------------------------------------
    // Minimal loopback HTTP/1.1 stub (no HttpListener: it needs a URL ACL on Windows).
    // -------------------------------------------------------------------------

    private sealed class StubHttpServer : IDisposable
    {
        private readonly TcpListener _listener;
        private readonly CancellationTokenSource _cts = new();
        private readonly byte[]? _response;
        private readonly bool _hang;
        private int _requestCount;

        private StubHttpServer(byte[]? response, bool hang)
        {
            _response = response;
            _hang = hang;
            _listener = new TcpListener(IPAddress.Loopback, 0);
            _listener.Start();
            Url = $"http://127.0.0.1:{((IPEndPoint)_listener.LocalEndpoint).Port}/index.html";
            _ = Task.Run(AcceptLoopAsync);
        }

        public string Url { get; }

        public int RequestCount => Volatile.Read(ref _requestCount);

        public static StubHttpServer WithHtml(string html)
        {
            var body = Encoding.UTF8.GetBytes(html);
            var header = Encoding.ASCII.GetBytes(
                "HTTP/1.1 200 OK\r\n" +
                "Content-Type: text/html; charset=utf-8\r\n" +
                $"Content-Length: {body.Length}\r\n" +
                "Connection: close\r\n\r\n");

            var response = new byte[header.Length + body.Length];
            Buffer.BlockCopy(header, 0, response, 0, header.Length);
            Buffer.BlockCopy(body, 0, response, header.Length, body.Length);
            return new StubHttpServer(response, hang: false);
        }

        public static StubHttpServer WithRawResponse(string raw) =>
            new(Encoding.ASCII.GetBytes(raw), hang: false);

        /// <summary>Accepts the connection but never sends a response.</summary>
        public static StubHttpServer WithNeverEndingResponse() => new(null, hang: true);

        private async Task AcceptLoopAsync()
        {
            try
            {
                while (!_cts.IsCancellationRequested)
                {
                    var client = await _listener.AcceptTcpClientAsync(_cts.Token);
                    _ = Task.Run(() => HandleAsync(client));
                }
            }
            catch
            {
                // Listener stopped.
            }
        }

        private async Task HandleAsync(TcpClient client)
        {
            using (client)
            {
                try
                {
                    Interlocked.Increment(ref _requestCount);
                    var stream = client.GetStream();

                    // Drain the request headers so the client's write completes. One read is
                    // enough here — we never inspect the request, we only need it flushed.
                    var buffer = new byte[4096];
                    var requestBytes = await stream.ReadAsync(buffer, _cts.Token);
                    if (requestBytes == 0)
                        return;   // client hung up before sending anything

                    if (_hang)
                    {
                        await Task.Delay(Timeout.Infinite, _cts.Token);
                        return;
                    }

                    await stream.WriteAsync(_response!, _cts.Token);
                    await stream.FlushAsync(_cts.Token);
                }
                catch
                {
                    // Client disconnected or server shutting down.
                }
            }
        }

        public void Dispose()
        {
            _cts.Cancel();
            try { _listener.Stop(); } catch { }
            _cts.Dispose();
        }
    }
}

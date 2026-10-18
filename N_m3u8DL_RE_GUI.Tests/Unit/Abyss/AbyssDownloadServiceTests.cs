#nullable enable
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using N_m3u8DL_RE_GUI.Core.Abyss;
using N_m3u8DL_RE_GUI.Tests.Fixtures;
using Xunit;

namespace N_m3u8DL_RE_GUI.Tests.Unit.Abyss;

public sealed class AbyssDownloadServiceTests : IDisposable
{
    private const int Md5Id = 4242;
    private const int ResId = 7;
    private const string Slug = "bx_e0gGc9";
    private const string Label = "1080p";

    private readonly string _workDir;

    public AbyssDownloadServiceTests()
    {
        _workDir = Path.Combine(Path.GetTempPath(), "nre_abyss_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_workDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_workDir, recursive: true); } catch { }
    }

    // --- fixture helpers -------------------------------------------------

    private static (AbyssMp4 mp4, AbyssSource source) MakeVideo(long size, int chunkSize)
    {
        var mp4 = new AbyssMp4
        {
            Domains = new List<string> { "cdn.example.test" },
            Slug = Slug,
            Md5Id = Md5Id,
            Sources = new List<AbyssSource>()
        };
        var source = new AbyssSource
        {
            Label = Label,
            Size = size,
            ResId = ResId,
            Subdomain = "s1",
            Codec = "h264",
            PartSize = chunkSize,
            Status = true
        };
        mp4.Sources.Add(source);
        return (mp4, source);
    }

    /// <summary>
    /// The chunk URL ends in a token derived from the chunk index. Precomputing
    /// the map with the production function is what lets the stub answer
    /// concurrent requests correctly without depending on arrival order.
    /// </summary>
    private static Dictionary<string, int> TokenToIndex(AbyssSource source, int chunkSize, int totalChunks)
    {
        var sizeKeyHex = AbyssCrypto.DeriveKey(source.Size);
        var map = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var i = 0; i < totalChunks; i++)
        {
            var token = AbyssDownloadService.GenerateSegmentToken(
                Md5Id, source.ResId, source.Size, chunkSize, i, sizeKeyHex);
            map[token] = i;
        }
        return map;
    }

    private static int IndexOf(HttpRequestMessage request, IReadOnlyDictionary<string, int> map)
    {
        var token = request.RequestUri!.Segments[^1];
        return map[token];
    }

    /// <summary>Distinct, verifiable content per chunk, so a merge in the wrong order is visible.</summary>
    private static byte[] ChunkBytes(int index, int length)
    {
        var bytes = new byte[length];
        for (var i = 0; i < length; i++)
            bytes[i] = (byte)((index + 1) & 0xFF);
        return bytes;
    }

    private static int ChunkLength(int index, long size, int chunkSize, int totalChunks)
        => index == totalChunks - 1 ? (int)(size - (long)index * chunkSize) : chunkSize;

    private static HttpResponseMessage Ok(byte[] body)
        => new(HttpStatusCode.OK) { Content = new ByteArrayContent(body) };

    private string ExpectedTempDir() => Path.Combine(_workDir, $"abyss_temp_{Slug}_{Label}");

    // --- tests -----------------------------------------------------------

    [Fact]
    public async Task DownloadsEveryChunkAndMergesThemInIndexOrder()
    {
        const long size = 5000;
        const int chunkSize = 2000;
        var totalChunks = 3;
        var (mp4, source) = MakeVideo(size, chunkSize);
        var map = TokenToIndex(source, chunkSize, totalChunks);

        var handler = new StubHttpMessageHandler((request, _) =>
        {
            var index = IndexOf(request, map);
            return Task.FromResult(Ok(ChunkBytes(index, ChunkLength(index, size, chunkSize, totalChunks))));
        });

        var output = Path.Combine(_workDir, "out.mp4");
        await new AbyssDownloadService(new HttpClient(handler))
            .DownloadAsync(mp4, source, output, connections: 1);

        var expected = Enumerable.Range(0, totalChunks)
            .SelectMany(i => ChunkBytes(i, ChunkLength(i, size, chunkSize, totalChunks)))
            .ToArray();

        Assert.Equal(expected, await File.ReadAllBytesAsync(output));
    }

    [Fact]
    public async Task TheLastChunkIsShorterWhenTheSizeIsNotAWholeNumberOfChunks()
    {
        const long size = 4500;
        const int chunkSize = 2000;
        var totalChunks = 3;
        var (mp4, source) = MakeVideo(size, chunkSize);
        var map = TokenToIndex(source, chunkSize, totalChunks);
        var served = new ConcurrentDictionary<int, int>();

        var handler = new StubHttpMessageHandler((request, _) =>
        {
            var index = IndexOf(request, map);
            var length = ChunkLength(index, size, chunkSize, totalChunks);
            served[index] = length;
            return Task.FromResult(Ok(ChunkBytes(index, length)));
        });

        var output = Path.Combine(_workDir, "out.mp4");
        await new AbyssDownloadService(new HttpClient(handler))
            .DownloadAsync(mp4, source, output, connections: 1);

        Assert.Equal(500, served[2]);
        Assert.Equal(size, new FileInfo(output).Length);
    }

    [Fact]
    public async Task ChunksAlreadyOnDiskAtTheRightSizeAreNotRequestedAgain()
    {
        const long size = 6000;
        const int chunkSize = 2000;
        var totalChunks = 3;
        var (mp4, source) = MakeVideo(size, chunkSize);
        var map = TokenToIndex(source, chunkSize, totalChunks);

        // Stand in for a previous run that got two of three chunks down.
        Directory.CreateDirectory(ExpectedTempDir());
        for (var i = 0; i < 2; i++)
        {
            await File.WriteAllBytesAsync(
                Path.Combine(ExpectedTempDir(), $"segment_{i:D6}.part"),
                ChunkBytes(i, chunkSize));
        }

        var requested = new ConcurrentBag<int>();
        var handler = new StubHttpMessageHandler((request, _) =>
        {
            var index = IndexOf(request, map);
            requested.Add(index);
            return Task.FromResult(Ok(ChunkBytes(index, ChunkLength(index, size, chunkSize, totalChunks))));
        });

        var output = Path.Combine(_workDir, "out.mp4");
        await new AbyssDownloadService(new HttpClient(handler))
            .DownloadAsync(mp4, source, output, connections: 1);

        Assert.Equal(new[] { 2 }, requested.ToArray());
        Assert.Equal(size, new FileInfo(output).Length);
    }

    [Fact]
    public async Task AChunkOnDiskAtTheWrongSizeIsRedownloaded()
    {
        // A truncated .part is exactly what a process killed mid-write leaves
        // behind, and trusting it would silently corrupt the merged file.
        const long size = 4000;
        const int chunkSize = 2000;
        var totalChunks = 2;
        var (mp4, source) = MakeVideo(size, chunkSize);
        var map = TokenToIndex(source, chunkSize, totalChunks);

        Directory.CreateDirectory(ExpectedTempDir());
        await File.WriteAllBytesAsync(
            Path.Combine(ExpectedTempDir(), "segment_000000.part"),
            ChunkBytes(0, chunkSize - 17));

        var requested = new ConcurrentBag<int>();
        var handler = new StubHttpMessageHandler((request, _) =>
        {
            var index = IndexOf(request, map);
            requested.Add(index);
            return Task.FromResult(Ok(ChunkBytes(index, chunkSize)));
        });

        var output = Path.Combine(_workDir, "out.mp4");
        await new AbyssDownloadService(new HttpClient(handler))
            .DownloadAsync(mp4, source, output, connections: 1);

        Assert.Contains(0, requested);
        Assert.Equal(size, new FileInfo(output).Length);
    }

    [Fact]
    public async Task TheTempDirectoryNameDependsOnlyOnTheVideoAndQuality()
    {
        // Resume works only because two runs of the same download agree on this
        // path; a timestamp in it would silently restart from zero every time.
        const long size = 2000;
        const int chunkSize = 2000;
        var (mp4, source) = MakeVideo(size, chunkSize);
        var map = TokenToIndex(source, chunkSize, 1);
        var seenDirs = new ConcurrentBag<string>();

        var handler = new StubHttpMessageHandler((request, _) =>
        {
            seenDirs.Add(Directory.Exists(ExpectedTempDir()) ? ExpectedTempDir() : "missing");
            return Task.FromResult(Ok(ChunkBytes(IndexOf(request, map), chunkSize)));
        });

        var service = new AbyssDownloadService(new HttpClient(handler));
        await service.DownloadAsync(mp4, source, Path.Combine(_workDir, "a.mp4"), connections: 1);
        await service.DownloadAsync(mp4, source, Path.Combine(_workDir, "b.mp4"), connections: 1);

        Assert.All(seenDirs, d => Assert.Equal(ExpectedTempDir(), d));
    }

    [Fact]
    public async Task ASuccessfulDownloadLeavesNoTempDirectoryBehind()
    {
        const long size = 2000;
        const int chunkSize = 2000;
        var (mp4, source) = MakeVideo(size, chunkSize);
        var map = TokenToIndex(source, chunkSize, 1);

        var handler = new StubHttpMessageHandler((request, _) =>
            Task.FromResult(Ok(ChunkBytes(IndexOf(request, map), chunkSize))));

        await new AbyssDownloadService(new HttpClient(handler))
            .DownloadAsync(mp4, source, Path.Combine(_workDir, "out.mp4"), connections: 1);

        Assert.False(Directory.Exists(ExpectedTempDir()));
    }

    [Fact]
    public async Task ATransientFailureIsRetriedAndTheDownloadStillSucceeds()
    {
        const long size = 2000;
        const int chunkSize = 2000;
        var (mp4, source) = MakeVideo(size, chunkSize);
        var map = TokenToIndex(source, chunkSize, 1);
        var attempts = 0;

        var handler = new StubHttpMessageHandler((request, _) =>
        {
            var index = IndexOf(request, map);
            if (Interlocked.Increment(ref attempts) == 1)
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError));
            return Task.FromResult(Ok(ChunkBytes(index, chunkSize)));
        });

        var output = Path.Combine(_workDir, "out.mp4");
        await new AbyssDownloadService(new HttpClient(handler), retryBackoff: TimeSpan.FromMilliseconds(1))
            .DownloadAsync(mp4, source, output, connections: 1);

        Assert.Equal(2, attempts);
        Assert.Equal(size, new FileInfo(output).Length);
    }

    [Fact]
    public async Task AChunkThatNeverSucceedsFailsTheDownloadAndKeepsWhatWasAlreadyFetched()
    {
        // The surviving temp directory is the resume state for the next attempt.
        const long size = 4000;
        const int chunkSize = 2000;
        var totalChunks = 2;
        var (mp4, source) = MakeVideo(size, chunkSize);
        var map = TokenToIndex(source, chunkSize, totalChunks);

        var handler = new StubHttpMessageHandler((request, _) =>
        {
            var index = IndexOf(request, map);
            if (index == 1)
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError));
            return Task.FromResult(Ok(ChunkBytes(index, chunkSize)));
        });

        var output = Path.Combine(_workDir, "out.mp4");
        // Exhausting all five retries at the production backoff would sleep
        // 1+2+3+4+5 = 15 real seconds for one assertion.
        await Assert.ThrowsAsync<HttpRequestException>(() =>
            new AbyssDownloadService(new HttpClient(handler), retryBackoff: TimeSpan.FromMilliseconds(1))
                .DownloadAsync(mp4, source, output, connections: 1));

        Assert.True(Directory.Exists(ExpectedTempDir()));
        Assert.True(File.Exists(Path.Combine(ExpectedTempDir(), "segment_000000.part")));
        Assert.False(File.Exists(output));
    }

    [Fact]
    public async Task AFinishedChunkThatGoesMissingBeforeTheMergeFailsTheDownload()
    {
        // Skipping it wrote a truncated MP4 and still logged "Download finished!".
        const long size = 4000;
        const int chunkSize = 2000;
        var totalChunks = 2;
        var (mp4, source) = MakeVideo(size, chunkSize);
        var map = TokenToIndex(source, chunkSize, totalChunks);

        var handler = new StubHttpMessageHandler((request, _) =>
        {
            var index = IndexOf(request, map);
            // With one connection, chunk 0 is already finished on disk when chunk 1 is asked for.
            if (index == 1)
                File.Delete(Path.Combine(ExpectedTempDir(), "segment_000000.part"));
            return Task.FromResult(Ok(ChunkBytes(index, chunkSize)));
        });

        var output = Path.Combine(_workDir, "out.mp4");
        await Assert.ThrowsAsync<FileNotFoundException>(() =>
            new AbyssDownloadService(new HttpClient(handler))
                .DownloadAsync(mp4, source, output, connections: 1));

        Assert.False(File.Exists(output));
    }

    [Fact]
    public async Task DisposingTheServiceReleasesTheClientItCreated()
    {
        var (mp4, source) = MakeVideo(2000, 2000);
        var service = new AbyssDownloadService(retryBackoff: TimeSpan.FromMilliseconds(1));

        service.Dispose();

        var ex = await Assert.ThrowsAsync<HttpRequestException>(() =>
            service.DownloadAsync(mp4, source, Path.Combine(_workDir, "out.mp4"), connections: 1));
        Assert.IsType<ObjectDisposedException>(ex.InnerException);
    }

    [Fact]
    public async Task DisposingTheServiceLeavesACallersClientUsable()
    {
        var client = new HttpClient(new StubHttpMessageHandler((_, _) => Task.FromResult(Ok(new byte[] { 1 }))));

        new AbyssDownloadService(client).Dispose();

        using var response = await client.GetAsync("https://cdn.example.test/still-open");
        Assert.True(response.IsSuccessStatusCode);
    }

    [Fact]
    public async Task NoPartialTempFilesSurviveASuccessfulRun()
    {
        const long size = 4000;
        const int chunkSize = 2000;
        var totalChunks = 2;
        var (mp4, source) = MakeVideo(size, chunkSize);
        var map = TokenToIndex(source, chunkSize, totalChunks);
        var probed = new ConcurrentBag<string[]>();

        var handler = new StubHttpMessageHandler((request, _) =>
        {
            if (Directory.Exists(ExpectedTempDir()))
                probed.Add(Directory.GetFiles(ExpectedTempDir(), "*.tmp"));
            var index = IndexOf(request, map);
            return Task.FromResult(Ok(ChunkBytes(index, chunkSize)));
        });

        await new AbyssDownloadService(new HttpClient(handler))
            .DownloadAsync(mp4, source, Path.Combine(_workDir, "out.mp4"), connections: 1);

        Assert.False(Directory.Exists(ExpectedTempDir()));
    }

    [Fact]
    public async Task SendsTheAbyssRefererAndOriginAndTheSuppliedUserAgent()
    {
        const long size = 2000;
        const int chunkSize = 2000;
        var (mp4, source) = MakeVideo(size, chunkSize);
        var map = TokenToIndex(source, chunkSize, 1);
        HttpRequestMessage? seen = null;

        var handler = new StubHttpMessageHandler((request, _) =>
        {
            seen = request;
            return Task.FromResult(Ok(ChunkBytes(IndexOf(request, map), chunkSize)));
        });

        await new AbyssDownloadService(new HttpClient(handler)).DownloadAsync(
            mp4, source, Path.Combine(_workDir, "out.mp4"),
            customHeaders: new Dictionary<string, string> { ["User-Agent"] = "TestAgent/9.9" },
            connections: 1);

        Assert.NotNull(seen);
        Assert.Equal("TestAgent/9.9", string.Join("", seen!.Headers.GetValues("User-Agent")));
        Assert.Equal("https://abysscdn.com/", string.Join("", seen.Headers.GetValues("Referer")));
        Assert.Equal("https://abysscdn.com", string.Join("", seen.Headers.GetValues("Origin")));
    }

    [Fact]
    public async Task TheChunkUrlCarriesTheSoraPathAndTheTotalSize()
    {
        const long size = 2000;
        const int chunkSize = 2000;
        var (mp4, source) = MakeVideo(size, chunkSize);
        var map = TokenToIndex(source, chunkSize, 1);
        Uri? seen = null;

        var handler = new StubHttpMessageHandler((request, _) =>
        {
            seen = request.RequestUri;
            return Task.FromResult(Ok(ChunkBytes(IndexOf(request, map), chunkSize)));
        });

        await new AbyssDownloadService(new HttpClient(handler))
            .DownloadAsync(mp4, source, Path.Combine(_workDir, "out.mp4"), connections: 1);

        Assert.NotNull(seen);
        Assert.Contains($"/sora/{size}/", seen!.AbsoluteUri);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-4)]
    public async Task ANonPositiveConnectionCountFallsBackToTheDefault(int connections)
    {
        const long size = 2000;
        const int chunkSize = 2000;
        var (mp4, source) = MakeVideo(size, chunkSize);
        var map = TokenToIndex(source, chunkSize, 1);

        var handler = new StubHttpMessageHandler((request, _) =>
            Task.FromResult(Ok(ChunkBytes(IndexOf(request, map), chunkSize))));

        var output = Path.Combine(_workDir, "out.mp4");
        await new AbyssDownloadService(new HttpClient(handler))
            .DownloadAsync(mp4, source, output, connections: connections);

        Assert.Equal(size, new FileInfo(output).Length);
    }

    [Fact]
    public async Task RejectsMissingArguments()
    {
        var (mp4, source) = MakeVideo(2000, 2000);
        var service = new AbyssDownloadService(new HttpClient(
            new StubHttpMessageHandler((_, _) => Task.FromResult(Ok(Array.Empty<byte>())))));

        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            service.DownloadAsync(null!, source, Path.Combine(_workDir, "a.mp4")));
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            service.DownloadAsync(mp4, null!, Path.Combine(_workDir, "a.mp4")));
        await Assert.ThrowsAsync<ArgumentException>(() =>
            service.DownloadAsync(mp4, source, "   "));
    }

    [Fact]
    public async Task ReportsProgressThatEndsAtOneHundredPercent()
    {
        const long size = 4000;
        const int chunkSize = 2000;
        var totalChunks = 2;
        var (mp4, source) = MakeVideo(size, chunkSize);
        var map = TokenToIndex(source, chunkSize, totalChunks);
        var reports = new ConcurrentBag<AbyssDownloadProgress>();

        var handler = new StubHttpMessageHandler((request, _) =>
            Task.FromResult(Ok(ChunkBytes(IndexOf(request, map), chunkSize))));

        await new AbyssDownloadService(new HttpClient(handler)).DownloadAsync(
            mp4, source, Path.Combine(_workDir, "out.mp4"),
            connections: 1,
            progress: new Progress<AbyssDownloadProgress>(p => reports.Add(p)));

        // Progress is reported through IProgress, which marshals asynchronously.
        var deadline = DateTime.UtcNow.AddSeconds(2);
        while (reports.IsEmpty && DateTime.UtcNow < deadline)
            await Task.Delay(25);

        Assert.NotEmpty(reports);
        Assert.Contains(reports, r => Math.Abs(r.Percentage - 100.0) < 0.001);
    }

    [Fact]
    public async Task CancellingMidDownloadThrowsCancellationNotAWrappedHttpError()
    {
        // MainWindow has a dedicated `catch (OperationCanceledException)` that
        // shows "Download stopped by user."; anything else lands in the generic
        // handler and paints a red error for an action the user chose.
        const long size = 8000;
        const int chunkSize = 2000;
        var totalChunks = 4;
        var (mp4, source) = MakeVideo(size, chunkSize);
        var map = TokenToIndex(source, chunkSize, totalChunks);
        using var cts = new CancellationTokenSource();

        var handler = new StubHttpMessageHandler(async (request, token) =>
        {
            cts.Cancel();
            await Task.Delay(50, CancellationToken.None);
            token.ThrowIfCancellationRequested();
            return Ok(ChunkBytes(IndexOf(request, map), chunkSize));
        });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new AbyssDownloadService(new HttpClient(handler)).DownloadAsync(
                mp4, source, Path.Combine(_workDir, "out.mp4"),
                connections: 1,
                cancellationToken: cts.Token));
    }

    [Fact]
    public async Task CancellingKeepsTheTempDirectoryForResuming()
    {
        const long size = 8000;
        const int chunkSize = 2000;
        var totalChunks = 4;
        var (mp4, source) = MakeVideo(size, chunkSize);
        var map = TokenToIndex(source, chunkSize, totalChunks);
        using var cts = new CancellationTokenSource();
        var served = 0;

        var handler = new StubHttpMessageHandler(async (request, token) =>
        {
            if (Interlocked.Increment(ref served) > 1)
            {
                cts.Cancel();
                await Task.Delay(50, CancellationToken.None);
                token.ThrowIfCancellationRequested();
            }
            return Ok(ChunkBytes(IndexOf(request, map), chunkSize));
        });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new AbyssDownloadService(new HttpClient(handler)).DownloadAsync(
                mp4, source, Path.Combine(_workDir, "out.mp4"),
                connections: 1,
                cancellationToken: cts.Token));

        Assert.True(Directory.Exists(ExpectedTempDir()));
    }

    [Fact]
    public async Task ARetriedChunkIsNotCountedTwiceInProgress()
    {
        // Percentage clamps at 100, so double counting does not show an
        // impossible number -- it shows the bar arriving at 100% while chunks
        // are still downloading, with ETA collapsed to zero.
        const long size = 100_000;
        const int chunkSize = 100_000;
        var (mp4, source) = MakeVideo(size, chunkSize);
        var map = TokenToIndex(source, chunkSize, 1);
        var payload = ChunkBytes(0, chunkSize);
        var attempts = 0;
        var reports = new ConcurrentBag<AbyssDownloadProgress>();

        var handler = new StubHttpMessageHandler((request, _) =>
        {
            var attempt = Interlocked.Increment(ref attempts);
            Stream body = attempt == 1
                // Drop 70% of the way in, after those bytes were counted.
                ? new FailsPartwayStream(payload, (int)(chunkSize * 0.7))
                // Slow enough that the 200 ms reporter observes the inflated total.
                : new SlowStream(payload, chunkSize / 10, TimeSpan.FromMilliseconds(60));

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StreamContent(body)
            });
        });

        // A short backoff keeps the test quick; the observation window that
        // matters is the SlowStream on attempt 2, not the wait before it.
        await new AbyssDownloadService(new HttpClient(handler), retryBackoff: TimeSpan.FromMilliseconds(20)).DownloadAsync(
            mp4, source, Path.Combine(_workDir, "out.mp4"),
            connections: 1,
            progress: new Progress<AbyssDownloadProgress>(p => reports.Add(p)));

        Assert.Equal(2, attempts);
        Assert.NotEmpty(reports);
        Assert.All(reports, r => Assert.True(
            r.DownloadedBytes <= r.TotalBytes,
            $"progress reported {r.DownloadedBytes} of {r.TotalBytes} bytes — a retried chunk was counted twice"));
    }
}

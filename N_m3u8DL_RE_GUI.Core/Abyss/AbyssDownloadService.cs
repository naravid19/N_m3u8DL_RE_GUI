using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace N_m3u8DL_RE_GUI.Core.Abyss;

/// <summary>
/// Downloads and reassembles fragmented video chunks from Abyss/Hydrax CDN servers.
/// Provides N_m3u8DL-RE consistent logging, progress reporting, and high throughput.
/// </summary>
public class AbyssDownloadService : IDisposable
{
    public const int DefaultFragmentSize = 2097152; // 2MB chunk
    public const int DefaultConcurrentConnections = 8;

    /// <summary>
    /// Base delay before a retried chunk is re-requested; the wait grows
    /// linearly with the attempt number. Exposed so the exhausted-retry
    /// path can be tested without spending 1+2+3+4+5 = 15 real seconds
    /// asleep, which was 75% of the entire .NET suite's runtime.
    /// </summary>
    public static readonly TimeSpan DefaultRetryBackoff = TimeSpan.FromSeconds(1);

    private readonly HttpClient _httpClient;
    private readonly bool _ownsHttpClient;
    private readonly TimeSpan _retryBackoff;

    public AbyssDownloadService(HttpClient httpClient = null, TimeSpan? retryBackoff = null)
    {
        _retryBackoff = retryBackoff ?? DefaultRetryBackoff;

        if (httpClient != null)
        {
            _httpClient = httpClient;
        }
        else
        {
            var handler = new HttpClientHandler
            {
                AutomaticDecompression = System.Net.DecompressionMethods.All,
                CheckCertificateRevocationList = false,
                MaxConnectionsPerServer = 64
            };
            _httpClient = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromSeconds(45)
            };
            _ownsHttpClient = true;
        }
    }

    /// <summary>
    /// Disposes the HttpClient this service created itself; a client passed in by
    /// the caller is left alone, as AbyssMetadataFetcher does.
    /// </summary>
    public void Dispose()
    {
        if (_ownsHttpClient)
            _httpClient.Dispose();
    }

    /// <summary>
    /// Constructs the base CDN URL for chunk downloading.
    /// </summary>
    public static string BuildSegmentBaseUrl(string primaryDomain, string subdomain)
    {
        if (string.IsNullOrEmpty(primaryDomain)) return $"https://{subdomain}.sssrr.org";

        string domainSuffix = primaryDomain;
        int dotIdx = primaryDomain.IndexOf('.');
        if (dotIdx >= 0 && dotIdx < primaryDomain.Length - 1)
        {
            domainSuffix = primaryDomain.Substring(dotIdx + 1);
        }

        return $"https://{subdomain}.{domainSuffix}";
    }

    /// <summary>
    /// Generates the double-Base64 segment token for a specific chunk index.
    /// </summary>
    public static string GenerateSegmentToken(int md5Id, int resId, long totalSize, int chunkSize, int chunkIndex, string sizeKeyHex)
    {
        string path = $"/mp4/{md5Id}/{resId}/{totalSize}/{chunkSize}/{chunkIndex}";
        byte[] encrypted = AbyssCrypto.EncryptAesCtr(path, sizeKeyHex);
        return AbyssCrypto.DoubleBase64(encrypted);
    }

    /// <summary>
    /// Downloads all segments in parallel and concatenates them into the destination MP4 file.
    /// </summary>
    public async Task DownloadAsync(
        AbyssMp4 mp4,
        AbyssSource source,
        string outputFilePath,
        IReadOnlyDictionary<string, string> customHeaders = null,
        int connections = DefaultConcurrentConnections,
        IProgress<AbyssDownloadProgress> progress = null,
        Action<string> log = null,
        CancellationToken cancellationToken = default)
    {
        if (mp4 == null) throw new ArgumentNullException(nameof(mp4));
        if (source == null) throw new ArgumentNullException(nameof(source));
        if (string.IsNullOrWhiteSpace(outputFilePath)) throw new ArgumentException("Output file path cannot be empty", nameof(outputFilePath));

        if (connections <= 0) connections = DefaultConcurrentConnections;

        string primaryDomain = mp4.Domains?.FirstOrDefault();
        string baseUrl = BuildSegmentBaseUrl(primaryDomain, source.Subdomain);
        string sizeKeyHex = AbyssCrypto.DeriveKey(source.Size);

        // AbyssVideoDownloader (VideoDownloader.kt) always signs and slices with a fixed
        // 2 MiB. This honours the size the source reports and falls back to that same
        // 2 MiB, so the two agree whenever the server sends the value it always has.
        int chunkSize = source.PartSize.HasValue && source.PartSize.Value > 0 ? source.PartSize.Value : DefaultFragmentSize;
        int totalChunks = (int)Math.Ceiling((double)source.Size / chunkSize);

        // User-Agent from custom headers if provided
        string userAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36";
        if (customHeaders != null)
        {
            foreach (var kvp in customHeaders)
            {
                if (kvp.Key.Equals("User-Agent", StringComparison.OrdinalIgnoreCase))
                {
                    userAgent = kvp.Value;
                    break;
                }
            }
        }

        // Setup temp directory (deterministic by slug and quality label for resuming)
        string outputDir = Path.GetDirectoryName(outputFilePath);
        if (string.IsNullOrEmpty(outputDir)) outputDir = Directory.GetCurrentDirectory();
        if (!Directory.Exists(outputDir)) Directory.CreateDirectory(outputDir);

        string cleanSlug = System.Text.RegularExpressions.Regex.Replace(mp4.Slug ?? "video", @"[^a-zA-Z0-9_\-]", "_");
        string cleanLabel = System.Text.RegularExpressions.Regex.Replace(source.Label ?? "res", @"[^a-zA-Z0-9_\-]", "_");
        string tempDirName = $"abyss_temp_{cleanSlug}_{cleanLabel}";
        string tempDirPath = Path.Combine(outputDir, tempDirName);
        Directory.CreateDirectory(tempDirPath);

        string ChunkPath(int index) => Path.Combine(tempDirPath, $"segment_{index:D6}.part");

        long totalDownloadedBytes = 0;
        int downloadedChunksCount = 0;
        int isDownloading = 1;
        var stopwatch = Stopwatch.StartNew();

        // Check for existing valid segments on disk to resume seamlessly
        var completedChunks = new HashSet<int>();
        for (int i = 0; i < totalChunks; i++)
        {
            string chunkFilePath = ChunkPath(i);
            if (File.Exists(chunkFilePath))
            {
                var fi = new FileInfo(chunkFilePath);
                long expectedLen = (i == totalChunks - 1) ? (source.Size - (long)i * chunkSize) : chunkSize;
                if (fi.Length == expectedLen)
                {
                    completedChunks.Add(i);
                    totalDownloadedBytes += fi.Length;
                    downloadedChunksCount++;
                }
            }
        }

        if (downloadedChunksCount > 0)
        {
            log?.Invoke($"{DateTime.Now:HH:mm:ss.fff} INFO : Resuming download: {downloadedChunksCount}/{totalChunks} segment(s) ({totalDownloadedBytes / 1024.0 / 1024.0:F1} MB) already exist on disk.");
        }

        log?.Invoke($"{DateTime.Now:HH:mm:ss.fff} INFO : Starting {connections} parallel segment workers (Total segments: {totalChunks})...");

        // Background reporter for smooth progress bar and periodic N_m3u8DL-RE style logs
        var progressReportingTask = Task.Run(async () =>
        {
            var lastLogStopwatch = Stopwatch.StartNew();
            while (Volatile.Read(ref isDownloading) == 1 && !cancellationToken.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(200, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }

                long currentBytes = Volatile.Read(ref totalDownloadedBytes);
                int currentChunks = Volatile.Read(ref downloadedChunksCount);
                double elapsedSec = stopwatch.Elapsed.TotalSeconds;
                double speed = elapsedSec > 0 ? (currentBytes / elapsedSec) : 0;
                long remainingBytes = Math.Max(0, source.Size - currentBytes);
                TimeSpan? eta = speed > 0 ? TimeSpan.FromSeconds(remainingBytes / speed) : null;

                var p = new AbyssDownloadProgress
                {
                    DownloadedChunks = currentChunks,
                    TotalChunks = totalChunks,
                    DownloadedBytes = currentBytes,
                    TotalBytes = source.Size,
                    SpeedBytesPerSec = speed,
                    Eta = eta
                };
                progress?.Report(p);

                if (lastLogStopwatch.ElapsedMilliseconds >= 1000 && currentBytes > 0)
                {
                    lastLogStopwatch.Restart();
                    log?.Invoke(p.FormatN_m3u8DL_RE_Line());
                }
            }
        }, CancellationToken.None);

        using var semaphore = new SemaphoreSlim(connections, connections);
        var downloadTasks = Enumerable.Range(0, totalChunks).Select(async chunkIndex =>
        {
            if (completedChunks.Contains(chunkIndex))
            {
                return;
            }

            await semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                cancellationToken.ThrowIfCancellationRequested();

                string token = GenerateSegmentToken(mp4.Md5Id, source.ResId, source.Size, chunkSize, chunkIndex, sizeKeyHex);
                string chunkUrl = $"{baseUrl}/sora/{source.Size}/{token}";
                string chunkFilePath = ChunkPath(chunkIndex);
                string tmpFilePath = ChunkPath(chunkIndex) + ".tmp";

                // Download with retry
                int retries = 0;
                const int maxRetries = 5;
                Exception lastException = null;

                while (true)
                {
                    try
                    {
                        using var request = new HttpRequestMessage(HttpMethod.Get, chunkUrl);
                        request.Headers.Add("User-Agent", userAgent);
                        request.Headers.Add("Referer", "https://abysscdn.com/");
                        request.Headers.Add("Origin", "https://abysscdn.com");

                        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
                        response.EnsureSuccessStatusCode();

                        long attemptBytes = 0;
                        try
                        {
                            using (var contentStream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false))
                            using (var fileStream = new FileStream(tmpFilePath, FileMode.Create, FileAccess.Write, FileShare.None, 65536, true))
                            {
                                byte[] buffer = new byte[65536];
                                int read;
                                while ((read = await contentStream.ReadAsync(buffer, 0, buffer.Length, cancellationToken).ConfigureAwait(false)) > 0)
                                {
                                    await fileStream.WriteAsync(buffer, 0, read, cancellationToken).ConfigureAwait(false);
                                    // Counted live so the bar moves smoothly, and
                                    // returned below if this attempt does not finish
                                    // -- otherwise a retry counts the same chunk twice
                                    // and the bar reaches 100% early.
                                    Interlocked.Add(ref totalDownloadedBytes, read);
                                    attemptBytes += read;
                                }
                            }
                        }
                        catch
                        {
                            Interlocked.Add(ref totalDownloadedBytes, -attemptBytes);
                            throw;
                        }

                        if (File.Exists(chunkFilePath)) File.Delete(chunkFilePath);
                        File.Move(tmpFilePath, chunkFilePath);

                        Interlocked.Increment(ref downloadedChunksCount);
                        break; // success
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
                        // The user pressed Stop. Neither handler below may see
                        // this: the retry filter would reject it anyway, and the
                        // fallback would wrap it in an HttpRequestException, which
                        // MainWindow paints as a red download failure instead of
                        // the neutral "Download stopped by user." it has a
                        // dedicated handler for.
                        throw;
                    }
                    catch (Exception ex) when (++retries <= maxRetries && !cancellationToken.IsCancellationRequested)
                    {
                        lastException = ex;
                        await Task.Delay(_retryBackoff * retries, cancellationToken).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        lastException = ex;
                        throw new HttpRequestException($"Failed to download segment {chunkIndex}/{totalChunks} from {chunkUrl} after {retries} retries: {ex.Message}", lastException);
                    }
                    finally
                    {
                        if (File.Exists(tmpFilePath))
                        {
                            try { File.Delete(tmpFilePath); } catch { }
                        }
                    }
                }
            }
            finally
            {
                semaphore.Release();
            }
        }).ToList();

        try
        {
            await Task.WhenAll(downloadTasks).ConfigureAwait(false);
        }
        finally
        {
            Volatile.Write(ref isDownloading, 0);
            try { await progressReportingTask.ConfigureAwait(false); } catch { }
        }

        log?.Invoke($"{DateTime.Now:HH:mm:ss.fff} INFO : All {totalChunks} segments downloaded successfully.");
        log?.Invoke($"{DateTime.Now:HH:mm:ss.fff} INFO : Merging segments into output MP4...");

        // A chunk lost between download and merge must fail the job, before an output
        // file exists: skipping it would write a truncated MP4 and report success.
        for (int i = 0; i < totalChunks; i++)
        {
            string chunkFilePath = ChunkPath(i);
            if (!File.Exists(chunkFilePath))
                throw new FileNotFoundException($"Segment {i + 1}/{totalChunks} is missing, so nothing was merged.", chunkFilePath);
        }

        // Concatenate all chunks in order into output file
        using (var outStream = new FileStream(outputFilePath, FileMode.Create, FileAccess.Write, FileShare.None, 131072, true))
        {
            for (int i = 0; i < totalChunks; i++)
            {
                string chunkFilePath = ChunkPath(i);
                using var inStream = new FileStream(chunkFilePath, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, true);
                await inStream.CopyToAsync(outStream, cancellationToken).ConfigureAwait(false);
            }
        }

        // Clean up temp dir
        try
        {
            if (Directory.Exists(tempDirPath))
                Directory.Delete(tempDirPath, true);
        }
        catch (Exception ex)
        {
            // The download itself succeeded; a leftover temp folder is only clutter.
            log?.Invoke($"{DateTime.Now:HH:mm:ss.fff} WARN : Could not remove the temp folder {tempDirPath}: {ex.Message}");
        }

        // Final 100% progress report
        if (progress != null)
        {
            double elapsedSec = stopwatch.Elapsed.TotalSeconds;
            progress.Report(new AbyssDownloadProgress
            {
                DownloadedChunks = totalChunks,
                TotalChunks = totalChunks,
                DownloadedBytes = source.Size,
                TotalBytes = source.Size,
                SpeedBytesPerSec = elapsedSec > 0 ? (source.Size / elapsedSec) : 0,
                Eta = TimeSpan.Zero
            });
        }

        log?.Invoke($"{DateTime.Now:HH:mm:ss.fff} INFO : Download finished! Saved to {outputFilePath}");
    }
}

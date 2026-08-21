using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace N_m3u8DL_RE_GUI.Core.Abyss
{
    /// <summary>
    /// Downloads and reassembles fragmented video chunks from Abyss/Hydrax CDN servers.
    /// Provides N_m3u8DL-RE consistent logging, progress reporting, and high throughput.
    /// </summary>
    public class AbyssDownloadService
    {
        public const int DefaultFragmentSize = 2097152; // 2MB chunk
        public const int DefaultConcurrentConnections = 8;

        private readonly HttpClient _httpClient;

        public AbyssDownloadService(HttpClient httpClient = null)
        {
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
            }
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

            // Setup temp directory
            string outputDir = Path.GetDirectoryName(outputFilePath);
            if (string.IsNullOrEmpty(outputDir)) outputDir = Directory.GetCurrentDirectory();
            if (!Directory.Exists(outputDir)) Directory.CreateDirectory(outputDir);

            string tempDirName = $"abyss_temp_{mp4.Slug}_{source.Label}_{DateTime.UtcNow.Ticks}";
            string tempDirPath = Path.Combine(outputDir, tempDirName);
            Directory.CreateDirectory(tempDirPath);

            long totalDownloadedBytes = 0;
            int downloadedChunksCount = 0;
            int isDownloading = 1;
            var stopwatch = Stopwatch.StartNew();

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
                await semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
                try
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    string token = GenerateSegmentToken(mp4.Md5Id, source.ResId, source.Size, chunkSize, chunkIndex, sizeKeyHex);
                    string chunkUrl = $"{baseUrl}/sora/{source.Size}/{token}";
                    string chunkFilePath = Path.Combine(tempDirPath, $"segment_{chunkIndex:D6}.tmp");

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

                            using var contentStream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
                            using var fileStream = new FileStream(chunkFilePath, FileMode.Create, FileAccess.Write, FileShare.None, 65536, true);

                            byte[] buffer = new byte[65536];
                            int read;
                            while ((read = await contentStream.ReadAsync(buffer, 0, buffer.Length, cancellationToken).ConfigureAwait(false)) > 0)
                            {
                                await fileStream.WriteAsync(buffer, 0, read, cancellationToken).ConfigureAwait(false);
                                Interlocked.Add(ref totalDownloadedBytes, read);
                            }

                            Interlocked.Increment(ref downloadedChunksCount);
                            break; // success
                        }
                        catch (Exception ex) when (++retries <= maxRetries && !cancellationToken.IsCancellationRequested)
                        {
                            lastException = ex;
                            await Task.Delay(1000 * retries, cancellationToken).ConfigureAwait(false);
                        }
                        catch (Exception ex)
                        {
                            lastException = ex;
                            throw new HttpRequestException($"Failed to download segment {chunkIndex}/{totalChunks} from {chunkUrl} after {retries} retries: {ex.Message}", lastException);
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

            // Concatenate all chunks in order into output file
            using (var outStream = new FileStream(outputFilePath, FileMode.Create, FileAccess.Write, FileShare.None, 131072, true))
            {
                for (int i = 0; i < totalChunks; i++)
                {
                    string chunkFilePath = Path.Combine(tempDirPath, $"segment_{i:D6}.tmp");
                    if (File.Exists(chunkFilePath))
                    {
                        using var inStream = new FileStream(chunkFilePath, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, true);
                        await inStream.CopyToAsync(outStream, cancellationToken).ConfigureAwait(false);
                    }
                }
            }

            // Clean up temp dir
            try
            {
                if (Directory.Exists(tempDirPath))
                    Directory.Delete(tempDirPath, true);
            }
            catch { }

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
}

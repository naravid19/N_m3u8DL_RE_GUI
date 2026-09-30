using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace N_m3u8DL_RE_GUI.Core.Abyss
{
    /// <summary>
    /// Fetches and decrypts video metadata from Abyss/Hydrax video hosts.
    /// </summary>
    public static class AbyssMetadataFetcher
    {
        private static readonly Regex DatasRegex = new Regex(@"const\s+datas\s*=\s*""([^""]+)""", RegexOptions.Compiled);
        private static readonly Regex VideoIdRegex = new Regex(@"[?&]v=([a-zA-Z0-9_-]+)", RegexOptions.Compiled);

        /// <summary>
        /// Checks if a given string or URL belongs to an Abyss/Hydrax host.
        /// </summary>
        public static bool IsAbyssUrl(string url)
        {
            if (string.IsNullOrWhiteSpace(url)) return false;
            string lower = url.ToLowerInvariant();
            return lower.Contains("abysscdn.com") ||
                   lower.Contains("playhydrax.com") ||
                   lower.Contains("zplayer.io") ||
                   lower.Contains("abyss.to") ||
                   lower.Contains("short.ink");
        }

        /// <summary>
        /// Extracts the video ID from a URL or returns the input if already an ID.
        /// </summary>
        public static string ExtractVideoId(string urlOrId)
        {
            if (string.IsNullOrWhiteSpace(urlOrId)) return string.Empty;

            if (urlOrId.Contains("://") || urlOrId.Contains("/"))
            {
                var match = VideoIdRegex.Match(urlOrId);
                if (match.Success) return match.Groups[1].Value;

                if (urlOrId.Contains("short.ink/"))
                {
                    int idx = urlOrId.LastIndexOf('/');
                    if (idx >= 0 && idx < urlOrId.Length - 1)
                        return urlOrId.Substring(idx + 1).Trim();
                }
            }

            return urlOrId.Trim();
        }

        /// <summary>
        /// Parses and decrypts the embedded video metadata from the page HTML.
        /// </summary>
        public static AbyssMp4 ParseMetadataFromHtml(string html)
        {
            if (string.IsNullOrWhiteSpace(html))
                throw new ArgumentException("HTML content is empty", nameof(html));

            var match = DatasRegex.Match(html);
            if (!match.Success)
            {
                throw new InvalidOperationException("No encoded 'datas' metadata found in the provided HTML.");
            }

            string rawBase64 = match.Groups[1].Value;
            byte[] rawBytes = Convert.FromBase64String(rawBase64);
            string isoDecoded = Encoding.GetEncoding("ISO-8859-1").GetString(rawBytes);

            var datas = JsonSerializer.Deserialize<AbyssDatas>(isoDecoded);
            if (datas == null || string.IsNullOrEmpty(datas.Media))
            {
                throw new InvalidOperationException("Failed to parse 'datas' payload from decoded JSON.");
            }

            // Derive key: MD5("${user_id}:${slug}:${md5_id}")
            string mediaKey = $"{datas.UserId}:{datas.Slug}:{datas.Md5Id}";
            string keyHex = AbyssCrypto.DeriveKey(mediaKey);

            // Decrypt media payload
            string decryptedJson = AbyssCrypto.DecryptString(datas.Media, keyHex);
            var videoPayload = JsonSerializer.Deserialize<AbyssVideoPayload>(decryptedJson);

            if (videoPayload?.Mp4 == null)
            {
                throw new InvalidOperationException("Decrypted payload does not contain valid MP4 sources.");
            }

            videoPayload.Mp4.Slug = datas.Slug;
            videoPayload.Mp4.Md5Id = datas.Md5Id;

            return videoPayload.Mp4;
        }

        /// <summary>
        /// Fetches the HTML from Abyss CDN and decrypts metadata, applying custom headers if provided.
        /// Uses HttpClient with an automatic fallback to Windows native curl.exe to bypass Cloudflare TLS challenges.
        /// </summary>
        public static async Task<AbyssMp4> FetchMetadataAsync(
            string urlOrId,
            System.Collections.Generic.IReadOnlyDictionary<string, string> customHeaders = null,
            HttpClient httpClient = null,
            CancellationToken cancellationToken = default)
        {
            string videoId = ExtractVideoId(urlOrId);
            if (string.IsNullOrEmpty(videoId))
                throw new ArgumentException("Invalid or missing video ID", nameof(urlOrId));

            string targetUrl = $"https://abysscdn.com/?v={videoId}";

            string userAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36";
            string referer = "https://abysscdn.com/";

            if (customHeaders != null)
            {
                foreach (var kvp in customHeaders)
                {
                    if (kvp.Key.Equals("User-Agent", StringComparison.OrdinalIgnoreCase))
                        userAgent = kvp.Value;
                    else if (kvp.Key.Equals("Referer", StringComparison.OrdinalIgnoreCase))
                        referer = kvp.Value;
                }
            }

            string html = null;
            Exception httpException = null;

            // Strategy 1: Try in-process HttpClient
            try
            {
                bool disposeClient = false;
                var client = httpClient;
                if (client == null)
                {
                    var handler = new HttpClientHandler
                    {
                        AutomaticDecompression = System.Net.DecompressionMethods.All
                    };
                    client = new HttpClient(handler);
                    disposeClient = true;
                }

                try
                {
                    using var request = new HttpRequestMessage(HttpMethod.Get, targetUrl);
                    if (customHeaders != null)
                    {
                        foreach (var kvp in customHeaders)
                        {
                            if (!kvp.Key.Equals("User-Agent", StringComparison.OrdinalIgnoreCase) &&
                                !kvp.Key.Equals("Referer", StringComparison.OrdinalIgnoreCase))
                            {
                                request.Headers.TryAddWithoutValidation(kvp.Key, kvp.Value);
                            }
                        }
                    }

                    request.Headers.Add("User-Agent", userAgent);
                    request.Headers.Add("Referer", referer);

                    using var response = await client.SendAsync(request, cancellationToken).ConfigureAwait(false);
                    if (response.IsSuccessStatusCode)
                    {
                        string body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                        if (!IsCloudflareChallenge(body))
                        {
                            html = body;
                        }
                    }
                    else
                    {
                        httpException = new HttpRequestException($"Response status code does not indicate success: {(int)response.StatusCode} ({response.ReasonPhrase}).");
                    }
                }
                finally
                {
                    if (disposeClient)
                    {
                        client.Dispose();
                    }
                }
            }
            catch (Exception ex) when (ex is HttpRequestException or System.Net.Sockets.SocketException)
            {
                httpException = ex;
            }

            // Strategy 2: If HttpClient failed (e.g. Cloudflare 403 TLS challenge), fall back to native curl.exe
            if (string.IsNullOrEmpty(html))
            {
                try
                {
                    html = await FetchHtmlViaCurlAsync(targetUrl, userAgent, referer, customHeaders, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception curlEx)
                {
                    if (httpException != null)
                    {
                        throw new InvalidOperationException($"Failed to connect to Abyss host via HttpClient ({httpException.Message}) and curl ({curlEx.Message}).", httpException);
                    }
                    throw;
                }
            }

            if (string.IsNullOrEmpty(html) || IsCloudflareChallenge(html))
            {
                throw new InvalidOperationException("Failed to bypass Cloudflare protection on Abyss host. Please ensure the Referer header is correct.");
            }

            return ParseMetadataFromHtml(html);
        }

        private static bool IsCloudflareChallenge(string html)
        {
            if (string.IsNullOrWhiteSpace(html)) return false;
            return html.Contains("<title>Just a moment...</title>", StringComparison.OrdinalIgnoreCase) ||
                   html.Contains("cf-mitigated", StringComparison.OrdinalIgnoreCase) ||
                   html.Contains("challenges.cloudflare.com", StringComparison.OrdinalIgnoreCase);
        }

        private static async Task<string> FetchHtmlViaCurlAsync(
            string targetUrl,
            string userAgent,
            string referer,
            System.Collections.Generic.IReadOnlyDictionary<string, string> customHeaders,
            CancellationToken cancellationToken)
        {
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "curl.exe",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8
            };

            psi.ArgumentList.Add("-s");
            psi.ArgumentList.Add("-L");
            psi.ArgumentList.Add("-H");
            psi.ArgumentList.Add($"User-Agent: {userAgent}");
            psi.ArgumentList.Add("-H");
            psi.ArgumentList.Add($"Referer: {referer}");

            if (customHeaders != null)
            {
                foreach (var kvp in customHeaders)
                {
                    if (!kvp.Key.Equals("User-Agent", StringComparison.OrdinalIgnoreCase) &&
                        !kvp.Key.Equals("Referer", StringComparison.OrdinalIgnoreCase))
                    {
                        psi.ArgumentList.Add("-H");
                        psi.ArgumentList.Add($"{kvp.Key}: {kvp.Value}");
                    }
                }
            }

            psi.ArgumentList.Add(targetUrl);

            using var process = new System.Diagnostics.Process { StartInfo = psi };
            process.Start();

            var readTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);

            string result = await readTask.ConfigureAwait(false);

            // If curl failed (e.g. exit code 6 DNS resolve failure), try with DNS-over-HTTPS fallback
            if (process.ExitCode != 0 && (string.IsNullOrEmpty(result) || IsCloudflareChallenge(result)))
            {
                var dohPsi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "curl.exe",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    StandardOutputEncoding = Encoding.UTF8
                };

                dohPsi.ArgumentList.Add("--doh-url");
                dohPsi.ArgumentList.Add("https://1.1.1.1/dns-query");
                dohPsi.ArgumentList.Add("-s");
                dohPsi.ArgumentList.Add("-L");
                dohPsi.ArgumentList.Add("-H");
                dohPsi.ArgumentList.Add($"User-Agent: {userAgent}");
                dohPsi.ArgumentList.Add("-H");
                dohPsi.ArgumentList.Add($"Referer: {referer}");

                if (customHeaders != null)
                {
                    foreach (var kvp in customHeaders)
                    {
                        if (!kvp.Key.Equals("User-Agent", StringComparison.OrdinalIgnoreCase) &&
                            !kvp.Key.Equals("Referer", StringComparison.OrdinalIgnoreCase))
                        {
                            dohPsi.ArgumentList.Add("-H");
                            dohPsi.ArgumentList.Add($"{kvp.Key}: {kvp.Value}");
                        }
                    }
                }

                dohPsi.ArgumentList.Add(targetUrl);

                using var dohProcess = new System.Diagnostics.Process { StartInfo = dohPsi };
                dohProcess.Start();

                var dohReadTask = dohProcess.StandardOutput.ReadToEndAsync(cancellationToken);
                await dohProcess.WaitForExitAsync(cancellationToken).ConfigureAwait(false);

                result = await dohReadTask.ConfigureAwait(false);
            }

            return result;
        }
    }
}

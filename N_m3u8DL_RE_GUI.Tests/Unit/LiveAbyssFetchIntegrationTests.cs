using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using N_m3u8DL_RE_GUI.Core.Abyss;
using Xunit;

namespace N_m3u8DL_RE_GUI.Tests.Unit
{
    public class LiveAbyssFetchIntegrationTests
    {
        [Fact(Skip = "Live network integration test")]
        public async Task FetchMetadataAsync_WithLiveAbyssUrl_Succeeds()
        {
            string url = "https://abysscdn.com/?v=ahozDvaga";
            var headers = new Dictionary<string, string>
            {
                ["Referer"] = "https://player.marimo.me/demo/?key=19qk0qWgZpKkpsOoj6uc1Z-a1rTVj9XY2p4&vid=18fjveikp2G745vBxo-n3NDZ1dKdtt66pNDhnryglnqSw7jTpbrDo6Of37nArdCLueeFk7LacqG1yszbxsOwp9SZ7pu86A",
                ["User-Agent"] = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/151.0.0.0 Safari/537.36 Edg/151.0.0.0"
            };

            var mp4 = await AbyssMetadataFetcher.FetchMetadataAsync(url, customHeaders: headers);
            Assert.NotNull(mp4);

            var source = mp4.Sources.OrderByDescending(s => s.Size).First();
            string primaryDomain = mp4.Domains[0];
            string baseUrl = AbyssDownloadService.BuildSegmentBaseUrl(primaryDomain, source.Subdomain);
            string sizeKeyHex = AbyssCrypto.DeriveKey(source.Size);
            int chunkSize = source.PartSize.HasValue && source.PartSize.Value > 0 ? source.PartSize.Value : 2097152;

            string token0 = AbyssDownloadService.GenerateSegmentToken(mp4.Md5Id, source.ResId, source.Size, chunkSize, 0, sizeKeyHex);
            string chunkUrl = $"{baseUrl}/sora/{source.Size}/{token0}";

            var handler = new System.Net.Http.HttpClientHandler { AutomaticDecompression = System.Net.DecompressionMethods.All };
            using var client = new System.Net.Http.HttpClient(handler);

            using var req1 = new System.Net.Http.HttpRequestMessage(System.Net.Http.HttpMethod.Get, chunkUrl);
            req1.Headers.Add("User-Agent", headers["User-Agent"]);
            req1.Headers.Add("Referer", "https://abysscdn.com/");
            var resp1 = await client.SendAsync(req1);
            byte[] b1 = await resp1.Content.ReadAsByteArrayAsync();

            Assert.True(resp1.IsSuccessStatusCode, $"chunkUrl: {chunkUrl} | Code: {(int)resp1.StatusCode} | Len: {b1.Length}");
            Assert.True(b1.Length > 0, "Chunk byte length is 0");
        }
    }
}

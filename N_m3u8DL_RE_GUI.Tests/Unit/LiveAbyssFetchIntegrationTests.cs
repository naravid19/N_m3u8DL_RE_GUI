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
            Assert.Equal("ahozDvaga", mp4.Slug);
            Assert.NotEmpty(mp4.Sources);
        }
    }
}

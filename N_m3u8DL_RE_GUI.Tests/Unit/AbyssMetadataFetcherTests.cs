using System;
using System.Linq;
using N_m3u8DL_RE_GUI.Core.Abyss;
using Xunit;

namespace N_m3u8DL_RE_GUI.Tests.Unit
{
    public class AbyssMetadataFetcherTests
    {
        [Theory]
        [InlineData("https://abysscdn.com/?v=EivD8IFMyk", true)]
        [InlineData("https://playhydrax.com/?v=abc1234", true)]
        [InlineData("https://zplayer.io/?v=xyz999", true)]
        [InlineData("https://short.ink/target_video", true)]
        [InlineData("https://example.com/video.m3u8", false)]
        [InlineData("https://test.com/stream.mpd", false)]
        public void IsAbyssUrl_IdentifiesSupportedDomains(string url, bool expected)
        {
            Assert.Equal(expected, AbyssMetadataFetcher.IsAbyssUrl(url));
        }

        [Theory]
        [InlineData("https://abysscdn.com/?v=EivD8IFMyk", "EivD8IFMyk")]
        [InlineData("https://playhydrax.com/?v=testVideo123", "testVideo123")]
        [InlineData("https://short.ink/abcXYZ", "abcXYZ")]
        [InlineData("EivD8IFMyk", "EivD8IFMyk")]
        public void ExtractVideoId_ExtractsCorrectSlug(string input, string expected)
        {
            Assert.Equal(expected, AbyssMetadataFetcher.ExtractVideoId(input));
        }

        [Fact]
        public void BuildSegmentBaseUrl_ConstructsProperCdnDomain()
        {
            string primaryDomain = "0bud01ado11.sssrr.org";
            string subdomain = "j4vbathl34";

            string url = AbyssDownloadService.BuildSegmentBaseUrl(primaryDomain, subdomain);
            Assert.Equal("https://j4vbathl34.sssrr.org", url);
        }

        [Fact]
        public void GenerateSegmentToken_ProducesExpectedDoubleBase64Format()
        {
            int md5Id = 29438996;
            int resId = 5;
            long size = 393459318;
            int chunkSize = 2097152;
            int chunkIndex = 0;
            string sizeKeyHex = AbyssCrypto.DeriveKey(size);

            string token = AbyssDownloadService.GenerateSegmentToken(md5Id, resId, size, chunkSize, chunkIndex, sizeKeyHex);
            Assert.NotEmpty(token);
            Assert.DoesNotContain("=", token);
            Assert.Equal("ZTdYQ2c1N0tPc2ZZOStXdGQrbXltUGh1OHg0YnJFYmFaYVZ4NUovM3g2NUg3emM", token);
        }

        [Fact]
        public void ParseMetadataFromHtml_ThrowsOnInvalidHtml()
        {
            Assert.Throws<InvalidOperationException>(() => AbyssMetadataFetcher.ParseMetadataFromHtml("<html><body>No datas here</body></html>"));
        }

        [Fact]
        public void ParseMetadataFromHtml_ThrowsOnCloudflareChallenge()
        {
            string challengeHtml = "<html><head><title>Just a moment...</title></head><body>Enable JavaScript</body></html>";
            Assert.Throws<InvalidOperationException>(() => AbyssMetadataFetcher.ParseMetadataFromHtml(challengeHtml));
        }
    }
}

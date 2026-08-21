using System.Collections.Generic;
using N_m3u8DL_RE_GUI.Core.Capture;
using Xunit;

namespace N_m3u8DL_RE_GUI.Tests.Unit.Core
{
    public class HeaderParserTests
    {
        [Fact]
        public void Parse_WithEmptyInput_ReturnsEmptyDictionary()
        {
            var result = HeaderParser.Parse(null);
            Assert.Empty(result);

            result = HeaderParser.Parse("   \n\r  ");
            Assert.Empty(result);
        }

        [Fact]
        public void Parse_WithStandardLines_ExtractsHeadersCaseInsensitively()
        {
            string raw = "Referer: https://example.com/embed\nUser-Agent: Mozilla/5.0 Test\nCookie: session=123";
            var headers = HeaderParser.Parse(raw);

            Assert.Equal(3, headers.Count);
            Assert.Equal("https://example.com/embed", headers["referer"]);
            Assert.Equal("Mozilla/5.0 Test", headers["user-agent"]);
            Assert.Equal("session=123", headers["cookie"]);
        }

        [Fact]
        public void Parse_WithCurlDashHFlagsAndQuotes_ExtractsCleanHeaders()
        {
            string raw = "-H \"Referer: https://player.marimo.me/demo/?key=xyz\"\n-H 'User-Agent: CustomUA/1.0'";
            var headers = HeaderParser.Parse(raw);

            Assert.Equal(2, headers.Count);
            Assert.Equal("https://player.marimo.me/demo/?key=xyz", headers["Referer"]);
            Assert.Equal("CustomUA/1.0", headers["User-Agent"]);
        }

        [Fact]
        public void Parse_WithPipeDelimitedLines_ExtractsAllHeaders()
        {
            string raw = "Referer: https://test.org/|Origin: https://test.org";
            var headers = HeaderParser.Parse(raw);

            Assert.Equal(2, headers.Count);
            Assert.Equal("https://test.org/", headers["referer"]);
            Assert.Equal("https://test.org", headers["origin"]);
        }
    }
}

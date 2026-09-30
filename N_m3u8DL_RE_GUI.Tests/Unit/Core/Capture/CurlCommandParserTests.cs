#nullable enable
using System.Linq;
using N_m3u8DL_RE_GUI.Core.Capture;
using Xunit;

namespace N_m3u8DL_RE_GUI.Tests.Unit.Core.Capture;

public class CurlCommandParserTests
{
    [Theory]
    [InlineData("curl 'https://example.com/a.m3u8'")]
    [InlineData("  curl https://example.com/a.m3u8")]
    [InlineData("CURL 'https://example.com/a.m3u8'")]
    public void LooksLikeCurl_AcceptsCommandsRegardlessOfCaseAndLeadingSpace(string text)
    {
        Assert.True(CurlCommandParser.LooksLikeCurl(text));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("https://example.com/a.m3u8")]
    [InlineData("curling is a sport")]
    public void LooksLikeCurl_RejectsAnythingElse(string? text)
    {
        Assert.False(CurlCommandParser.LooksLikeCurl(text));
    }

    [Fact]
    public void Parse_BashDialect_ExtractsUrlAndHeaders()
    {
        const string command = """
            curl 'https://cdn.example.com/hls/master.m3u8' \
              -H 'Referer: https://player.example.com/' \
              -H 'User-Agent: Mozilla/5.0 (Windows NT 10.0; Win64; x64)' \
              --compressed
            """;

        var result = CurlCommandParser.Parse(command);

        Assert.NotNull(result);
        Assert.Equal("https://cdn.example.com/hls/master.m3u8", result!.Url);
        Assert.Equal(CapturedStreamKind.Hls, result.Kind);
        Assert.Equal(2, result.Headers.Count);
        Assert.Contains(result.Headers, h => h.Name == "Referer" && h.Value == "https://player.example.com/");
    }

    [Fact]
    public void Parse_CmdDialect_UnwrapsCaretAndBackslashEscapes()
    {
        const string command = "curl \"https://cdn.example.com/a.m3u8\" ^\r\n  -H \"X-Token: ^\\\"abc^\\\"\"";

        var result = CurlCommandParser.Parse(command);

        Assert.NotNull(result);
        Assert.Equal("https://cdn.example.com/a.m3u8", result!.Url);
        Assert.Contains(result.Headers, h => h.Name == "X-Token" && h.Value == "\"abc\"");
    }

    [Fact]
    public void Parse_BashEscapedSingleQuote_IsReassembledIntoOneToken()
    {
        // 'a'\''b' is the bash idiom for the literal a'b
        const string command = @"curl 'https://example.com/a.m3u8' -H 'X-N: a'\''b'";

        var result = CurlCommandParser.Parse(command);

        Assert.Contains(result!.Headers, h => h.Name == "X-N" && h.Value == "a'b");
    }

    [Fact]
    public void Parse_AppliesHeaderPolicy()
    {
        const string command = """
            curl 'https://example.com/a.m3u8' \
              -H 'sec-fetch-dest: empty' \
              -H 'accept-encoding: gzip, deflate, br' \
              -H 'Referer: https://example.com/'
            """;

        var result = CurlCommandParser.Parse(command);

        Assert.Single(result!.Headers);
        Assert.Equal("Referer", result.Headers[0].Name);
    }

    [Fact]
    public void Parse_LongFormHeaderFlag_IsSupported()
    {
        var result = CurlCommandParser.Parse(
            "curl 'https://example.com/a.m3u8' --header 'Referer: https://example.com/'");

        Assert.Single(result!.Headers);
    }

    [Fact]
    public void Parse_CookieFlag_BecomesACookieHeader()
    {
        var result = CurlCommandParser.Parse(
            "curl 'https://example.com/a.m3u8' -b 'session=abc; theme=dark'");

        Assert.Contains(result!.Headers, h => h.Name == "Cookie" && h.Value == "session=abc; theme=dark");
    }

    [Fact]
    public void Parse_ExplicitCookieHeaderIsNotDuplicatedByCookieFlag()
    {
        var result = CurlCommandParser.Parse(
            "curl 'https://example.com/a.m3u8' -H 'Cookie: a=1' -b 'b=2'");

        Assert.Single(result!.Headers, h => h.Name == "Cookie");
    }

    [Theory]
    [InlineData("curl 'https://example.com/manifest.mpd'", CapturedStreamKind.Dash)]
    [InlineData("curl 'https://example.com/master.m3u8?token=x'", CapturedStreamKind.Hls)]
    [InlineData("curl 'https://example.com/video.mp4'", CapturedStreamKind.Media)]
    [InlineData("curl 'https://example.com/page'", CapturedStreamKind.Unknown)]
    public void Parse_ClassifiesByUrlPathIgnoringQuery(string command, CapturedStreamKind expected)
    {
        Assert.Equal(expected, CurlCommandParser.Parse(command)!.Kind);
    }

    [Fact]
    public void Parse_SkipsFlagValuesWhenLookingForTheUrl()
    {
        // -X GET must not make "GET" a URL candidate, and the URL comes after it.
        var result = CurlCommandParser.Parse(
            "curl -X GET --compressed 'https://example.com/a.m3u8'");

        Assert.Equal("https://example.com/a.m3u8", result!.Url);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("curl --compressed")]
    [InlineData("curl 'ftp://example.com/a.m3u8'")]
    [InlineData("not a curl command at all")]
    public void Parse_ReturnsNullWhenThereIsNoUsableHttpUrl(string? command)
    {
        Assert.Null(CurlCommandParser.Parse(command));
    }

    [Fact]
    public void Parse_MalformedHeaderWithoutColon_IsIgnoredNotCrashed()
    {
        var result = CurlCommandParser.Parse(
            "curl 'https://example.com/a.m3u8' -H 'GarbageWithNoColon'");

        Assert.NotNull(result);
        Assert.Empty(result!.Headers);
    }

    [Fact]
    public void Parse_TrailingHeaderFlagWithNoValue_IsIgnoredNotCrashed()
    {
        var result = CurlCommandParser.Parse("curl 'https://example.com/a.m3u8' -H");

        Assert.NotNull(result);
        Assert.Empty(result!.Headers);
    }
}

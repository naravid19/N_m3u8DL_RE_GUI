using N_m3u8DL_RE_GUI.Core;
using N_m3u8DL_RE_GUI.Core.Capture;
using Xunit;

namespace N_m3u8DL_RE_GUI.Tests.Unit.Core.Capture;

public class HeaderSeparatorTests
{
    [Fact]
    public void SplitHeaderLines_PipeInCookieWithNewlines_PreservesCookieIntact()
    {
        // C2 defect: cookie containing '|' was shattered when splitting on pipe unconditionally.
        var raw = "Referer: https://example.com/\nCookie: wordpress_logged_in_9c1=alice|1755123456|9af3c2\nUser-Agent: Mozilla/5.0";
        var lines = HeaderParser.SplitHeaderLines(raw);

        Assert.Equal(3, lines.Length);
        Assert.Equal("Referer: https://example.com/", lines[0]);
        Assert.Equal("Cookie: wordpress_logged_in_9c1=alice|1755123456|9af3c2", lines[1]);
        Assert.Equal("User-Agent: Mozilla/5.0", lines[2]);

        var dict = HeaderParser.Parse(raw);
        Assert.Equal(3, dict.Count);
        Assert.Equal("wordpress_logged_in_9c1=alice|1755123456|9af3c2", dict["Cookie"]);
    }

    [Fact]
    public void SplitHeaderLines_HandlesCrLfAndTrailingNewlines()
    {
        var raw = "A: 1\r\nB: 2\rC: 3\n\n";
        var lines = HeaderParser.SplitHeaderLines(raw);

        Assert.Equal(3, lines.Length);
        Assert.Equal("A: 1", lines[0]);
        Assert.Equal("B: 2", lines[1]);
        Assert.Equal("C: 3", lines[2]);
    }

    [Fact]
    public void SplitHeaderLines_LegacyPipeWithoutNewlines_SplitsByPipe()
    {
        var raw = "Referer: https://example.com/|Cookie: foo=bar|Origin: https://example.com";
        var lines = HeaderParser.SplitHeaderLines(raw);

        Assert.Equal(3, lines.Length);
        Assert.Equal("Referer: https://example.com/", lines[0]);
        Assert.Equal("Cookie: foo=bar", lines[1]);
        Assert.Equal("Origin: https://example.com", lines[2]);

        var dict = HeaderParser.Parse(raw);
        Assert.Equal("foo=bar", dict["Cookie"]);
    }

    [Fact]
    public void ArgsBuilder_PreservesPipeInsideCookieWhenNewlineFormatted()
    {
        var options = new DownloadOptions
        {
            Input = "https://example.com/stream.m3u8",
            Headers = "Referer: https://example.com/\nCookie: auth=token|part2|part3"
        };
        var args = ArgsBuilder.Build(options);

        Assert.Contains("-H \"Referer: https://example.com/\"", args);
        Assert.Contains("-H \"Cookie: auth=token|part2|part3\"", args);
        Assert.DoesNotContain("-H \"part2\"", args);
        Assert.DoesNotContain("-H \"part3\"", args);
    }

    [Fact]
    public void ArgsBuilder_LegacyPipeFormatted_EmitsMultipleHFlags()
    {
        var options = new DownloadOptions
        {
            Input = "https://example.com/stream.m3u8",
            Headers = "Referer: https://example.com/|Cookie: auth=token"
        };
        var args = ArgsBuilder.Build(options);

        Assert.Contains("-H \"Referer: https://example.com/\"", args);
        Assert.Contains("-H \"Cookie: auth=token\"", args);
    }
}

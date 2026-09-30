#nullable enable
using System.Text;
using N_m3u8DL_RE_GUI.Core;
using Xunit;

namespace N_m3u8DL_RE_GUI.Tests.Unit.Core;

public class HtmlTitleExtractorTests
{
    [Theory]
    [InlineData("<html><head><title>Episode 01</title></head>", "Episode 01")]
    [InlineData("<title data-x=\"1\" lang=\"th\">รายการที่ 5</title>", "รายการที่ 5")]
    [InlineData("<title>\n   Spaced   \n</title>", "Spaced")]
    [InlineData("<TITLE>Upper Case Tag</TITLE>", "Upper Case Tag")]
    [InlineData("<html><body>no title</body></html>", "")]
    [InlineData("<title></title>", "")]
    [InlineData("", "")]
    public void Extract_ShouldReturnTheCleanedTitleOrEmpty(string html, string expected)
    {
        Assert.Equal(expected, HtmlTitleExtractor.Extract(html));
    }

    [Theory]
    [InlineData("A:B/C?D*E|F\"G", "ABCDEFG")]
    [InlineData("My Video_哔哩哔哩", "My Video")]
    [InlineData("My Video - WeTV", "My Video")]
    [InlineData("My Video_腾讯视频", "My Video")]
    [InlineData("My Video_爱奇艺", "My Video")]
    [InlineData("My Video_优酷", "My Video")]
    [InlineData("   padded   ", "padded")]
    [InlineData("", "")]
    public void Clean_ShouldStripIllegalCharactersAndKnownSiteSuffixes(string raw, string expected)
    {
        Assert.Equal(expected, HtmlTitleExtractor.Clean(raw));
    }

    [Fact]
    public void ContainsClosingTitleTag_ShouldFindATagSplitAcrossTwoChunks()
    {
        // THE BUG this replaces: the old code called sb.ToString().Contains() on the whole
        // accumulated buffer every chunk, which is O(N^2). A carry of 7 chars is enough to
        // catch "</title>" no matter where the chunk boundary lands.
        var carry = string.Empty;

        Assert.False(HtmlTitleExtractor.ContainsClosingTitleTag("<title>Some Name</ti", ref carry));
        Assert.True(HtmlTitleExtractor.ContainsClosingTitleTag("tle></head>", ref carry));
    }

    [Fact]
    public void ContainsClosingTitleTag_ShouldFindATagFullyInsideOneChunk()
    {
        var carry = string.Empty;

        Assert.True(HtmlTitleExtractor.ContainsClosingTitleTag("<title>X</title>", ref carry));
    }

    [Fact]
    public void ContainsClosingTitleTag_ShouldNotFalselyMatchAcrossUnrelatedChunks()
    {
        var carry = string.Empty;

        Assert.False(HtmlTitleExtractor.ContainsClosingTitleTag("aaaaaaaaaaaaaaaa", ref carry));
        Assert.False(HtmlTitleExtractor.ContainsClosingTitleTag("bbbbbbbbbbbbbbbb", ref carry));
    }

    [Fact]
    public void ContainsClosingTitleTag_ShouldBeCaseInsensitive()
    {
        var carry = string.Empty;

        Assert.True(HtmlTitleExtractor.ContainsClosingTitleTag("<TITLE>X</TITLE>", ref carry));
    }

    [Theory]
    [InlineData("utf-8", "utf-8")]
    [InlineData("UTF-8", "utf-8")]
    [InlineData("\"utf-8\"", "utf-8")]
    [InlineData("gb2312", "gb2312")]
    [InlineData("gbk", "gb2312")]
    [InlineData("big5", "big5")]
    [InlineData("shift_jis", "shift_jis")]
    [InlineData("iso-8859-1", "iso-8859-1")]
    [InlineData(null, "utf-8")]
    [InlineData("", "utf-8")]
    [InlineData("not-a-real-charset", "utf-8")]
    public void ResolveEncoding_ShouldMapCharsetTokensAndFallBackToUtf8(string? charSet, string expectedWebName)
    {
        Assert.Equal(expectedWebName, HtmlTitleExtractor.ResolveEncoding(charSet).WebName);
    }

    [Fact]
    public void ResolveEncoding_ShouldActuallyDecodeGbkBytes()
    {
        var encoding = HtmlTitleExtractor.ResolveEncoding("gbk");
        var bytes = new byte[] { 0xB4, 0xF2 };   // "打" in GBK

        var decoded = encoding.GetString(bytes);

        Assert.Equal("打", decoded);
        Assert.DoesNotContain('\uFFFD', decoded);
    }
}

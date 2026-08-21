#nullable enable
using N_m3u8DL_RE_GUI.Core.Capture;
using Xunit;

namespace N_m3u8DL_RE_GUI.Tests.Unit.Core.Capture;

public class BatchPasteHelperTests
{
    [Fact]
    public void LooksLikeBatchList_IsTrueForTwoOrMoreUrls()
    {
        var payload = "https://example.com/1.m3u8\nhttps://example.com/2.m3u8";
        Assert.True(BatchPasteHelper.LooksLikeBatchList(payload));
    }

    [Fact]
    public void LooksLikeBatchList_IsFalseForOne()
    {
        var payload = "https://example.com/1.m3u8";
        Assert.False(BatchPasteHelper.LooksLikeBatchList(payload));
    }

    [Fact]
    public void LooksLikeBatchList_IsFalseForACurlCommand()
    {
        var payload = "curl 'https://example.com/1.m3u8' \\\n  -H 'Referer: https://site.com/'";
        Assert.False(BatchPasteHelper.LooksLikeBatchList(payload));
    }

    [Fact]
    public void LooksLikeBatchList_IgnoresCommentLines()
    {
        var payload = "# Referer: https://site.com/\n# Just a comment\nhttps://example.com/1.m3u8\nhttps://example.com/2.m3u8";
        Assert.True(BatchPasteHelper.LooksLikeBatchList(payload));
    }

    [Fact]
    public void LooksLikeBatchList_IgnoresBlankLines()
    {
        var payload = "\n\nhttps://example.com/1.m3u8\n\n\nhttps://example.com/2.m3u8\n\n";
        Assert.True(BatchPasteHelper.LooksLikeBatchList(payload));
    }

    [Fact]
    public void LooksLikeBatchList_AcceptsTitleCommaUrlLines()
    {
        var payload = "Episode 1,https://example.com/1.m3u8\nEpisode 2,https://example.com/2.m3u8";
        Assert.True(BatchPasteHelper.LooksLikeBatchList(payload));
    }

    [Fact]
    public void LooksLikeBatchList_IsFalseForProse()
    {
        var payload = "This is a random paragraph of text with no URLs.";
        Assert.False(BatchPasteHelper.LooksLikeBatchList(payload));
    }
}

#nullable enable
using N_m3u8DL_RE_GUI.Core;
using Xunit;

namespace N_m3u8DL_RE_GUI.Tests;

public class BatchInputParserTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("# comment")]
    [InlineData("   # comment")]
    public void TryParse_ShouldReturnFalse_ForEmptyOrCommentLine(string? line)
    {
        var result = BatchInputParser.TryParse(line, out var entry);

        Assert.False(result);
        Assert.Null(entry);
    }

    [Fact]
    public void TryParse_WithPlainUrl_ShouldUseAutoTitleMode()
    {
        const string line = "https://example.com/video.m3u8";

        var result = BatchInputParser.TryParse(line, out var entry);

        Assert.True(result);
        Assert.NotNull(entry);
        Assert.Equal("https://example.com/video.m3u8", entry!.Url);
        Assert.Equal(string.Empty, entry.Title);
        Assert.False(entry.HasCustomTitle);
    }

    [Fact]
    public void TryParse_WithCustomTitleAndUrl_ShouldParseBoth()
    {
        const string line = "Episode 1,https://example.com/video.m3u8";

        var result = BatchInputParser.TryParse(line, out var entry);

        Assert.True(result);
        Assert.NotNull(entry);
        Assert.Equal("Episode 1", entry!.Title);
        Assert.Equal("https://example.com/video.m3u8", entry.Url);
        Assert.True(entry.HasCustomTitle);
    }

    [Fact]
    public void TryParse_WithCustomTitleAndUrlSeparatedByCommaSpace_ShouldParseBoth()
    {
        const string line = "Episode 2, https://example.com/video2.m3u8";

        var result = BatchInputParser.TryParse(line, out var entry);

        Assert.True(result);
        Assert.NotNull(entry);
        Assert.Equal("Episode 2", entry!.Title);
        Assert.Equal("https://example.com/video2.m3u8", entry.Url);
        Assert.True(entry.HasCustomTitle);
    }

    [Fact]
    public void TryParse_WithTitleContainingComma_ShouldKeepEntireTitle()
    {
        const string line = "Episode, Part 1, https://example.com/video3.m3u8";

        var result = BatchInputParser.TryParse(line, out var entry);

        Assert.True(result);
        Assert.NotNull(entry);
        Assert.Equal("Episode, Part 1", entry!.Title);
        Assert.Equal("https://example.com/video3.m3u8", entry.Url);
        Assert.True(entry.HasCustomTitle);
    }

    [Fact]
    public void TryParse_WithEmptyCustomTitle_ShouldPreserveCustomTitleMode()
    {
        const string line = ",https://example.com/video.m3u8";

        var result = BatchInputParser.TryParse(line, out var entry);

        Assert.True(result);
        Assert.NotNull(entry);
        Assert.Equal(string.Empty, entry!.Title);
        Assert.Equal("https://example.com/video.m3u8", entry.Url);
        Assert.True(entry.HasCustomTitle);
    }

    [Fact]
    public void TryParse_WithEmptyCustomTitleAndSpaceBeforeUrl_ShouldPreserveCustomTitleMode()
    {
        const string line = ", https://example.com/video.m3u8";

        var result = BatchInputParser.TryParse(line, out var entry);

        Assert.True(result);
        Assert.NotNull(entry);
        Assert.Equal(string.Empty, entry!.Title);
        Assert.Equal("https://example.com/video.m3u8", entry.Url);
        Assert.True(entry.HasCustomTitle);
    }

    [Fact]
    public void TryParse_WithUnsupportedFormat_ShouldReturnFalse()
    {
        const string line = "not a valid line";

        var result = BatchInputParser.TryParse(line, out var entry);

        Assert.False(result);
        Assert.Null(entry);
    }
}

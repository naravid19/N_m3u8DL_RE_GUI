#nullable enable
using System.Linq;
using N_m3u8DL_RE_GUI.Core.Abyss;
using Xunit;

namespace N_m3u8DL_RE_GUI.Tests.Unit.Abyss;

public class AbyssSourceChoiceTests
{
    private static AbyssSource Src(string label, long size, string? codec = "h264") =>
        new() { Label = label, Size = size, Codec = codec };

    [Fact]
    public void Ranked_PutsTheLargestFirst_SoAnUnattendedRunKeepsTakingTheBestQuality()
    {
        var ranked = AbyssSourceChoice.Ranked(new[] { Src("360p", 40), Src("1080p", 400), Src("720p", 200) });

        Assert.Equal(new[] { "1080p", "720p", "360p" }, ranked.Select(s => s.Label));
    }

    [Fact]
    public void Ranked_BreaksSizeTiesByLabel_SoTheListDoesNotReshuffleBetweenRuns()
    {
        var ranked = AbyssSourceChoice.Ranked(new[] { Src("b", 100), Src("A", 100), Src("c", 100) });

        Assert.Equal(new[] { "A", "b", "c" }, ranked.Select(s => s.Label));
    }

    [Fact]
    public void Ranked_OfNothing_IsEmpty()
    {
        Assert.Empty(AbyssSourceChoice.Ranked(System.Array.Empty<AbyssSource>()));
    }

    [Theory]
    [InlineData("1080p", 432537600L, "h264", "1080p — 412.5 MB · h264")]
    [InlineData("720p", 1048576L, null, "720p — 1.0 MB")]
    [InlineData("720p", 1048576L, "  ", "720p — 1.0 MB")]
    [InlineData(null, 1048576L, "vp9", "? — 1.0 MB · vp9")]
    public void Describe_ShowsLabelSizeAndCodec(string? label, long size, string? codec, string expected)
    {
        Assert.Equal(expected, AbyssSourceChoice.Describe(Src(label!, size, codec)));
    }
}

#nullable enable
using N_m3u8DL_RE_GUI.Core;
using Xunit;

namespace N_m3u8DL_RE_GUI.Tests.Unit.Core;

public class LegacyConfigCodecTests
{
    [Theory]
    [InlineData("plain", "plain")]
    [InlineData("", "")]
    [InlineData(null, "")]
    [InlineData("ads;sponsor", "ads%3Bsponsor")]
    [InlineData("a;b;c", "a%3Bb%3Bc")]
    [InlineData("100%", "100%25")]
    [InlineData("%3B", "%253B")]                       // a literal %3B must not decode to ';'
    [InlineData("mix%and;match", "mix%25and%3Bmatch")]
    [InlineData("$Title_$Id=$Res", "$Title_$Id=$Res")]  // '=' is safe: only the first one splits
    public void EscapeValue_ShouldEncodeOnlyTheSeparatorAndTheEscapeCharacter(string? raw, string expected)
    {
        Assert.Equal(expected, LegacyConfigCodec.EscapeValue(raw));
    }

    [Theory]
    [InlineData("ads;sponsor")]
    [InlineData("100% sure; really")]
    [InlineData("%3B")]
    [InlineData("%25%3B")]
    [InlineData("ตอนที่ 1;中文")]
    [InlineData("")]
    public void EscapeThenUnescape_ShouldRoundTrip(string raw)
    {
        Assert.Equal(raw, LegacyConfigCodec.UnescapeValue(LegacyConfigCodec.EscapeValue(raw)));
    }

    [Theory]
    [InlineData("no escapes here", "no escapes here")]
    [InlineData("", "")]
    [InlineData(null, "")]
    public void UnescapeValue_ShouldLeaveOldUnescapedValuesAlone(string? stored, string expected)
    {
        // Files written before this codec existed contain no % sequences, so they decode
        // to themselves. That is what keeps existing config.txt files loading.
        Assert.Equal(expected, LegacyConfigCodec.UnescapeValue(stored));
    }

    [Fact]
    public void UnescapeValue_ShouldBeCaseInsensitiveOnHexDigits()
    {
        Assert.Equal(";", LegacyConfigCodec.UnescapeValue("%3b"));
        Assert.Equal(";", LegacyConfigCodec.UnescapeValue("%3B"));
    }
}

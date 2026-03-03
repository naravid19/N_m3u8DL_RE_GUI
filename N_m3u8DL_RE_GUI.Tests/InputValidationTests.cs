#nullable enable
using N_m3u8DL_RE_GUI.Core;
using Xunit;

namespace N_m3u8DL_RE_GUI.Tests;

public class InputValidationTests
{
    [Theory]
    [InlineData("http://example.com/a.m3u8", true)]
    [InlineData("https://example.com/a.m3u8", true)]
    [InlineData("HTTP://example.com/a.m3u8", true)]
    [InlineData("C:\\videos\\a.m3u8", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsHttpUrl_ShouldMatchExpectedProtocolRules(string? input, bool expected)
    {
        Assert.Equal(expected, InputValidation.IsHttpUrl(input));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    [InlineData("http://127.0.0.1:8080")]
    [InlineData("HTTP://proxy.local:8888")]
    [InlineData("socks5://127.0.0.1:1080")]
    public void IsValidProxy_ShouldAcceptSupportedValues(string? proxy)
    {
        Assert.True(InputValidation.IsValidProxy(proxy));
    }

    [Theory]
    [InlineData("https://proxy.local:8080")]
    [InlineData("ftp://proxy.local:21")]
    [InlineData("127.0.0.1:7890")]
    public void IsValidProxy_ShouldRejectUnsupportedValues(string proxy)
    {
        Assert.False(InputValidation.IsValidProxy(proxy));
    }

    [Fact]
    public void IsLikelyValidInput_ShouldAcceptHttpAndHttps()
    {
        Assert.True(InputValidation.IsLikelyValidInput("http://example.com/a.m3u8"));
        Assert.True(InputValidation.IsLikelyValidInput("https://example.com/a.m3u8"));
    }

    [Fact]
    public void IsLikelyValidInput_ShouldAcceptExistingFileAndDirectory()
    {
        var tempDir = Directory.CreateTempSubdirectory("input-validation");
        var tempFile = Path.Combine(tempDir.FullName, "list.txt");
        File.WriteAllText(tempFile, "https://example.com");
        try
        {
            Assert.True(InputValidation.IsLikelyValidInput(tempFile));
            Assert.True(InputValidation.IsLikelyValidInput(tempDir.FullName));
        }
        finally
        {
            tempDir.Delete(recursive: true);
        }
    }

    [Fact]
    public void IsLikelyValidInput_ShouldRejectUnknownLocalPath()
    {
        Assert.False(InputValidation.IsLikelyValidInput(@"C:\this\path\should\not\exist\__test__"));
    }

    [Theory]
    [InlineData("https://example.com/video.m3u8", true)]
    [InlineData("http://example.com/manifest", true)]
    [InlineData("C:\\input\\list.m3u8", true)]
    [InlineData("C:\\input\\list.txt", true)]
    [InlineData("C:\\input\\list.json", true)]
    [InlineData("C:\\input\\list.mpd", true)]
    [InlineData("C:\\input\\list.srt", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsSupportedStartupInputArgument_ShouldMatchSupportedInputs(string? argument, bool expected)
    {
        Assert.Equal(expected, InputValidation.IsSupportedStartupInputArgument(argument));
    }

    [Fact]
    public void ExtractFirstUrl_ShouldReturnFirstHttpOrHttps()
    {
        const string text = "prefix https://example.com/a.m3u8 and http://example.org/b.mpd";
        Assert.Equal("https://example.com/a.m3u8", InputValidation.ExtractFirstUrl(text));
    }

    [Theory]
    [InlineData("")]
    [InlineData("no links here")]
    [InlineData("ftp://example.com/file")]
    [InlineData(null)]
    public void ExtractFirstUrl_ShouldReturnEmpty_WhenNoSupportedUrl(string? text)
    {
        Assert.Equal(string.Empty, InputValidation.ExtractFirstUrl(text));
    }
}

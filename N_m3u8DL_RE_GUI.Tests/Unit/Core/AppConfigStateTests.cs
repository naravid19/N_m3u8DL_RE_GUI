#nullable enable
using N_m3u8DL_RE_GUI.Services;
using Xunit;

namespace N_m3u8DL_RE_GUI.Tests.Unit.Core;

public class AppConfigStateTests
{
    [Fact]
    public void Set_And_Get_ShouldStoreAndRetrieveValues()
    {
        var state = new AppConfigState();
        state.Set("Theme", "Dark");
        state.Set("MaxThreads", "16");

        Assert.Equal("Dark", state.Get("Theme"));
        Assert.Equal("16", state.Get("MaxThreads"));
    }

    [Fact]
    public void Get_NonExistentKey_ShouldReturnEmptyString()
    {
        var state = new AppConfigState();
        Assert.Equal(string.Empty, state.Get("NonExistentKey"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Set_NullOrWhitespaceKey_ShouldBeIgnored(string? invalidKey)
    {
        var state = new AppConfigState();
        state.Set(invalidKey!, "SomeValue");
        Assert.Empty(state.Entries);
    }

    [Fact]
    public void Set_NullValue_ShouldStoreEmptyString()
    {
        var state = new AppConfigState();
        state.Set("KeyWithNull", null);
        Assert.Equal(string.Empty, state.Get("KeyWithNull"));
    }

    [Theory]
    [InlineData("1", false, true)]
    [InlineData("0", true, false)]
    [InlineData("true", false, true)]
    [InlineData("false", true, false)]
    [InlineData("TRUE", false, true)]
    [InlineData("FALSE", true, false)]
    [InlineData("invalid_bool", true, true)]
    [InlineData("invalid_bool", false, false)]
    [InlineData("", true, true)]
    public void GetBool_ShouldParseVariousFormats(string rawValue, bool defaultValue, bool expected)
    {
        var state = new AppConfigState();
        state.Set("BoolKey", rawValue);

        var result = state.GetBool("BoolKey", defaultValue);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("42", 42)]
    [InlineData("-10", -10)]
    [InlineData("0", 0)]
    [InlineData("not_an_int", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void GetInt_ShouldParseIntegersOrReturnNull(string? rawValue, int? expected)
    {
        var state = new AppConfigState();
        if (rawValue != null)
            state.Set("IntKey", rawValue);

        var result = state.GetInt("IntKey");
        Assert.Equal(expected, result);
    }

    [Fact]
    public void SetEncodedBase64_And_GetDecodedBase64_Roundtrip()
    {
        var state = new AppConfigState();
        const string plaintext = @"C:\Program Files\N_m3u8DL-RE\N_m3u8DL-RE.exe";

        state.SetEncodedBase64("ExePath", plaintext);
        var decoded = state.GetDecodedBase64("ExePath");

        Assert.Equal(plaintext, decoded);
    }

    [Fact]
    public void SetEncodedBase64_WithThaiAndChineseCharacters_Roundtrip()
    {
        var state = new AppConfigState();
        const string unicodeText = "ดาวน์โหลดวิดีโอ_视频下载_日本語";

        state.SetEncodedBase64("UnicodeKey", unicodeText);
        var decoded = state.GetDecodedBase64("UnicodeKey");

        Assert.Equal(unicodeText, decoded);
    }

    [Fact]
    public void GetDecodedBase64_WithInvalidBase64String_ShouldReturnEmptyString()
    {
        var state = new AppConfigState();
        state.Set("BadBase64", "this is not valid base64 !!!");

        var result = state.GetDecodedBase64("BadBase64");
        Assert.Equal(string.Empty, result);
    }

    [Fact]
    public void GetDecodedBase64_WithEmptyKey_ShouldReturnEmptyString()
    {
        var state = new AppConfigState();
        Assert.Equal(string.Empty, state.GetDecodedBase64("UnsetKey"));
    }
}

#nullable enable
using N_m3u8DL_RE_GUI.Core.Capture;
using Xunit;

namespace N_m3u8DL_RE_GUI.Tests.Unit.Core.Capture;

public class HeaderPolicyTests
{
    [Theory]
    [InlineData("Referer")]
    [InlineData("referer")]
    [InlineData("Origin")]
    [InlineData("User-Agent")]
    [InlineData("Cookie")]
    [InlineData("Authorization")]
    [InlineData("X-Custom-Token")]
    public void ShouldForward_KeepsHeadersThatAffectStreamAccess(string name)
    {
        Assert.True(HeaderPolicy.ShouldForward(name));
    }

    [Theory]
    [InlineData(":authority")]
    [InlineData(":method")]
    [InlineData(":path")]
    [InlineData(":scheme")]
    public void ShouldForward_DropsHttp2PseudoHeaders(string name)
    {
        // These appear verbatim in HAR captures and are illegal to set on a request.
        Assert.False(HeaderPolicy.ShouldForward(name));
    }

    [Theory]
    [InlineData("accept-encoding")]
    [InlineData("Accept-Encoding")]
    [InlineData("content-length")]
    [InlineData("host")]
    [InlineData("connection")]
    [InlineData("priority")]
    [InlineData("dnt")]
    [InlineData("upgrade-insecure-requests")]
    public void ShouldForward_DropsTransportAndNoiseHeaders(string name)
    {
        Assert.False(HeaderPolicy.ShouldForward(name));
    }

    [Theory]
    [InlineData("sec-fetch-dest")]
    [InlineData("sec-fetch-mode")]
    [InlineData("sec-ch-ua")]
    [InlineData("Sec-CH-UA-Platform")]
    public void ShouldForward_DropsTheEntireSecPrefix(string name)
    {
        Assert.False(HeaderPolicy.ShouldForward(name));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ShouldForward_RejectsEmptyNames(string? name)
    {
        Assert.False(HeaderPolicy.ShouldForward(name));
    }
}

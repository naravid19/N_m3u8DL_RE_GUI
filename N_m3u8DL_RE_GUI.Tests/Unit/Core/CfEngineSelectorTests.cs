#nullable enable
using N_m3u8DL_RE_GUI.Core;
using N_m3u8DL_RE_GUI.Core.Capture;
using Xunit;

namespace N_m3u8DL_RE_GUI.Tests.Unit.Core;

public class CfEngineSelectorTests
{
    [Fact]
    public void PinnedDirect_AlwaysUsesDirect_RegardlessOfKindOrKey()
    {
        Assert.False(CfEngineSelector.ShouldUseProxy("direct", CapturedStreamKind.Dash, hasKey: true));
        Assert.False(CfEngineSelector.ShouldUseProxy("direct", CapturedStreamKind.Hls, hasKey: false));
    }

    [Fact]
    public void PinnedProxy_AlwaysUsesProxy_RegardlessOfKindOrKey()
    {
        Assert.True(CfEngineSelector.ShouldUseProxy("proxy", CapturedStreamKind.Hls, hasKey: false));
        Assert.True(CfEngineSelector.ShouldUseProxy("proxy", CapturedStreamKind.Media, hasKey: false));
    }

    [Fact]
    public void Auto_HlsWithoutKey_UsesDirect()
    {
        Assert.False(CfEngineSelector.ShouldUseProxy("auto", CapturedStreamKind.Hls, hasKey: false));
    }

    [Fact]
    public void Auto_HlsWithKey_UsesProxy()
    {
        Assert.True(CfEngineSelector.ShouldUseProxy("auto", CapturedStreamKind.Hls, hasKey: true));
    }

    [Fact]
    public void Auto_Dash_UsesProxy_RegardlessOfKey()
    {
        Assert.True(CfEngineSelector.ShouldUseProxy("auto", CapturedStreamKind.Dash, hasKey: false));
        Assert.True(CfEngineSelector.ShouldUseProxy("auto", CapturedStreamKind.Dash, hasKey: true));
    }

    [Fact]
    public void Auto_UnknownKind_UsesProxy()
    {
        // .ism/.isml (MSS) and anything else unclassified fall through to the
        // general-purpose engine rather than risk the direct HLS-only path.
        Assert.True(CfEngineSelector.ShouldUseProxy("auto", CapturedStreamKind.Unknown, hasKey: false));
    }

    [Fact]
    public void Auto_Media_UsesProxy()
    {
        Assert.True(CfEngineSelector.ShouldUseProxy("auto", CapturedStreamKind.Media, hasKey: false));
    }

    [Fact]
    public void NullOrUnrecognisedTag_BehavesLikeAuto()
    {
        Assert.False(CfEngineSelector.ShouldUseProxy(null, CapturedStreamKind.Hls, hasKey: false));
        Assert.True(CfEngineSelector.ShouldUseProxy("bogus", CapturedStreamKind.Dash, hasKey: false));
    }
}

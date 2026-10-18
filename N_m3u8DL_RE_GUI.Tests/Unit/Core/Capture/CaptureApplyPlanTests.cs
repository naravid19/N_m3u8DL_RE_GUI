#nullable enable
using System.Collections.Generic;
using N_m3u8DL_RE_GUI.Core.Capture;
using Xunit;

namespace N_m3u8DL_RE_GUI.Tests.Unit.Core.Capture;

public class CaptureApplyPlanTests
{
    [Fact]
    public void From_NullOrEmptyUrl_ReturnsNull()
    {
        Assert.Null(CaptureApplyPlan.From(null));
        Assert.Null(CaptureApplyPlan.From(new CapturedRequest("", new List<CapturedHeader>(), CapturedStreamKind.Unknown)));
    }

    [Fact]
    public void From_MapsBasicFieldsAndHeaderLines()
    {
        var headers = new List<CapturedHeader>
        {
            new("Referer", "https://example.com/player"),
            new("User-Agent", "Mozilla/5.0"),
            new("Cookie", "session=123")
        };
        var directives = new Dictionary<string, string>
        {
            ["select-video"] = "res=\"1080*\"",
            ["save-name"] = "My Video"
        };
        var captured = new CapturedRequest("https://cdn.example.com/master.m3u8", headers, CapturedStreamKind.Hls, directives);

        var plan = CaptureApplyPlan.From(captured);

        Assert.NotNull(plan);
        Assert.Equal("https://cdn.example.com/master.m3u8", plan!.Url);
        Assert.Equal("Referer: https://example.com/player\nUser-Agent: Mozilla/5.0\nCookie: session=123", plan.HeaderLines);
        Assert.Equal("res=\"1080*\"", plan.SelectVideo);
        Assert.Equal("My Video", plan.SaveName);
        Assert.Equal("https://example.com/player", plan.CfReferer);
        Assert.Equal("session=123", plan.CfCookie);
        Assert.Equal(3, plan.HeaderCount);
        Assert.Equal(CapturedStreamKind.Hls, plan.Kind);
    }

    [Fact]
    public void From_PageUrlDirective_OverridesHeaderRefererForCfReferer()
    {
        var headers = new List<CapturedHeader> { new("Referer", "https://player.cdn.com/") };
        var directives = new Dictionary<string, string> { ["page-url"] = "https://site.example/video/1" };
        var captured = new CapturedRequest("https://cdn.com/a.m3u8", headers, CapturedStreamKind.Hls, directives);

        var plan = CaptureApplyPlan.From(captured);

        Assert.Equal("https://site.example/video/1", plan!.CfReferer);
    }

    [Fact]
    public void From_ReapplyingEmptyCapture_ClearsOwnedFields()
    {
        var captured = new CapturedRequest("https://other.com/b.mp4", new List<CapturedHeader>(), CapturedStreamKind.Media);

        var plan = CaptureApplyPlan.From(captured);

        Assert.NotNull(plan);
        Assert.Equal(string.Empty, plan!.HeaderLines);
        Assert.Equal(string.Empty, plan.SelectVideo);
        Assert.Equal(string.Empty, plan.SaveName);
        Assert.Equal(string.Empty, plan.CfReferer);
        Assert.Equal(string.Empty, plan.CfCookie);
        Assert.Null(plan.CfImpersonate);
        Assert.False(plan.EnableCfBypass);
        Assert.Equal(0, plan.HeaderCount);
    }

    [Fact]
    public void From_ImpersonateDirective_MatchesKnownProfilesOrReturnsNull()
    {
        var capKnown = new CapturedRequest("https://a.com/1.m3u8", new List<CapturedHeader>(), CapturedStreamKind.Hls,
            new Dictionary<string, string> { ["impersonate"] = "Chrome131" });
        Assert.Equal("chrome131", CaptureApplyPlan.From(capKnown)!.CfImpersonate);

        var capUnknown = new CapturedRequest("https://a.com/1.m3u8", new List<CapturedHeader>(), CapturedStreamKind.Hls,
            new Dictionary<string, string> { ["impersonate"] = "firefox999" });
        Assert.Null(CaptureApplyPlan.From(capUnknown)!.CfImpersonate);
    }

    [Fact]
    public void From_CfDirective_MapsToBoolean_AndAbsentMeansOff()
    {
        var cap1 = new CapturedRequest("https://a.com/1.m3u8", new List<CapturedHeader>(), CapturedStreamKind.Hls,
            new Dictionary<string, string> { ["cf"] = "1" });
        Assert.True(CaptureApplyPlan.From(cap1)!.EnableCfBypass);

        var cap0 = new CapturedRequest("https://a.com/1.m3u8", new List<CapturedHeader>(), CapturedStreamKind.Hls,
            new Dictionary<string, string> { ["cf"] = "0" });
        Assert.False(CaptureApplyPlan.From(cap0)!.EnableCfBypass);

        var capNone = new CapturedRequest("https://a.com/1.m3u8", new List<CapturedHeader>(), CapturedStreamKind.Hls);
        // A capture that says nothing about Cloudflare must not inherit the previous
        // capture's bypass: the GUI starts the download straight away.
        Assert.False(CaptureApplyPlan.From(capNone)!.EnableCfBypass);
    }

    [Fact]
    public void From_KindDirective_OverridesClassifiedKind()
    {
        var cap = new CapturedRequest("https://a.com/stream-without-extension", new List<CapturedHeader>(), CapturedStreamKind.Unknown,
            new Dictionary<string, string> { ["kind"] = "dash" });

        Assert.Equal(CapturedStreamKind.Dash, CaptureApplyPlan.From(cap)!.Kind);
    }
}

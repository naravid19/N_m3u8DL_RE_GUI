#nullable enable
using System.Collections.Generic;
using N_m3u8DL_RE_GUI.Core.Capture;
using Xunit;

namespace N_m3u8DL_RE_GUI.Tests.Unit.Core.Capture;

public class CaptureDirectivesTests
{
    [Fact]
    public void Parse_ReadsASingleDirective()
    {
        var payload = "curl 'https://example.com/master.m3u8'\n# nre-select-video: res=\"1080*\"";
        var directives = CaptureDirectives.Parse(payload);

        Assert.Single(directives);
        Assert.Equal("res=\"1080*\"", directives["select-video"]);
    }

    [Fact]
    public void Parse_ReadsSeveralDirectives()
    {
        var payload = "curl 'https://example.com/master.m3u8'\n# nre-select-video: res=\"1080*\"\n# nre-title: My Video";
        var directives = CaptureDirectives.Parse(payload);

        Assert.Equal(2, directives.Count);
        Assert.Equal("res=\"1080*\"", directives["select-video"]);
        Assert.Equal("My Video", directives["title"]);
    }

    [Fact]
    public void Parse_IgnoresOrdinaryShellComments()
    {
        var payload = "curl 'https://example.com/master.m3u8'\n# just a note\n# nre not a directive";
        var directives = CaptureDirectives.Parse(payload);

        Assert.Empty(directives);
    }

    [Fact]
    public void Parse_IgnoresALineWithNoColon()
    {
        var payload = "# nre-select-video 1080p";
        var directives = CaptureDirectives.Parse(payload);

        Assert.Empty(directives);
    }

    [Fact]
    public void Parse_IsCaseInsensitiveOnTheKey()
    {
        var payload = "# nre-SELECT-VIDEO: 1080p";
        var directives = CaptureDirectives.Parse(payload);

        Assert.True(directives.ContainsKey("select-video"));
        Assert.Equal("1080p", directives["select-video"]);
    }

    [Fact]
    public void Parse_TrimsSurroundingWhitespace()
    {
        var payload = "  # nre-select-video  :   res=\"1080*\"   ";
        var directives = CaptureDirectives.Parse(payload);

        Assert.Equal("res=\"1080*\"", directives["select-video"]);
    }

    [Fact]
    public void Parse_KeepsAValueContainingAColon()
    {
        var payload = "# nre-select-video: res=\"1080*\" x:y";
        var directives = CaptureDirectives.Parse(payload);

        Assert.Equal("res=\"1080*\" x:y", directives["select-video"]);
    }

    [Fact]
    public void Parse_ReturnsEmptyForNullOrEmptyInput()
    {
        Assert.Empty(CaptureDirectives.Parse(null));
        Assert.Empty(CaptureDirectives.Parse(""));
        Assert.Empty(CaptureDirectives.Parse("   "));
    }

    [Fact]
    public void Parse_LastWinsOnADuplicateKey()
    {
        var payload = "# nre-select-video: 720p\n# nre-select-video: 1080p";
        var directives = CaptureDirectives.Parse(payload);

        Assert.Single(directives);
        Assert.Equal("1080p", directives["select-video"]);
    }
}

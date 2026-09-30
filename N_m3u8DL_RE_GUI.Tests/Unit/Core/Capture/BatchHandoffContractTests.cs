#nullable enable
using System.Linq;
using N_m3u8DL_RE_GUI.Core;
using N_m3u8DL_RE_GUI.Core.Capture;
using Xunit;

namespace N_m3u8DL_RE_GUI.Tests.Unit.Core.Capture;

/// <summary>
/// Pins the format the browser extension writes against the readers on this side.
///
/// The payload below is copied verbatim from extension/test/curl.test.js. If the
/// extension changes what toBatchList emits, that test and this one must be
/// updated together — which is the point. Both halves passing their own tests
/// while disagreeing about the format is the failure this prevents.
/// </summary>
public class BatchHandoffContractTests
{
    private const string ExtensionPayload =
        "# nre-headers: Referer: https://site.example.com/\\nUser-Agent: Mozilla/5.0\n" +
        "Episode 1,https://cdn.example.com/ep1/master.m3u8\n" +
        "Episode 2,https://cdn.example.com/ep2/master.m3u8\n" +
        "https://cdn.example.com/ep3/master.m3u8";

    [Fact]
    public void ExtensionPayload_IsRecognisedAsABatchList()
    {
        Assert.True(BatchPasteHelper.LooksLikeBatchList(ExtensionPayload));
    }

    [Fact]
    public void ExtensionPayload_IsNotMistakenForACurlCommand()
    {
        Assert.False(CurlCommandParser.LooksLikeCurl(ExtensionPayload));
    }

    [Fact]
    public void EveryUrlSurvivesTheParser()
    {
        var entries = ExtensionPayload
            .Split('\n')
            .Select(line => BatchInputParser.TryParse(line, out var entry) ? entry : null)
            .Where(entry => entry is not null)
            .ToList();

        Assert.Equal(3, entries.Count);
        Assert.Equal("https://cdn.example.com/ep1/master.m3u8", entries[0]!.Url);
        Assert.Equal("https://cdn.example.com/ep3/master.m3u8", entries[2]!.Url);
    }

    [Fact]
    public void TitlesSurviveOnTheLinesThatCarryThem()
    {
        BatchInputParser.TryParse("Episode 1,https://cdn.example.com/ep1/master.m3u8", out var entry);

        Assert.True(entry!.HasCustomTitle);
        Assert.Equal("Episode 1", entry.Title);
    }

    [Fact]
    public void TheHeadersDirectiveSurvivesAndExpands()
    {
        // This is the regression that shipped: headers were written as a bare
        // '#' comment, which BatchInputParser skips by design, so every batch
        // download ran with no headers at all.
        var directives = CaptureDirectives.Parse(ExtensionPayload);

        Assert.True(directives.ContainsKey("headers"));
        Assert.Contains("Referer: https://site.example.com/", directives["headers"]);
        Assert.Contains("User-Agent: Mozilla/5.0", directives["headers"]);
        Assert.Contains('\n', directives["headers"]);
    }

    [Fact]
    public void TheDirectiveLineIsNotParsedAsAStreamUrl()
    {
        Assert.False(BatchInputParser.TryParse(
            "# nre-headers: Referer: https://site.example.com/", out _));
    }

    [Fact]
    public void ExpandedHeadersReachArgsBuilderAsSeparateFlags()
    {
        var directives = CaptureDirectives.Parse(ExtensionPayload);
        var args = ArgsBuilder.Build(new DownloadOptions
        {
            Input = "https://cdn.example.com/ep1/master.m3u8",
            Headers = directives["headers"]
        });

        Assert.Contains("Referer: https://site.example.com/", args);
        Assert.Contains("User-Agent: Mozilla/5.0", args);
    }

    [Fact]
    public void ASingleUrlPayloadStaysOffTheBatchPath()
    {
        Assert.False(BatchPasteHelper.LooksLikeBatchList("https://cdn.example.com/only.m3u8"));
    }
}

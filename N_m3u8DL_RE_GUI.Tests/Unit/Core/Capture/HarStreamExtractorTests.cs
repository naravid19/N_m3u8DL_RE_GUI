#nullable enable
using System;
using System.IO;
using System.Linq;
using System.Text;
using N_m3u8DL_RE_GUI.Core.Capture;
using Xunit;

namespace N_m3u8DL_RE_GUI.Tests.Unit.Core.Capture;

public class HarStreamExtractorTests
{
    private static string Fixture(string name) =>
        Path.Combine(AppContext.BaseDirectory, "Fixtures", "Har", name);

    [Fact]
    public void Extract_HlsCapture_ReturnsManifestsAndNeverSegments()
    {
        var results = HarStreamExtractor.ExtractFromFile(Fixture("hls-with-segments.har"));

        Assert.Equal(2, results.Count);
        Assert.All(results, r => Assert.Equal(CapturedStreamKind.Hls, r.Kind));
        Assert.DoesNotContain(results, r => r.Url.Contains(".ts", StringComparison.Ordinal));
    }

    [Fact]
    public void Extract_HlsCapture_RanksTheMasterFirst()
    {
        // The master is requested before its variants, so first-seen order is correct.
        var results = HarStreamExtractor.ExtractFromFile(Fixture("hls-with-segments.har"));

        Assert.Equal("https://cdn.example.com/hls/master.m3u8", results[0].Url);
    }

    [Fact]
    public void Extract_AppliesHeaderPolicyToCapturedHeaders()
    {
        var master = HarStreamExtractor.ExtractFromFile(Fixture("hls-with-segments.har"))[0];

        Assert.Contains(master.Headers, h => h.Name == "Referer");
        Assert.Contains(master.Headers, h => h.Name == "User-Agent");
        Assert.DoesNotContain(master.Headers, h => h.Name.StartsWith(':'));
        Assert.DoesNotContain(master.Headers, h => h.Name.StartsWith("sec-", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(master.Headers, h => h.Name.Equals("accept-encoding", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Extract_RangeRequestsForOneFile_CollapseToASingleEntry()
    {
        var results = HarStreamExtractor.ExtractFromFile(Fixture("progressive-mp4-ranges.har"));

        Assert.Single(results);
        Assert.Equal("https://cdn.example.com/video/movie.mp4", results[0].Url);
        Assert.Equal(CapturedStreamKind.Media, results[0].Kind);
    }

    [Fact]
    public void Extract_DedupeKeepsTheFirstOccurrencesHeaders()
    {
        var results = HarStreamExtractor.ExtractFromFile(Fixture("progressive-mp4-ranges.har"));

        Assert.Contains(results[0].Headers, h => h.Name == "Referer");
    }

    [Fact]
    public void Extract_FailedResponses_AreNotOffered()
    {
        var results = HarStreamExtractor.ExtractFromFile(Fixture("progressive-mp4-ranges.har"));

        Assert.DoesNotContain(results, r => r.Url.Contains("missing.mp4", StringComparison.Ordinal));
    }

    [Fact]
    public void Extract_CaptureWithNoMedia_ReturnsEmptyRatherThanThrowing()
    {
        Assert.Empty(HarStreamExtractor.ExtractFromFile(Fixture("no-streams.har")));
    }

    [Fact]
    public void Extract_MalformedJson_ThrowsInvalidDataWithAReadableMessage()
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("{ not json"));

        var exception = Assert.Throws<InvalidDataException>(() => HarStreamExtractor.Extract(stream));
        Assert.Contains("HAR", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Extract_JsonThatIsNotAHar_ThrowsInvalidData()
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("""{"hello":"world"}"""));

        Assert.Throws<InvalidDataException>(() => HarStreamExtractor.Extract(stream));
    }

    [Fact]
    public void Extract_EntryMissingResponse_IsSkippedNotFatal()
    {
        const string har = """
            { "log": { "entries": [
              { "request": { "url": "https://cdn.example.com/a.m3u8", "headers": [] } },
              { "request": { "url": "https://cdn.example.com/b.m3u8", "headers": [] },
                "response": { "status": 200, "content": { "mimeType": "application/vnd.apple.mpegurl" } } }
            ] } }
            """;
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(har));

        var results = HarStreamExtractor.Extract(stream);

        Assert.Single(results);
        Assert.Equal("https://cdn.example.com/b.m3u8", results[0].Url);
    }

    [Fact]
    public void Extract_ClassifiesByMimeTypeWhenTheUrlHasNoExtension()
    {
        const string har = """
            { "log": { "entries": [
              { "request": { "url": "https://cdn.example.com/manifest?id=42", "headers": [] },
                "response": { "status": 200, "content": { "mimeType": "application/dash+xml" } } }
            ] } }
            """;
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(har));

        var results = HarStreamExtractor.Extract(stream);

        Assert.Single(results);
        Assert.Equal(CapturedStreamKind.Dash, results[0].Kind);
    }

    [Fact]
    public void ExtractFromFile_OverTheSizeCap_ThrowsBeforeParsing()
    {
        var path = Path.Combine(Path.GetTempPath(), $"huge_{Guid.NewGuid():N}.har");
        try
        {
            // Create a sparse file past the cap without writing 256 MB.
            using (var fs = new FileStream(path, FileMode.CreateNew))
                fs.SetLength(HarStreamExtractor.MaxFileBytes + 1);

            Assert.Throws<InvalidDataException>(() => HarStreamExtractor.ExtractFromFile(path));
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }
}

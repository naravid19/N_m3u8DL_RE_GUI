#nullable enable
using N_m3u8DL_RE_GUI.Core;
using N_m3u8DL_RE_GUI.Tests.Fixtures;
using Xunit;

namespace N_m3u8DL_RE_GUI.Tests.Unit.Core;

public class CfCommandBuilderTests
{
    private static CfCommandOptions Sample(string url = "https://example.com/a.m3u8") => new(
        PythonExe: "python",
        ScriptPath: @"C:\App\m3u8_cf_bypass.py",
        Url: url,
        OutputName: "video.mp4",
        WorkDir: @"C:\Save",
        SegDir: @"C:\App\cf_segments",
        Referer: "https://example.com/",
        Cookie: "",
        Impersonate: "chrome",
        KeepSegments: false);

    [Fact]
    public void BuildCommand_ShouldQuoteEveryPathArgument()
    {
        var cmd = CfCommandBuilder.BuildCommand(Sample());

        Assert.Contains("\"python\"", cmd);
        Assert.Contains("\"C:\\App\\m3u8_cf_bypass.py\"", cmd);
        Assert.Contains("-o \"video.mp4\"", cmd);
        Assert.Contains("--work-dir \"C:\\Save\"", cmd);
        Assert.Contains("--impersonate \"chrome\"", cmd);
    }

    [Fact]
    public void BuildCommand_ShouldOmitCookieWhenEmpty()
    {
        Assert.DoesNotContain("--cookie", CfCommandBuilder.BuildCommand(Sample()));
    }

    [Fact]
    public void BuildCommand_ShouldIncludeCookieWhenPresent()
    {
        var options = Sample() with { Cookie = "cf_clearance=abc" };

        Assert.Contains("--cookie \"cf_clearance=abc\"", CfCommandBuilder.BuildCommand(options));
    }

    [Fact]
    public void BuildCommand_ShouldAppendKeepSegsOnlyWhenRequested()
    {
        Assert.DoesNotContain("--keep-segs", CfCommandBuilder.BuildCommand(Sample()));
        Assert.Contains("--keep-segs", CfCommandBuilder.BuildCommand(Sample() with { KeepSegments = true }));
    }

    [Fact]
    public void BuildCommand_ShouldEscapeEmbeddedDoubleQuotes()
    {
        var options = Sample() with { OutputName = "my \"best\" clip.mp4" };

        Assert.Contains("-o \"my \\\"best\\\" clip.mp4\"", CfCommandBuilder.BuildCommand(options));
    }

    [Fact]
    public void BuildArguments_ValuesEndingInABackslash_ShouldEachReachPythonAsOneArgument()
    {
        // A drive-root save folder or a captured cookie ending in "\" must not escape its
        // own closing quote and swallow every flag that follows it.
        var options = Sample() with { WorkDir = @"D:\", Cookie = @"sid=abc\", Threads = 8, Serve = true };

        var parsed = Win32CommandLine.Parse("python " + CfCommandBuilder.BuildArguments(options));

        Assert.Equal(new[]
        {
            "python", @"C:\App\m3u8_cf_bypass.py", "https://example.com/a.m3u8",
            "--referer", "https://example.com/",
            "-o", "video.mp4",
            "--work-dir", @"D:\",
            "--seg-dir", @"C:\App\cf_segments",
            "--impersonate", "chrome",
            "--cookie", @"sid=abc\",
            "--threads", "8",
            "--serve",
        }, parsed);
    }

    [Theory]
    [InlineData("https://custom.example/", "https://example.com/a.m3u8", "https://custom.example/")]
    [InlineData("", "https://example.com/path/a.m3u8", "https://example.com/")]
    [InlineData(null, "https://example.com:8443/a.m3u8", "https://example.com:8443/")]
    public void DeriveReferer_ResolvesExpectedOrigin(string? explicitReferer, string url, string expected)
    {
        Assert.Equal(expected, CfCommandBuilder.DeriveReferer(explicitReferer, url));
    }

    [Fact]
    public void ComputeJobId_IsDeterministicAndEightHexChars()
    {
        var id1 = CfCommandBuilder.ComputeJobId("https://surrit.com/test/playlist.m3u8", "video.mp4");
        var id2 = CfCommandBuilder.ComputeJobId("https://surrit.com/test/playlist.m3u8", "video.mp4");
        var id3 = CfCommandBuilder.ComputeJobId("https://surrit.com/other/playlist.m3u8", "video.mp4");

        Assert.Equal(id1, id2);
        Assert.NotEqual(id1, id3);
        Assert.Equal(8, id1.Length);
        Assert.Matches("^[0-9a-f]{8}$", id1);
    }
}

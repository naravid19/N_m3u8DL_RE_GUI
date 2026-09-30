#nullable enable
using N_m3u8DL_RE_GUI.Core;
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
    public void BuildBatchScript_ShouldDoublePercentSigns()
    {
        // THE BUG: a percent-encoded URL is eaten by cmd.exe argument expansion.
        var cmd = CfCommandBuilder.BuildCommand(Sample("https://example.com/a%20b.m3u8"));

        var bat = CfCommandBuilder.BuildBatchScript(cmd);

        Assert.Contains("a%%20b.m3u8", bat);
        Assert.DoesNotContain("a%20b.m3u8", bat.Replace("%%", "\u0000"));
    }

    [Fact]
    public void BuildBatchScript_ShouldEmitUtf8Header()
    {
        var bat = CfCommandBuilder.BuildBatchScript("echo hi");

        Assert.StartsWith("@echo off", bat);
        Assert.Contains("chcp 65001 >nul", bat);
        Assert.Contains("set PYTHONUTF8=1", bat);
    }

    [Theory]
    [InlineData("https://custom.example/", "https://example.com/a.m3u8", "https://custom.example/")]
    [InlineData("", "https://example.com/path/a.m3u8", "https://example.com/")]
    [InlineData(null, "https://example.com:8443/a.m3u8", "https://example.com:8443/")]
    [InlineData("", "not a url", "")]
    [InlineData("", null, "")]
    public void DeriveReferer_ShouldPreferExplicitThenFallBackToTheUrlAuthority(
        string? explicitReferer, string? inputUrl, string expected)
    {
        Assert.Equal(expected, CfCommandBuilder.DeriveReferer(explicitReferer, inputUrl));
    }
}

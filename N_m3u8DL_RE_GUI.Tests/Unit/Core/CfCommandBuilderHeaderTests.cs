#nullable enable
using System.Collections.Generic;
using N_m3u8DL_RE_GUI.Core;
using N_m3u8DL_RE_GUI.Core.Capture;
using Xunit;

namespace N_m3u8DL_RE_GUI.Tests.Unit.Core;

public class CfCommandBuilderHeaderTests
{
    private static CfCommandOptions BaseOptions() => new(
        PythonExe: "python",
        ScriptPath: @"C:\App\m3u8_cf_bypass.py",
        Url: "https://example.com/stream.m3u8",
        OutputName: "video.mp4",
        WorkDir: @"C:\Save",
        SegDir: @"C:\App\cf_segments\job12345",
        Referer: "https://example.com/",
        Cookie: "",
        Impersonate: "chrome131",
        KeepSegments: false);

    [Fact]
    public void BuildCommand_EmitsUserAgentWhenPresent()
    {
        var optionsWithUa = BaseOptions() with { UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64)" };
        var cmd = CfCommandBuilder.BuildCommand(optionsWithUa);

        Assert.Contains("--user-agent \"Mozilla/5.0 (Windows NT 10.0; Win64; x64)\"", cmd);

        var optionsWithoutUa = BaseOptions() with { UserAgent = "" };
        Assert.DoesNotContain("--user-agent", CfCommandBuilder.BuildCommand(optionsWithoutUa));
    }

    [Fact]
    public void BuildCommand_EmitsExtraHeadersWithHFlags()
    {
        var headers = new List<CapturedHeader>
        {
            new("Authorization", "Bearer secret123"),
            new("X-Custom-Token", "tok_abc")
        };
        var options = BaseOptions() with { ExtraHeaders = headers };
        var cmd = CfCommandBuilder.BuildCommand(options);

        Assert.Contains("-H \"Authorization: Bearer secret123\"", cmd);
        Assert.Contains("-H \"X-Custom-Token: tok_abc\"", cmd);
    }

    [Fact]
    public void BuildCommand_EmitsThreadsAndServeFlags()
    {
        var options = BaseOptions() with { Threads = 8, Serve = true };
        var cmd = CfCommandBuilder.BuildCommand(options);

        Assert.Contains("--threads 8", cmd);
        Assert.Contains("--serve", cmd);
    }

    [Fact]
    public void BuildMaskedCommand_MasksCookieToPreventCredentialLeak()
    {
        var options = BaseOptions() with { Cookie = "cf_clearance=super_secret_cookie_value_12345; session=abc" };
        var maskedCmd = CfCommandBuilder.BuildMaskedCommand(options);
        var realCmd = CfCommandBuilder.BuildCommand(options);

        Assert.Contains("...(masked,", maskedCmd);
        Assert.DoesNotContain("super_secret_cookie_value_12345", maskedCmd);

        Assert.Contains("super_secret_cookie_value_12345", realCmd);
    }

    [Fact]
    public void BuildMaskedCommand_DoesNotClaimCfClearanceWhenCookieHasNone()
    {
        // N10: the mask must not fabricate a "cf_clearance=" label for a
        // cookie that never contained one.
        var options = BaseOptions() with { Cookie = "session=abc; other=def" };
        var maskedCmd = CfCommandBuilder.BuildMaskedCommand(options);

        Assert.DoesNotContain("cf_clearance", maskedCmd);
        Assert.DoesNotContain("session=abc", maskedCmd);
    }

    [Fact]
    public void BuildMaskedCommand_MasksExtraHeaderValuesTooLikeAuthorization()
    {
        var options = BaseOptions() with
        {
            Cookie = "",
            ExtraHeaders = new[] { new CapturedHeader("Authorization", "Bearer super-secret-token") }
        };
        var maskedCmd = CfCommandBuilder.BuildMaskedCommand(options);
        var realCmd = CfCommandBuilder.BuildCommand(options);

        Assert.Contains("-H \"Authorization: ...(masked,", maskedCmd);
        Assert.DoesNotContain("super-secret-token", maskedCmd);
        Assert.Contains("super-secret-token", realCmd);
    }

    [Fact]
    public void BuildCommand_StripsControlCharactersFromValues()
    {
        var options = BaseOptions() with { Cookie = "foo\r\nbar\t123" };
        var cmd = CfCommandBuilder.BuildCommand(options);

        Assert.DoesNotContain("\r", cmd);
        Assert.DoesNotContain("\n", cmd);
        Assert.DoesNotContain("\t", cmd);
        Assert.Contains("foobar123", cmd);
    }
}

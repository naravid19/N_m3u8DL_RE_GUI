#nullable enable
using System.Text;
using N_m3u8DL_RE_GUI.Core;
using N_m3u8DL_RE_GUI.Tests.Fixtures;
using Xunit;

namespace N_m3u8DL_RE_GUI.Tests.Unit.Core;

/// <summary>
/// Verifies that <see cref="StringBuilderExtensions"/> produces argument strings that
/// Windows itself parses back into the original value.
///
/// The escaper has two code paths (a fast path for values without <c>\</c> or <c>"</c>,
/// and a CommandLineToArgvW-compatible slow path). These tests round-trip every value
/// through the real Win32 parser so both paths are proven equivalent to the OS contract
/// rather than to a hand-written expectation string.
/// </summary>
public class ArgsBuilderQuotingTests
{
    [Theory]
    // Fast path (no backslash, no quote)
    [InlineData("simple")]
    [InlineData("has space")]
    [InlineData("multiple   inner   spaces")]
    [InlineData("--flag=value")]
    [InlineData("percent%20encoded")]
    [InlineData("ภาษาไทย 中文 日本語")]
    [InlineData("tab\tseparated")]
    // Slow path (backslashes)
    [InlineData(@"C:\Downloads\video.mp4")]
    [InlineData(@"C:\path with space\video.mp4")]
    [InlineData(@"C:\")]
    [InlineData(@"\\server\share\file.mkv")]
    [InlineData(@"trailing\")]
    [InlineData(@"double\\backslash")]
    [InlineData(@"ends with two\\")]
    // Slow path (quotes)
    [InlineData("he said \"hi\"")]
    [InlineData("\"leading quote")]
    [InlineData("trailing quote\"")]
    [InlineData("\"")]
    // Slow path (both)
    [InlineData(@"a\""b")]
    [InlineData(@"C:\dir\""quoted\""\file.ts")]
    [InlineData(@"\\\""")]
    public void AppendQuoted_ShouldRoundTripThroughWin32Parser(string value)
    {
        var rendered = new StringBuilder().AppendQuoted(value).ToString();

        var parsed = Win32CommandLine.Parse("prog" + rendered);

        Assert.Equal(new[] { "prog", value }, parsed);
    }

    [Theory]
    [InlineData("plain")]
    [InlineData(@"C:\Downloads")]
    [InlineData("with \"quotes\" inside")]
    public void AppendIfNotEmpty_ShouldRoundTripFlagAndValue(string value)
    {
        var rendered = new StringBuilder().AppendIfNotEmpty("--save-dir", value).ToString();

        var parsed = Win32CommandLine.Parse("prog" + rendered);

        Assert.Equal(new[] { "prog", "--save-dir", value }, parsed);
    }

    [Fact]
    public void AppendQuoted_FastPathAndSlowPath_ShouldAgreeOnEscapeFreeValues()
    {
        // Values with neither '\' nor '"' must render identically to a plain quote wrap.
        foreach (var value in new[] { "abc", "a b c", "--x=1", "ไทย" })
        {
            var rendered = new StringBuilder().AppendQuoted(value).ToString();
            Assert.Equal($" \"{value}\"", rendered);
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t")]
    public void AppendQuoted_WithNullOrWhitespace_ShouldAppendNothing(string? value)
    {
        var rendered = new StringBuilder("seed").AppendQuoted(value).ToString();

        Assert.Equal("seed", rendered);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void AppendIfNotEmpty_WithNullOrWhitespace_ShouldAppendNeitherFlagNorValue(string? value)
    {
        var rendered = new StringBuilder().AppendIfNotEmpty("--save-dir", value).ToString();

        Assert.Equal(string.Empty, rendered);
    }

    [Fact]
    public void AppendIfTrue_ShouldAppendFlagOnlyWhenConditionHolds()
    {
        Assert.Equal(" --auto-select", new StringBuilder().AppendIfTrue("--auto-select", true).ToString());
        Assert.Equal(string.Empty, new StringBuilder().AppendIfTrue("--auto-select", false).ToString());
    }

    [Fact]
    public void Build_WithPathsContainingSpaces_ShouldKeepEachValueAsOneArgument()
    {
        var options = new DownloadOptions
        {
            Input = @"C:\In Box\playlist.m3u8",
            SaveDir = @"C:\Save Dir",
            SaveName = "My Video \"Special\" Cut",
            TmpDir = @"C:\Temp Dir\",
            FFmpegBinaryPath = @"C:\Program Files\ffmpeg\bin\ffmpeg.exe"
        };

        var parsed = Win32CommandLine.Parse("prog " + ArgsBuilder.Build(options));

        Assert.Contains(@"C:\In Box\playlist.m3u8", parsed);
        Assert.Contains(@"C:\Save Dir", parsed);
        Assert.Contains("My Video \"Special\" Cut", parsed);
        Assert.Contains(@"C:\Temp Dir\", parsed);
        Assert.Contains(@"C:\Program Files\ffmpeg\bin\ffmpeg.exe", parsed);
    }

    [Fact]
    public void Build_WithHeadersContainingQuotes_ShouldKeepEachHeaderAsOneArgument()
    {
        var options = new DownloadOptions
        {
            Input = "https://example.com/a.m3u8",
            Headers = "Referer: https://example.com/|User-Agent: Mozilla/5.0 \"Custom\""
        };

        var parsed = Win32CommandLine.Parse("prog " + ArgsBuilder.Build(options));

        Assert.Contains("Referer: https://example.com/", parsed);
        Assert.Contains("User-Agent: Mozilla/5.0 \"Custom\"", parsed);
    }

    // -------------------------------------------------------------------------
    // Values quoted inside a larger option (--mux-after-done's bin_path, the
    // --custom-range start-end pair) must round-trip just like standalone ones.
    // -------------------------------------------------------------------------

    [Theory]
    [InlineData(@"C:\Program Files\mkvtoolnix\mkvmerge.exe")]
    [InlineData(@"C:\od""d\mkvmerge.exe")]
    [InlineData(@"C:\mkvtoolnix\")]
    [InlineData(@"D:\tools\mkvmerge.exe")]
    [InlineData(@"mkvmerge.exe")]
    public void Build_MuxBinPath_ShouldReachReIntactThroughBothParsers(string binPath)
    {
        // Two parsers stand between the textbox and the muxer, and the path has to
        // survive both: CommandLineToArgvW (which must keep the mux option one token)
        // and RE's own ComplexParamParser (which splits that token on ':', so the
        // drive letter needs escaping or the path arrives as bare "C").
        var options = new DownloadOptions
        {
            Input = "https://example.com/a.m3u8",
            MuxAfterDone = true,
            MuxFormat = "mkv",
            Muxer = "mkvmerge",
            MuxBinPath = binPath,
            MuxKeepFiles = true,
            MuxSkipSubtitle = true,
            LivePerformAsVod = true
        };

        var parsed = Win32CommandLine.Parse("prog " + ArgsBuilder.Build(options));

        var muxArg = parsed[Array.IndexOf(parsed, "--mux-after-done") + 1];
        Assert.Equal(binPath, ReComplexParam.GetValue(muxArg, "bin_path"));
        Assert.Equal("mkv", ReComplexParam.GetValue(muxArg, "format"));
        Assert.Equal("mkvmerge", ReComplexParam.GetValue(muxArg, "muxer"));
        Assert.Equal("true", ReComplexParam.GetValue(muxArg, "keep"));
        Assert.Equal("true", ReComplexParam.GetValue(muxArg, "skip_sub"));
        // The flag after the mux option must still be its own argument.
        Assert.Contains("--live-perform-as-vod", parsed);
    }

    [Fact]
    public void Build_MuxBinPath_ShouldBeEscapedTheWayReDocumentsIt()
    {
        // RE's --morehelp mux-after-done prints bin_path="C\:\Program Files\...",
        // so the emitted text should read that way too (minus the quotes the Windows
        // parser eats) — a reader comparing the preview bar against RE's own docs
        // should see the same thing.
        var options = new DownloadOptions
        {
            Input = "https://example.com/a.m3u8",
            MuxAfterDone = true,
            MuxFormat = "mkv",
            Muxer = "mkvmerge",
            MuxBinPath = @"C:\Program Files\MKVToolNix\mkvmerge.exe"
        };

        Assert.Contains(@"bin_path=""C\:\Program Files\MKVToolNix\mkvmerge.exe""", ArgsBuilder.Build(options));
    }

    [Fact]
    public void AppendQuoted_ShouldNotAllocateAFreshSearchArrayPerCall()
    {
        // Guards the "zero-allocation fast path": the escape-character set must be a
        // cached static, not a `new[]` literal evaluated on every invocation.
        var source = File.ReadAllText(ArgsBuilderSourcePath());

        Assert.DoesNotContain("IndexOfAny(new", source);
        Assert.Contains("EscapeChars", source);
    }

    private static string ArgsBuilderSourcePath()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            var candidate = Path.Combine(dir.FullName, "N_m3u8DL_RE_GUI.Core", "ArgsBuilder.cs");
            if (File.Exists(candidate))
                return candidate;
            dir = dir.Parent;
        }
        throw new FileNotFoundException("Could not locate ArgsBuilder.cs from " + AppContext.BaseDirectory);
    }

    [Theory]
    [InlineData("00:01:00", "00:02:00")]
    [InlineData("00:01:00\"", "00:02:00")]
    [InlineData("00:01:00", @"00:02:00\")]
    public void Build_CustomRange_ShouldSurviveAsOneArgument(string start, string end)
    {
        var options = new DownloadOptions
        {
            Input = "https://example.com/a.m3u8",
            RangeStart = start,
            RangeEnd = end,
            SkipMerge = true
        };

        var parsed = Win32CommandLine.Parse("prog " + ArgsBuilder.Build(options));

        Assert.Contains("--custom-range", parsed);
        Assert.Contains($"{start}-{end}", parsed);
        Assert.Contains("--skip-merge", parsed);
    }
}

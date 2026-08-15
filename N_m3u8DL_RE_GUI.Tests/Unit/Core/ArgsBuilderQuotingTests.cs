#nullable enable
using System.Runtime.InteropServices;
using System.Text;
using N_m3u8DL_RE_GUI.Core;
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

        var parsed = ParseCommandLine("prog" + rendered);

        Assert.Equal(new[] { "prog", value }, parsed);
    }

    [Theory]
    [InlineData("plain")]
    [InlineData(@"C:\Downloads")]
    [InlineData("with \"quotes\" inside")]
    public void AppendIfNotEmpty_ShouldRoundTripFlagAndValue(string value)
    {
        var rendered = new StringBuilder().AppendIfNotEmpty("--save-dir", value).ToString();

        var parsed = ParseCommandLine("prog" + rendered);

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

        var parsed = ParseCommandLine("prog " + ArgsBuilder.Build(options));

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

        var parsed = ParseCommandLine("prog " + ArgsBuilder.Build(options));

        Assert.Contains("Referer: https://example.com/", parsed);
        Assert.Contains("User-Agent: Mozilla/5.0 \"Custom\"", parsed);
    }

    // -------------------------------------------------------------------------
    // Characterisation tests: these lock in argument construction that does NOT
    // go through the escaper. They document current behaviour so a future fix is
    // a deliberate, visible change rather than an accidental one.
    // -------------------------------------------------------------------------

    [Fact]
    public void Build_MuxBinPathWithSpaces_ShouldStillParseAsOneArgument()
    {
        // --mux-after-done builds "format=...:bin_path=\"...\"" by raw interpolation
        // rather than through the escaper. A mid-token quote still round-trips because
        // CommandLineToArgvW toggles quoting without splitting the token, so paths with
        // spaces are safe.
        var options = new DownloadOptions
        {
            Input = "https://example.com/a.m3u8",
            MuxAfterDone = true,
            MuxFormat = "mkv",
            Muxer = "mkvmerge",
            MuxBinPath = @"C:\Program Files\mkvtoolnix\mkvmerge.exe"
        };

        var parsed = ParseCommandLine("prog " + ArgsBuilder.Build(options));

        Assert.Contains(@"format=mkv:muxer=mkvmerge:bin_path=C:\Program Files\mkvtoolnix\mkvmerge.exe", parsed);
    }

    [Fact]
    public void Build_MuxBinPathContainingAQuote_ShouldSurviveAsOneArgument()
    {
        var options = new DownloadOptions
        {
            Input = "https://example.com/a.m3u8",
            MuxAfterDone = true,
            MuxFormat = "mkv",
            Muxer = "mkvmerge",
            MuxBinPath = @"C:\od""d\mkvmerge.exe"
        };

        var parsed = ParseCommandLine("prog " + ArgsBuilder.Build(options));

        Assert.Contains(@"format=mkv:muxer=mkvmerge:bin_path=C:\od""d\mkvmerge.exe", parsed);
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

    [Fact]
    public void Build_CustomRange_ShouldParseAsOneArgumentForWellFormedTimestamps()
    {
        // --custom-range uses $" --custom-range \"{start}-{end}\"" directly rather than
        // the escaper. Timestamps contain no quotes or backslashes, so this is safe.
        var options = new DownloadOptions
        {
            Input = "https://example.com/a.m3u8",
            RangeStart = "00:01:00",
            RangeEnd = "00:02:00"
        };

        var args = ArgsBuilder.Build(options);
        var parsed = ParseCommandLine("prog " + args);

        Assert.Contains("--custom-range \"00:01:00-00:02:00\"", args);
        Assert.Contains("--custom-range", parsed);
        Assert.Contains("00:01:00-00:02:00", parsed);
    }

    [Fact]
    public void Build_CustomRangeContainingAQuote_ShouldSurviveAsOneArgument()
    {
        var options = new DownloadOptions
        {
            Input = "https://example.com/a.m3u8",
            RangeStart = "00:01:00\"",
            RangeEnd = "00:02:00"
        };

        var parsed = ParseCommandLine("prog " + ArgsBuilder.Build(options));

        Assert.Contains("00:01:00\"-00:02:00", parsed);
    }

    // -------------------------------------------------------------------------
    // Win32 command-line parser
    // -------------------------------------------------------------------------

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint CommandLineToArgvW(string lpCmdLine, out int pNumArgs);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern nint LocalFree(nint hMem);

    /// <summary>
    /// Parses a command line with the exact same routine the CRT/Windows uses when a
    /// process reads its own arguments.
    /// </summary>
    private static string[] ParseCommandLine(string commandLine)
    {
        var argv = CommandLineToArgvW(commandLine, out var argc);
        if (argv == nint.Zero)
            throw new InvalidOperationException($"CommandLineToArgvW failed for: {commandLine}");

        try
        {
            var result = new string[argc];
            for (var i = 0; i < argc; i++)
            {
                var ptr = Marshal.ReadIntPtr(argv, i * nint.Size);
                result[i] = Marshal.PtrToStringUni(ptr) ?? string.Empty;
            }
            return result;
        }
        finally
        {
            LocalFree(argv);
        }
    }
}

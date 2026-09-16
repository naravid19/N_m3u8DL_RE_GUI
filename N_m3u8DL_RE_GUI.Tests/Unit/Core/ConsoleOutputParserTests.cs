#nullable enable
using N_m3u8DL_RE_GUI.Core;
using Xunit;

namespace N_m3u8DL_RE_GUI.Tests.Unit.Core;

public class ConsoleOutputParserTests
{
    [Theory]
    [InlineData("plain text", "plain text")]
    [InlineData("\u001b[32mgreen\u001b[0m", "green")]
    [InlineData("\u001b[1;33mbold yellow\u001b[0m tail", "bold yellow tail")]
    [InlineData("\u001b[2K\u001b[1Gredrawn", "redrawn")]
    [InlineData("no escapes at all", "no escapes at all")]
    [InlineData("", "")]
    public void StripAnsi_ShouldRemoveEscapeSequencesOnly(string input, string expected)
    {
        Assert.Equal(expected, ConsoleOutputParser.StripAnsi(input));
    }

    [Theory]
    [InlineData("Downloading... 45%", 45)]
    [InlineData("Vid 1080p | 45.7% | 3.2MBps", 45)]
    [InlineData("100%", 100)]
    [InlineData("0%", 0)]
    [InlineData("first 10% then 80%", 80)]      // last match wins — it is the freshest
    [InlineData("no percent here", null)]
    [InlineData("", null)]
    [InlineData("999%", null)]                   // out of range, ignore
    [InlineData("file_100%_name.ts", 100)]
    public void TryExtractPercent_ShouldReturnTheLastValidPercentage(string line, int? expected)
    {
        Assert.Equal(expected, ConsoleOutputParser.TryExtractPercent(line));
    }

    [Fact]
    public void TryExtractPercent_ShouldIgnoreAnsiNoise()
    {
        Assert.Equal(72, ConsoleOutputParser.TryExtractPercent("\u001b[32m72%\u001b[0m done"));
    }

    [Theory]
    [InlineData("  \u001b[32mhello\u001b[0m  ", "hello")]
    [InlineData("\u001b[2K", "")]
    [InlineData("   ", "")]
    [InlineData("\r\n", "")]
    public void Clean_ShouldStripEscapesAndTrim(string input, string expected)
    {
        Assert.Equal(expected, ConsoleOutputParser.Clean(input));
    }

    [Fact]
    public void Clean_ShouldPreserveInternalSpacingAndUnicode()
    {
        Assert.Equal("ตอนที่ 1 中文 — dash", ConsoleOutputParser.Clean("  ตอนที่ 1 中文 — dash  "));
    }

    [Fact]
    public void SplitOutput_ShouldSeparateRecordsFromNewlineLessProgressFrames()
    {
        // Shape seen in real runs: records glued directly onto progress frame redraws.
        var text = "08:35:59.885 INFO : 保存文件名: index" +
                   "Vid Kbps ------------------------------ 1/1213 0.08% -152.20KBps00:02:54" +
                   "Vid Kbps ------------------------------ 7/1213 0.58% 4.42MBps00:08:37" +
                   "08:36:01.987 INFO : [0x100]: Video, h264 (Main), 854x480";

        var segments = ConsoleOutputParser.SplitOutput(text);

        Assert.Collection(segments,
            s => { Assert.Equal("08:35:59.885 INFO : 保存文件名: index", s.Text); Assert.False(s.IsProgress); },
            s => { Assert.Contains("1/1213", s.Text); Assert.True(s.IsProgress); },
            s => { Assert.Contains("7/1213", s.Text); Assert.True(s.IsProgress); },
            s => { Assert.Contains("[0x100]", s.Text); Assert.False(s.IsProgress); });
    }

    [Fact]
    public void SplitOutput_ShouldSplitRecordsGluedOntoEachOther()
    {
        var segments = ConsoleOutputParser.SplitOutput(
            "08:35:59.550 INFO : 加载URL: https://example.com08:35:59.867 WARN : 写出meta json");

        Assert.Collection(segments,
            s => Assert.Equal("08:35:59.550 INFO : 加载URL: https://example.com", s.Text),
            s => Assert.Equal("08:35:59.867 WARN : 写出meta json", s.Text));
    }

    [Fact]
    public void SplitOutput_ShouldHoldTrailingIncompleteFragment()
    {
        var segments = ConsoleOutputParser.SplitOutput(
            "08:35:59.550 INFO : 加载URL: https://example.com08:36:0");

        Assert.Single(segments);
        Assert.False(segments[0].IsComplete);
        Assert.Equal("08:35:59.550 INFO : 加载URL: https://example.com08:36:0", segments[0].Text);
    }

    [Fact]
    public void SplitOutput_ShouldKeepNewlineTerminatedRecordComplete()
    {
        var segments = ConsoleOutputParser.SplitOutput("08:35:59.550 INFO : Done\r\n");

        Assert.Single(segments);
        Assert.True(segments[0].IsComplete);
        Assert.Equal("08:35:59.550 INFO : Done", segments[0].Text);
    }

    [Fact]
    public void SplitOutput_ShouldCutTheSpacelessErrorStampOffAProgressFrame()
    {
        // Real shape from a failed run: the terminal record is glued onto the tail of a
        // progress frame and the engine writes "ERROR:" with no space before the colon.
        // When the stamp pattern demanded whitespace there, this record stayed inside the
        // progress segment, never reached ClassifyOutcome, and the run was reported as
        // successful — skipping retry, CF fallback and the missing-segment merge.
        var text = "Vid Kbps --- 1433/1434 99.93% 947.94MB/948.60MB 0.00Bps 00:00:00 "
                   + "22:13:55.280 ERROR: Failed";

        var segments = ConsoleOutputParser.SplitOutput(text);

        Assert.Collection(segments,
            s => { Assert.Contains("1433/1434", s.Text); Assert.True(s.IsProgress); },
            s =>
            {
                Assert.Equal("22:13:55.280 ERROR: Failed", s.Text);
                Assert.False(s.IsProgress);
                Assert.Equal(ConsoleOutputParser.EngineOutcome.FatalError,
                    ConsoleOutputParser.ClassifyOutcome(s.Text));
            });
    }

    [Fact]
    public void ClassifyOutcome_ShouldStillSeeAFailureSignatureLeftOnAProgressFrame()
    {
        // Defense in depth: even if a frame is never split, the glued failure must be
        // detectable from the frame text itself.
        var frame = "Vid Kbps --- 1433/1434 99.93% 947.94MB/948.60MB 0.00Bps 00:00:00 22:13:55.280 ERROR: Failed";
        Assert.Equal(ConsoleOutputParser.EngineOutcome.FatalError,
            ConsoleOutputParser.ClassifyOutcome(ConsoleOutputParser.Clean(frame)));
    }

    [Theory]
    [InlineData("Vid Kbps --- 1433/1434 99.93% 947.94MB/948.60MB 0.00Bps 00:00:00", 1433, 1434)]
    [InlineData("Vid Kbps --- 12/101 11.88% 42.50MB/356.00MB 1.20MBps --:--:--", 12, 101)]
    [InlineData("Sub Kbps --- 5/100 5.00% --:--:--", null, null)]   // not the video bar
    [InlineData("08:35:59.550 INFO : Done", null, null)]
    [InlineData("", null, null)]
    public void TryExtractVideoSegmentCount_ShouldOnlyTrustTheVideoBar(string line, int? done, int? total)
    {
        var count = ConsoleOutputParser.TryExtractVideoSegmentCount(line);
        if (done == null)
            Assert.Null(count);
        else
        {
            Assert.NotNull(count);
            Assert.Equal(done.Value, count!.Value.Done);
            Assert.Equal(total!.Value, count.Value.Total);
        }
    }

    [Theory]
    [InlineData("22:36:19.034 ERROR: Failed", ConsoleOutputParser.EngineOutcome.FatalError)]
    [InlineData("something ERROR : Failed tail", ConsoleOutputParser.EngineOutcome.FatalError)]
    [InlineData("22:29:53.044 WARN : Response status code does not indicate success: 404 (Not Found).", ConsoleOutputParser.EngineOutcome.HttpBlocked)]
    [InlineData("22:29:53.044 WARN : Response status code does not indicate success: 403 (Forbidden).", ConsoleOutputParser.EngineOutcome.HttpBlocked)]
    [InlineData("08:35:59.550 INFO : Done", ConsoleOutputParser.EngineOutcome.None)]
    [InlineData("Vid Kbps --- 12/100 12%", ConsoleOutputParser.EngineOutcome.None)]
    [InlineData("", ConsoleOutputParser.EngineOutcome.None)]
    public void ClassifyOutcome_ShouldDetectEngineFailureSignatures(string record, ConsoleOutputParser.EngineOutcome expected)
    {
        Assert.Equal(expected, ConsoleOutputParser.ClassifyOutcome(record));
    }

    [Fact]
    public void ClassifyOutcome_ShouldPreferFatalOverHttpBlocked()
    {
        var combined = "WARN : Response status code does not indicate success: 404 (Not Found). ERROR: Failed";
        Assert.Equal(ConsoleOutputParser.EngineOutcome.FatalError, ConsoleOutputParser.ClassifyOutcome(combined));
    }

    [Theory]
    [InlineData("23:11:29.287 WARN : Packet corrupt (stream = 0, dts = 672556677).", true)]
    [InlineData("23:11:29.274 WARN : [in#0/mpegts @ 000001c39c022480] corrupt input packet in stream 0", true)]
    [InlineData("22:29:53.044 WARN : Response status code does not indicate success: 404 (Not Found).", false)]
    [InlineData("08:35:59.550 INFO : Done", false)]
    [InlineData("", false)]
    public void IsRepetitiveRecord_ShouldMatchOnlyBurstProneDiagnostics(string record, bool expected)
    {
        Assert.Equal(expected, ConsoleOutputParser.IsRepetitiveRecord(record));
    }

    [Fact]
    public void IsRepetitiveRecord_ShouldNeverClassifyAsFailure()
    {
        // The corrupt-packet noise must stay cosmetic: it is a WARN, not a fatal failure.
        Assert.Equal(ConsoleOutputParser.EngineOutcome.None,
            ConsoleOutputParser.ClassifyOutcome("23:11:29.287 WARN : Packet corrupt (stream = 0, dts = 672556677)."));
    }

    [Fact]
    public void RepetitionKey_ShouldCollapseBurstMembersWithDifferentTimestampsAndDts()
    {
        // Real records from a merge phase: only stamp and dts differ between members.
        var a = ConsoleOutputParser.RepetitionKey(
            "23:29:54.024 WARN : [in#0/mpegts @ 000001fdee300640] Packet corrupt (stream = 0, dts = 684448557).");
        var b = ConsoleOutputParser.RepetitionKey(
            "23:29:54.033 WARN : [in#0/mpegts @ 000001fdee300640] Packet corrupt (stream = 0, dts = 665457).");
        var c = ConsoleOutputParser.RepetitionKey(
            "23:29:54.034 WARN : [in#0/mpegts @ 000001fdee300280] corrupt input packet in stream 0");

        Assert.NotNull(a);
        Assert.Equal(a, b);           // same shape -> same burst
        Assert.NotEqual(a, c);        // different shape -> different burst
    }

    [Fact]
    public void RepetitionKey_ShouldReturnNullForNonRepetitiveRecords()
    {
        Assert.Null(ConsoleOutputParser.RepetitionKey("23:29:54.034 INFO : Done"));
        Assert.Null(ConsoleOutputParser.RepetitionKey(
            "22:29:53.044 WARN : Response status code does not indicate success: 404 (Not Found)."));
        Assert.Null(ConsoleOutputParser.RepetitionKey(""));
    }

    [Theory]
    [InlineData("Vid Kbps --- 1257/1268 99.13% 5.53MBps00:00:01", 1257)]
    [InlineData("Vid Kbps --- 0/1268 0.00% -0.00Bps --:--:--", 0)]
    [InlineData("Sub Kbps --- 5/100 5.00% --:--:--", 5)]
    [InlineData("08:35:59.550 INFO : Done", null)]
    [InlineData("", null)]
    public void TryExtractSegmentCount_ShouldReadTheVideoBarCounters(string line, int? expectedDone)
    {
        var count = ConsoleOutputParser.TryExtractSegmentCount(line);
        if (expectedDone == null)
            Assert.Null(count);
        else
        {
            Assert.NotNull(count);
            Assert.Equal(expectedDone.Value, count!.Value.Done);
            Assert.True(count.Value.Total > 0);
        }
    }

    [Theory]
    [InlineData(
        "Vid Kbps ------------------------------ 764/1268 60.25% 505.17MB/846.18MB4.51MBps00:00:58",
        "Vid Kbps ------------------------------ 764/1268 60.25% 505.17MB/846.18MB 4.51MBps 00:00:58")]
    [InlineData(
        "Vid Kbps --- 1/1213 0.08% -152.20KBps00:02:54",
        "Vid Kbps --- 1/1213 0.08% -152.20KBps 00:02:54")]
    [InlineData(
        "Vid Kbps --- 12/101 11.88% 42.50MB/356.00MB1.20MBps--:--:--",
        "Vid Kbps --- 12/101 11.88% 42.50MB/356.00MB 1.20MBps --:--:--")]
    [InlineData(
        "Vid Kbps --- 7/1213 0.58% 4.42MB/765.13MB4.27MBps00:08:37",
        "Vid Kbps --- 7/1213 0.58% 4.42MB/765.13MB 4.27MBps 00:08:37")]
    public void RepairFieldSpacing_ShouldSeparateGluedProgressFields(string glued, string expected)
    {
        Assert.Equal(expected, ConsoleOutputParser.RepairFieldSpacing(glued));
    }

    [Fact]
    public void RepairFieldSpacing_ShouldLeaveAlreadySpacedRowsUnchanged()
    {
        var spaced = "Vid Kbps --- 12/101 11.88% 42.50MB/356.00MB 1.20MBps 00:08:37";
        Assert.Equal(spaced, ConsoleOutputParser.RepairFieldSpacing(spaced));
    }
}

#nullable enable
using N_m3u8DL_RE_GUI.Core;
using System.Linq;
using Xunit.Abstractions;
using Xunit;

namespace N_m3u8DL_RE_GUI.Tests.Unit.Core;

public class SplitOutputBurstProbe
{
    private readonly ITestOutputHelper _output;
    public SplitOutputBurstProbe(ITestOutputHelper output) => _output = output;

    [Fact]
    public void Probe_SplitOutputOnBurstChunk()
    {
        var lines = new System.Text.StringBuilder();
        for (var i = 0; i < 4; i++)
        {
            lines.Append($"23:43:25.{i * 3 % 1000:D3} WARN : [in#0/mpegts @ 00000278ca1d5dc0] Packet corrupt (stream = 0, dts = {i * 540540}).\r\n");
            lines.Append($"23:43:25.{(i * 3 + 1) % 1000:D3} WARN : [in#0/mpegts @ 00000278ca1d5c40] corrupt input packet in stream 0\r\n");
        }

        var segments = ConsoleOutputParser.SplitOutput(lines.ToString());
        _output.WriteLine($"segments={segments.Count}");
        foreach (var s in segments)
        {
            var cleaned = ConsoleOutputParser.Clean(s.Text);
            _output.WriteLine($"progress={s.IsProgress} complete={s.IsComplete} key={ConsoleOutputParser.RepetitionKey(cleaned) ?? "null"} :: {cleaned[..Math.Min(60, cleaned.Length)]}");
        }
    }
}

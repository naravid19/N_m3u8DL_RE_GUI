#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace N_m3u8DL_RE_GUI.Core.Capture;

/// <summary>One HTTP header worth re-sending. Name keeps its original casing.</summary>
public sealed record CapturedHeader(string Name, string Value);

/// <summary>What kind of stream a captured URL appears to be. Drives ranking.</summary>
public enum CapturedStreamKind
{
    /// <summary>Not recognisably a stream — a segment, a script, an image.</summary>
    Unknown,
    Hls,
    Dash,
    /// <summary>A progressive media file: .mp4, .webm, or a video/* response.</summary>
    Media
}

/// <summary>
/// A single request lifted out of a browser capture, reduced to what a downloader
/// needs. Produced by every capture path (cURL paste, HAR drop) so the GUI has one
/// shape to consume.
/// </summary>
public sealed record CapturedRequest(
    string Url,
    IReadOnlyList<CapturedHeader> Headers,
    CapturedStreamKind Kind,
    IReadOnlyDictionary<string, string>? Directives = null)
{
    public IReadOnlyDictionary<string, string> Directives { get; init; } =
        Directives ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Newline-separated "Name: Value" lines, the format TextBox_Headers holds.
    /// Newlines are illegal inside an HTTP header value, so this round-trips
    /// losslessly — unlike the legacy pipe separator.
    /// </summary>
    public string ToHeaderLines() =>
        string.Join("\n", Headers.Select(h => $"{h.Name}: {h.Value}"));
}

#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace N_m3u8DL_RE_GUI.Core.Capture;

/// <summary>
/// Pulls downloadable stream candidates out of a browser HAR capture.
///
/// A HAR holds everything the browser did, including credentials in request
/// bodies. This type reads only request URLs, request headers, response status
/// and response mimeType — never response bodies — and never logs what it reads.
/// </summary>
public static class HarStreamExtractor
{
    /// <summary>Refuse anything larger. A HAR this big is a mistake, and parsing it
    /// would balloon well past its own size in memory.</summary>
    public const long MaxFileBytes = 256L * 1024 * 1024;

    /// <summary>Media segments. Excluded even when the response says video/*, because
    /// a live capture contains hundreds of them and exactly one manifest.</summary>
    private static readonly HashSet<string> SegmentExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".ts", ".m4s", ".aac", ".mp3", ".vtt", ".cmfv", ".cmfa", ".cmft", ".init", ".key"
    };

    public static IReadOnlyList<CapturedRequest> ExtractFromFile(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("Path is required.", nameof(path));

        var info = new FileInfo(path);
        if (!info.Exists)
            throw new FileNotFoundException("HAR file not found.", path);

        if (info.Length > MaxFileBytes)
        {
            throw new InvalidDataException(
                $"This HAR is {info.Length / (1024 * 1024)} MB, over the " +
                $"{MaxFileBytes / (1024 * 1024)} MB limit. Re-capture with the network " +
                "log cleared just before you press play.");
        }

        using var stream = File.OpenRead(path);
        return Extract(stream);
    }

    public static IReadOnlyList<CapturedRequest> Extract(Stream harStream)
    {
        JsonDocument document;
        try
        {
            // ponytail: JsonDocument buffers the whole file. Fine up to the cap above;
            // upgrade path is a Utf8JsonReader walk if the cap ever needs raising.
            document = JsonDocument.Parse(harStream);
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException(
                "This file is not valid JSON, so it cannot be a HAR capture.", ex);
        }

        using (document)
        {
            if (!document.RootElement.TryGetProperty("log", out var log) ||
                !log.TryGetProperty("entries", out var entries) ||
                entries.ValueKind != JsonValueKind.Array)
            {
                throw new InvalidDataException(
                    "This JSON file has no log.entries array, so it is not a HAR capture.");
            }

            var seen = new HashSet<string>(StringComparer.Ordinal);
            var found = new List<CapturedRequest>();

            foreach (var entry in entries.EnumerateArray())
            {
                var captured = ReadEntry(entry);
                if (captured is null)
                    continue;

                // Collapse the range requests a <video> element fires for one file.
                if (!seen.Add(captured.Url))
                    continue;

                found.Add(captured);
            }

            // Manifests before progressive files; original request order within each
            // group, because a master playlist is fetched before its variants.
            return found
                .Select((request, index) => (request, index))
                .OrderBy(x => x.request.Kind == CapturedStreamKind.Media ? 1 : 0)
                .ThenBy(x => x.index)
                .Select(x => x.request)
                .ToList();
        }
    }

    private static CapturedRequest? ReadEntry(JsonElement entry)
    {
        if (!entry.TryGetProperty("request", out var request) ||
            !request.TryGetProperty("url", out var urlElement) ||
            urlElement.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        var url = urlElement.GetString();
        if (string.IsNullOrWhiteSpace(url))
            return null;

        if (!entry.TryGetProperty("response", out var response))
            return null;

        var status = 0;
        string? mimeType = null;

        if (response.TryGetProperty("status", out var statusElement) &&
            statusElement.ValueKind == JsonValueKind.Number)
        {
            status = statusElement.GetInt32();
        }

        if (response.TryGetProperty("content", out var content) &&
            content.TryGetProperty("mimeType", out var mimeElement) &&
            mimeElement.ValueKind == JsonValueKind.String)
        {
            mimeType = mimeElement.GetString();
        }

        var kind = Classify(url, mimeType, status);
        if (kind == CapturedStreamKind.Unknown)
            return null;

        return new CapturedRequest(url, ReadHeaders(request), kind);
    }

    private static List<CapturedHeader> ReadHeaders(JsonElement request)
    {
        var headers = new List<CapturedHeader>();

        if (!request.TryGetProperty("headers", out var headerArray) ||
            headerArray.ValueKind != JsonValueKind.Array)
        {
            return headers;
        }

        foreach (var header in headerArray.EnumerateArray())
        {
            if (!header.TryGetProperty("name", out var nameElement) ||
                !header.TryGetProperty("value", out var valueElement) ||
                nameElement.ValueKind != JsonValueKind.String ||
                valueElement.ValueKind != JsonValueKind.String)
            {
                continue;
            }

            var name = nameElement.GetString();
            var value = valueElement.GetString();

            if (!HeaderPolicy.ShouldForward(name) || string.IsNullOrWhiteSpace(value))
                continue;

            headers.Add(new CapturedHeader(name!.Trim(), value!.Trim()));
        }

        return headers;
    }

    internal static CapturedStreamKind Classify(string url, string? mimeType, int status)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            return CapturedStreamKind.Unknown;
        }

        var extension = Path.GetExtension(uri.AbsolutePath);

        // Segments first: a .ts served as video/mp2t must never outrank the manifest.
        if (SegmentExtensions.Contains(extension))
            return CapturedStreamKind.Unknown;

        var byUrl = CurlCommandParser.ClassifyUrl(url);
        if (byUrl is CapturedStreamKind.Hls or CapturedStreamKind.Dash)
            return byUrl;

        var mime = mimeType ?? string.Empty;
        if (mime.Contains("mpegurl", StringComparison.OrdinalIgnoreCase))
            return CapturedStreamKind.Hls;
        if (mime.Contains("dash+xml", StringComparison.OrdinalIgnoreCase))
            return CapturedStreamKind.Dash;

        // Progressive media only counts when the server actually served it.
        if (status is 200 or 206)
        {
            if (byUrl == CapturedStreamKind.Media)
                return CapturedStreamKind.Media;
            if (mime.StartsWith("video/", StringComparison.OrdinalIgnoreCase))
                return CapturedStreamKind.Media;
        }

        return CapturedStreamKind.Unknown;
    }
}

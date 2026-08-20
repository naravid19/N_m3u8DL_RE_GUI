#nullable enable
using System;
using System.Collections.Generic;

namespace N_m3u8DL_RE_GUI.Core.Capture;

/// <summary>
/// Decides which captured headers are worth re-sending. A browser sends far more
/// than a downloader needs, and some of them break it.
/// </summary>
public static class HeaderPolicy
{
    private static readonly HashSet<string> Dropped = new(StringComparer.OrdinalIgnoreCase)
    {
        // Transport-level: the HTTP client owns these.
        "accept-encoding", "content-length", "host", "connection",
        "te", "trailer", "transfer-encoding", "expect", "keep-alive",
        // Navigation hints with no bearing on stream access.
        "priority", "dnt", "upgrade-insecure-requests", "cache-control", "pragma",
    };

    public static bool ShouldForward(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return false;

        var trimmed = name.Trim();

        // HTTP/2 pseudo-headers appear in HAR captures. They are not settable headers.
        if (trimmed.StartsWith(':'))
            return false;

        // sec-fetch-*, sec-ch-ua* — browser fingerprint metadata, pure noise here.
        if (trimmed.StartsWith("sec-", StringComparison.OrdinalIgnoreCase))
            return false;

        return !Dropped.Contains(trimmed);
    }
}

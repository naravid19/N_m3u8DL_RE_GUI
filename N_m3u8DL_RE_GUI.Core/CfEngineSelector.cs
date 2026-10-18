#nullable enable
using N_m3u8DL_RE_GUI.Core.Capture;

namespace N_m3u8DL_RE_GUI.Core;

/// <summary>
/// Decides which Cloudflare-bypass engine a run should use once the user has
/// ticked "Bypass Cloudflare" (the checkbox itself already routes the
/// no-bypass case straight to N_m3u8DL-RE, so this only has to choose between
/// the two CF-aware engines).
/// </summary>
public static class CfEngineSelector
{
    /// <summary>
    /// True when the run should go through the local reverse proxy + RE
    /// (DASH/MSS/anything CENC-keyed), false for the direct HLS downloader.
    /// A pinned engine tag ("direct" / "proxy") always wins; "auto" applies
    /// the decision table: HLS with no key stays on the fast direct path,
    /// everything else — DASH, MSS, unrecognised URLs, or HLS with a key
    /// supplied — needs the general-purpose proxy+RE path.
    /// </summary>
    public static bool ShouldUseProxy(string? engineTag, CapturedStreamKind kind, bool hasKey)
    {
        if (engineTag == "direct")
            return false;
        if (engineTag == "proxy")
            return true;

        // auto (or unrecognised tag — fail toward the engine that works for more cases)
        return !(kind == CapturedStreamKind.Hls && !hasKey);
    }
}

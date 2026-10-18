#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace N_m3u8DL_RE_GUI.Core.Capture;

/// <summary>
/// Authoritative, complete plan for applying a capture payload to GUI fields.
/// Every Stream-scoped field is replaced unconditionally, eliminating state leakage across
/// captures. The user's own preferences (for example the proxy) are not part of a plan.
/// </summary>
public sealed record CaptureApplyPlan(
    string Url,
    string HeaderLines,        // "" clears the box
    string SelectVideo,
    string SelectAudio,
    string SelectSubtitle,
    string SaveName,
    string CfReferer,
    string CfCookie,
    string? CfImpersonate,     // null = reset the combo to its default profile
    bool    EnableCfBypass,    // false unless the capture asks for Cloudflare bypass
    string? Warning,
    CapturedStreamKind Kind,
    int     HeaderCount)
{
    private static readonly HashSet<string> KnownImpersonates = new(StringComparer.OrdinalIgnoreCase)
    {
        "chrome131", "chrome120", "edge101", "safari17_0", "chrome"
    };

    public static CaptureApplyPlan? From(CapturedRequest? captured)
    {
        if (captured is null || string.IsNullOrWhiteSpace(captured.Url))
            return null;

        var headerLines = captured.Headers.Count > 0 ? captured.ToHeaderLines() : string.Empty;
        var headerDict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var h in captured.Headers)
        {
            headerDict[h.Name] = h.Value;
        }

        // Select directives
        captured.Directives.TryGetValue("select-video", out var selectVideo);
        captured.Directives.TryGetValue("select-audio", out var selectAudio);
        captured.Directives.TryGetValue("select-subtitle", out var selectSubtitle);
        captured.Directives.TryGetValue("save-name", out var saveName);

        // CF mappings
        // Referer: directive page-url beats header Referer
        string cfReferer = string.Empty;
        if (captured.Directives.TryGetValue("page-url", out var pageUrl) && !string.IsNullOrWhiteSpace(pageUrl))
        {
            cfReferer = pageUrl.Trim();
        }
        else if (headerDict.TryGetValue("Referer", out var refHeader) && !string.IsNullOrWhiteSpace(refHeader))
        {
            cfReferer = refHeader.Trim();
        }

        // Cookie: header Cookie
        string cfCookie = string.Empty;
        if (headerDict.TryGetValue("Cookie", out var cookieHeader) && !string.IsNullOrWhiteSpace(cookieHeader))
        {
            cfCookie = cookieHeader.Trim();
        }

        // Impersonate: directive impersonate (only if in known list)
        string? cfImpersonate = null;
        if (captured.Directives.TryGetValue("impersonate", out var imp) && !string.IsNullOrWhiteSpace(imp))
        {
            var trimmedImp = imp.Trim();
            if (KnownImpersonates.Contains(trimmedImp))
                cfImpersonate = trimmedImp.ToLowerInvariant();
        }

        // EnableCfBypass: directive cf (1/true); anything else, including no directive, is off
        var enableCfBypass = captured.Directives.TryGetValue("cf", out var cfVal)
            && (cfVal.Trim() == "1" || cfVal.Trim().Equals("true", StringComparison.OrdinalIgnoreCase));

        // Warning
        captured.Directives.TryGetValue("warn", out var warning);

        // Kind (allow nre-kind to override if provided)
        var kind = captured.Kind;
        if (captured.Directives.TryGetValue("kind", out var kindStr) && !string.IsNullOrWhiteSpace(kindStr))
        {
            if (Enum.TryParse<CapturedStreamKind>(kindStr.Trim(), true, out var parsedKind))
            {
                kind = parsedKind;
            }
        }

        return new CaptureApplyPlan(
            Url: captured.Url,
            HeaderLines: headerLines,
            SelectVideo: selectVideo?.Trim() ?? string.Empty,
            SelectAudio: selectAudio?.Trim() ?? string.Empty,
            SelectSubtitle: selectSubtitle?.Trim() ?? string.Empty,
            SaveName: saveName?.Trim() ?? string.Empty,
            CfReferer: cfReferer,
            CfCookie: cfCookie,
            CfImpersonate: cfImpersonate,
            EnableCfBypass: enableCfBypass,
            Warning: string.IsNullOrWhiteSpace(warning) ? null : warning.Trim(),
            Kind: kind,
            HeaderCount: captured.Headers.Count
        );
    }
}

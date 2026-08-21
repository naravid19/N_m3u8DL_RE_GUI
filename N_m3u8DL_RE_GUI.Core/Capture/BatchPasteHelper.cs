#nullable enable
using System;
using System.IO;
using System.Text;

namespace N_m3u8DL_RE_GUI.Core.Capture;

public static class BatchPasteHelper
{
    /// <summary>
    /// True when a pasted payload holds several stream URLs rather than one.
    /// The single-URL case must stay on the ordinary path, so this deliberately
    /// requires two or more.
    /// </summary>
    public static bool LooksLikeBatchList(string? payload)
    {
        if (string.IsNullOrWhiteSpace(payload))
            return false;

        // If it starts with curl, it is a cURL command, not a batch list.
        if (CurlCommandParser.LooksLikeCurl(payload))
            return false;

        var lines = payload.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.RemoveEmptyEntries);
        var urlCount = 0;

        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (trimmed.Length == 0 || trimmed.StartsWith('#') || trimmed.StartsWith("//"))
                continue;

            // Lines can be "[title],url" or plain "url"
            var commaIdx = trimmed.IndexOf(',');
            var candidateUrl = commaIdx >= 0 ? trimmed[(commaIdx + 1)..].Trim() : trimmed;

            if (Uri.TryCreate(candidateUrl, UriKind.Absolute, out var uri) &&
                (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
            {
                urlCount++;
            }
        }

        return urlCount >= 2;
    }

    /// <summary>
    /// Writes a pasted batch list to a temporary text file with UTF-8 encoding (no BOM).
    /// </summary>
    public static string WriteTempBatchFile(string payload)
    {
        var tempDir = Path.GetTempPath();
        var fileName = $"paste_{DateTime.Now:yyyyMMdd_HHmmss}_{Guid.NewGuid().ToString("N")[..6]}.txt";
        var filePath = Path.Combine(tempDir, fileName);

        // UTF-8 without BOM
        var utf8WithoutBom = new UTF8Encoding(false);
        File.WriteAllText(filePath, payload, utf8WithoutBom);

        return filePath;
    }
}

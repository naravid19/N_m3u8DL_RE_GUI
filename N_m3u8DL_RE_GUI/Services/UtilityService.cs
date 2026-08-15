#nullable enable
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using N_m3u8DL_RE_GUI.Core;

namespace N_m3u8DL_RE_GUI.Services;

/// <summary>
/// Implementation of utility service.
/// </summary>
public class UtilityService : IUtilityService, IDisposable
{
    private static readonly HttpClient SharedHttpClient = new() { Timeout = TimeSpan.FromSeconds(15) };
    private static readonly char[] _invalidPathChars = System.IO.Path.GetInvalidFileNameChars().Concat(System.IO.Path.GetInvalidPathChars()).Distinct().ToArray();
    private static readonly HashSet<string> _reservedDeviceNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
    };

    public UtilityService()
    {
    }

    public async Task<string> GetTitleFromUrlAsync(string url, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(url))
            return string.Empty;

        if (!InputValidation.IsHttpUrl(url))
            return string.Empty;

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(15));
        var token = timeoutCts.Token;

        try
        {
            if (url.Contains("v.qq.com"))
                return await GetQQTitleAsync(url, token);
            else
                return await GetHtmlTitleStreamingAsync(url, token);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to resolve title from URL '{url}': {ex.Message}");
            return string.Empty;
        }
    }

    private async Task<string> GetHtmlTitleStreamingAsync(string url, CancellationToken cancellationToken)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            using var response = await SharedHttpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();

            // Honour the charset the server declared. Defaulting to UTF-8 turned every
            // GBK/Big5 page title into replacement characters.
            var encoding = HtmlTitleExtractor.ResolveEncoding(response.Content.Headers.ContentType?.CharSet);

            using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var reader = new StreamReader(stream, encoding, detectEncodingFromByteOrderMarks: true);

            var buffer = new char[8192];
            var sb = new StringBuilder();
            var carry = string.Empty;
            int read;

            while ((read = await reader.ReadAsync(buffer, 0, buffer.Length)) > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var chunk = new string(buffer, 0, read);
                sb.Append(chunk);

                if (HtmlTitleExtractor.ContainsClosingTitleTag(chunk, ref carry))
                    break;

                // Bound allocations: stop buffering just past 256 KB.
                if (sb.Length > 256 * 1024)
                    break;
            }

            return HtmlTitleExtractor.Extract(sb.ToString());
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to get HTML title from {url}: {ex.Message}");
        }
        return string.Empty;
    }

    private async Task<string> GetQQTitleAsync(string url, CancellationToken cancellationToken)
    {
        try
        {
            var vidMatch = Regex.Match(url, @"vid=([^&]+)");
            if (vidMatch.Success)
            {
                var vid = vidMatch.Groups[1].Value;
                var apiUrl = $"https://vv.video.qq.com/getinfo?vids={vid}&platform=101001&charge=0&otype=json";
                var response = await SharedHttpClient.GetStringAsync(apiUrl, cancellationToken);
                
                // Extract title from JSON response
                var titleMatch = Regex.Match(response, @"""title"":""([^""]+)""");
                if (titleMatch.Success)
                {
                    return HtmlTitleExtractor.Clean(titleMatch.Groups[1].Value);
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to get QQ title: {ex.Message}");
        }
        return string.Empty;
    }

    public string GetValidFileName(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return string.Empty;

        string sanitized;
        if (path.IndexOfAny(_invalidPathChars) < 0)
        {
            sanitized = path.Trim();
        }
        else
        {
            sanitized = string.Create(path.Length, path, (span, p) => 
            {
                for (int i = 0; i < p.Length; i++)
                {
                    char c = p[i];
                    span[i] = Array.IndexOf(_invalidPathChars, c) >= 0 ? '_' : c;
                }
            }).Trim();
        }

        // Windows matches DOS device names against the segment before the FIRST dot, so
        // "CON.txt.bak" is still reserved. Path.GetFileNameWithoutExtension strips only the
        // last extension and returned "CON.txt", which never matched.
        var firstDot = sanitized.IndexOf('.');
        var baseName = firstDot < 0 ? sanitized : sanitized[..firstDot];
        if (_reservedDeviceNames.Contains(baseName))
        {
            return $"_{sanitized}";
        }

        return sanitized;
    }

    public string? SelectFolder(string description, string? initialPath = null)
    {
        try
        {
#pragma warning disable CA1416 // Validate platform compatibility
            using var dialog = new System.Windows.Forms.FolderBrowserDialog
            {
                Description = description,
                ShowNewFolderButton = true
            };

            if (!string.IsNullOrWhiteSpace(initialPath) && System.IO.Directory.Exists(initialPath))
            {
                dialog.SelectedPath = initialPath;
            }

            if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                return dialog.SelectedPath;
            }
#pragma warning restore CA1416
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Folder selection error: {ex.Message}");
        }

        return null;
    }

    public bool FileExists(string filePath)
    {
        return !string.IsNullOrWhiteSpace(filePath) && System.IO.File.Exists(filePath);
    }

    public string GetFileExtension(string filePath)
    {
        return !string.IsNullOrWhiteSpace(filePath) ? System.IO.Path.GetExtension(filePath) : string.Empty;
    }

    public void Dispose()
    {
    }
}

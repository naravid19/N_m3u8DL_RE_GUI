#nullable enable
using System.Diagnostics;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using N_m3u8DL_RE_GUI.Core;

namespace N_m3u8DL_RE_GUI.Services;

/// <summary>
/// Implementation of utility service.
/// </summary>
public class UtilityService : IUtilityService
{
    private readonly HttpClient _httpClient;

    public UtilityService()
    {
        _httpClient = new HttpClient();
        _httpClient.Timeout = TimeSpan.FromMinutes(2);
    }

    public async Task<string> GetTitleFromUrlAsync(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return string.Empty;

        if (!InputValidation.IsHttpUrl(url))
            return string.Empty;

        try
        {
            // Handle different URL patterns
            if (url.Contains("iqiyi.com"))
                return await GetIqiyiTitleAsync(url);
            else if (url.Contains("v.qq.com"))
                return await GetQQTitleAsync(url);
            else if (url.Contains("wetv.vip"))
                return await GetWeTVTitleAsync(url);
            else
                return await GetGenericTitleAsync(url);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to resolve title from URL '{url}': {ex.Message}");
            return string.Empty;
        }
    }

    private async Task<string> GetIqiyiTitleAsync(string url)
    {
        try
        {
            var response = await _httpClient.GetStringAsync(url);
            var titleMatch = Regex.Match(response, @"<title[^>]*>([^<]+)</title>", RegexOptions.IgnoreCase);
            if (titleMatch.Success)
            {
                var title = titleMatch.Groups[1].Value.Trim();
                return CleanTitle(title);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to get iQiyi title: {ex.Message}");
        }
        return string.Empty;
    }

    private async Task<string> GetQQTitleAsync(string url)
    {
        try
        {
            var vidMatch = Regex.Match(url, @"vid=([^&]+)");
            if (vidMatch.Success)
            {
                var vid = vidMatch.Groups[1].Value;
                var apiUrl = $"https://vv.video.qq.com/getinfo?vids={vid}&platform=101001&charge=0&otype=json";
                var response = await _httpClient.GetStringAsync(apiUrl);
                
                // Extract title from JSON response
                var titleMatch = Regex.Match(response, @"""title"":""([^""]+)""");
                if (titleMatch.Success)
                {
                    return CleanTitle(titleMatch.Groups[1].Value);
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to get QQ title: {ex.Message}");
        }
        return string.Empty;
    }

    private async Task<string> GetWeTVTitleAsync(string url)
    {
        try
        {
            var response = await _httpClient.GetStringAsync(url);
            var titleMatch = Regex.Match(response, @"<title[^>]*>([^<]+)</title>", RegexOptions.IgnoreCase);
            if (titleMatch.Success)
            {
                var title = titleMatch.Groups[1].Value.Trim();
                return CleanTitle(title);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to get WeTV title: {ex.Message}");
        }
        return string.Empty;
    }

    private async Task<string> GetGenericTitleAsync(string url)
    {
        try
        {
            var response = await _httpClient.GetStringAsync(url);
            var titleMatch = Regex.Match(response, @"<title[^>]*>([^<]+)</title>", RegexOptions.IgnoreCase);
            if (titleMatch.Success)
            {
                var title = titleMatch.Groups[1].Value.Trim();
                return CleanTitle(title);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to get generic title: {ex.Message}");
        }
        return string.Empty;
    }

    private string CleanTitle(string title)
    {
        if (string.IsNullOrWhiteSpace(title))
            return string.Empty;

        // Remove common suffixes
        title = Regex.Replace(title, "[-_\\s]*(\\u7231\\u5947\\u827A).*?$", "", RegexOptions.IgnoreCase);
        title = Regex.Replace(title, "[-_\\s]*(\\u817E\\u8BAF\\u89C6\\u9891).*?$", "", RegexOptions.IgnoreCase);
        title = Regex.Replace(title, @"[-_\s]*WeTV.*$", "", RegexOptions.IgnoreCase);
        title = Regex.Replace(title, "[-_\\s]*(\\u54D4\\u54E9\\u54D4\\u54E9).*?$", "", RegexOptions.IgnoreCase);
        title = Regex.Replace(title, "[-_\\s]*(\\u4F18\\u9177).*?$", "", RegexOptions.IgnoreCase);

        // Clean up special characters
        title = Regex.Replace(title, @"[<>:""/\\|?*]", "");
        title = title.Trim();

        return title;
    }

    public string GetValidFileName(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return string.Empty;

        // Remove invalid characters
        var invalidChars = System.IO.Path.GetInvalidFileNameChars();
        foreach (var invalidChar in invalidChars)
        {
            path = path.Replace(invalidChar, '_');
        }

        // Remove invalid characters for path
        var invalidPathChars = System.IO.Path.GetInvalidPathChars();
        foreach (var invalidChar in invalidPathChars)
        {
            path = path.Replace(invalidChar, '_');
        }

        return path.Trim();
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
        _httpClient?.Dispose();
    }
} 

namespace N_m3u8DL_RE_GUI.Services;

/// <summary>
/// Interface for utility operations.
/// </summary>
public interface IUtilityService
{
    /// <summary>
    /// Get title from URL.
    /// </summary>
    /// <param name="url">URL to extract title from</param>
    /// <returns>Extracted title or empty string</returns>
    Task<string> GetTitleFromUrlAsync(string url);

    /// <summary>
    /// Validate and format file path.
    /// </summary>
    /// <param name="path">File path to validate</param>
    /// <returns>Valid file path</returns>
    string GetValidFileName(string path);

    /// <summary>
    /// Select folder dialog.
    /// </summary>
    /// <param name="description">Dialog description</param>
    /// <param name="initialPath">Initial path</param>
    /// <returns>Selected folder path or null if cancelled</returns>
    string? SelectFolder(string description, string? initialPath = null);

    /// <summary>
    /// Check if file exists.
    /// </summary>
    /// <param name="filePath">File path to check</param>
    /// <returns>True if file exists</returns>
    bool FileExists(string filePath);

    /// <summary>
    /// Get file extension.
    /// </summary>
    /// <param name="filePath">File path</param>
    /// <returns>File extension</returns>
    string GetFileExtension(string filePath);
} 
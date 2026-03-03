#nullable enable
namespace N_m3u8DL_RE_GUI.Services;

/// <summary>
/// Interface for drag and drop operations.
/// </summary>
public interface IDragDropService
{
    /// <summary>
    /// Handle file drop event.
    /// </summary>
    /// <param name="data">Drop data</param>
    /// <returns>Processed file path or null</returns>
    string? HandleFileDrop(object data);

    /// <summary>
    /// Check if drop data contains files.
    /// </summary>
    /// <param name="data">Drop data</param>
    /// <returns>True if contains files</returns>
    bool HasFiles(object data);

    /// <summary>
    /// Get file paths from drop data.
    /// </summary>
    /// <param name="data">Drop data</param>
    /// <returns>Array of file paths</returns>
    string[] GetFilePaths(object data);
} 
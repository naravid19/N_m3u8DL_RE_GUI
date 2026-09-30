#nullable enable
using System;
using System.IO;
using System.Text.Json;
using N_m3u8DL_RE_GUI.Core.Resume;

namespace N_m3u8DL_RE_GUI.Services;

/// <summary>
/// Persists and manages the active download job record.
/// Allows recovering interrupted downloads by attaching a fresh link to existing temp segments.
/// </summary>
public class ResumeJobStore
{
    private static readonly Lazy<ResumeJobStore> _default = new(() => new ResumeJobStore());
    public static ResumeJobStore Default => _default.Value;

    private readonly string _recordFilePath;
    private readonly object _lock = new();

    public string RecordFilePath => _recordFilePath;

    public ResumeJobStore(string? recordFilePath = null)
    {
        _recordFilePath = recordFilePath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "N_m3u8DL_RE_GUI",
            "active-job.json");
    }

    /// <summary>
    /// Writes a new active job record. Overwrites any existing record.
    /// Note: Only the hostname is extracted from the URL; credentials/tokens are never persisted.
    /// </summary>
    public ResumeJob? Begin(string? urlOrHost, string saveName, string saveDir, string tmpDir)
    {
        lock (_lock)
        {
            try
            {
                var host = ExtractHost(urlOrHost);
                var job = new ResumeJob(
                    SaveName: saveName?.Trim() ?? string.Empty,
                    SaveDir: saveDir?.Trim() ?? string.Empty,
                    TmpDir: tmpDir?.Trim() ?? string.Empty,
                    SourceHost: host,
                    StartedAt: DateTimeOffset.UtcNow
                );

                var dir = Path.GetDirectoryName(_recordFilePath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                var json = JsonSerializer.Serialize(job, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(_recordFilePath, json);

                return job;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ResumeJobStore] Failed to begin job: {ex.Message}");
                return null;
            }
        }
    }

    /// <summary>
    /// Removes the active job record upon successful completion of the download.
    /// </summary>
    public void Complete()
    {
        lock (_lock)
        {
            try
            {
                if (File.Exists(_recordFilePath))
                {
                    File.Delete(_recordFilePath);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ResumeJobStore] Failed to complete job record: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Checks for an existing interrupted job that has recoverable bytes on disk.
    /// Returns null if no record exists, if the record is corrupt, if the temp folder is missing,
    /// or if the temp folder is empty (in which case the stale record is also cleaned up).
    /// </summary>
    public ResumeJob? TryFindResumable()
    {
        lock (_lock)
        {
            try
            {
                if (!File.Exists(_recordFilePath))
                {
                    return null;
                }

                var json = File.ReadAllText(_recordFilePath);
                var job = JsonSerializer.Deserialize<ResumeJob>(json);
                if (job == null || string.IsNullOrWhiteSpace(job.TmpDir))
                {
                    return null;
                }

                if (!Directory.Exists(job.TmpDir))
                {
                    return null;
                }

                long totalBytes = 0;
                var dirInfo = new DirectoryInfo(job.TmpDir);
                foreach (var file in dirInfo.EnumerateFiles("*", SearchOption.AllDirectories))
                {
                    totalBytes += file.Length;
                }

                if (totalBytes <= 0)
                {
                    // No segments landed before the download stopped; clean up the stale record
                    Complete();
                    return null;
                }

                return job with { ExistingBytes = totalBytes };
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ResumeJobStore] TryFindResumable error: {ex.Message}");
                return null;
            }
        }
    }

    /// <summary>
    /// Discards the interrupted download by deleting the temporary directory files
    /// and removing the job record.
    /// Returns false if deleting the temp directory fails (e.g. due to file locks),
    /// keeping the record intact.
    /// </summary>
    public bool Discard()
    {
        lock (_lock)
        {
            try
            {
                if (!File.Exists(_recordFilePath))
                {
                    return true;
                }

                var json = File.ReadAllText(_recordFilePath);
                var job = JsonSerializer.Deserialize<ResumeJob>(json);

                if (job != null && !string.IsNullOrWhiteSpace(job.TmpDir) && Directory.Exists(job.TmpDir))
                {
                    try
                    {
                        Directory.Delete(job.TmpDir, recursive: true);
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"[ResumeJobStore] Failed to delete tmp directory: {ex.Message}");
                        // Keep the record if file is locked or cannot be deleted
                        return false;
                    }
                }

                Complete();
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ResumeJobStore] Failed to discard job: {ex.Message}");
                return false;
            }
        }
    }

    private static string ExtractHost(string? urlOrHost)
    {
        if (string.IsNullOrWhiteSpace(urlOrHost))
        {
            return string.Empty;
        }

        var trimmed = urlOrHost.Trim();
        if (Uri.TryCreate(trimmed, UriKind.Absolute, out var uri))
        {
            return uri.Host;
        }

        // Fallback: strip any scheme prefix, port, query, or path
        var clean = trimmed;
        var queryIdx = clean.IndexOf('?');
        if (queryIdx >= 0) clean = clean[..queryIdx];

        var slashIdx = clean.IndexOf('/');
        if (slashIdx >= 0) clean = clean[..slashIdx];

        return clean;
    }
}

#nullable enable
using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace N_m3u8DL_RE_GUI.Core.NativeBridge;

/// <summary>
/// A directory of capture payloads waiting to be applied.
///
/// This is deliberately a directory rather than an IPC channel. The native
/// host process and the running GUI are separate processes that may not
/// overlap in time at all, and a directory serves both cases with one
/// mechanism: the host always writes, and whichever GUI is running — or
/// starts next — drains. Because a drain refuses to run while a download is
/// in progress and leaves the file in place, the directory is also the
/// download queue.
/// </summary>
public sealed class CaptureInbox
{
    private const string EntryExtension = ".json";

    public string Directory { get; }

    public CaptureInbox(string? directory = null)
    {
        Directory = directory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "N_m3u8DL_RE_GUI",
            "inbox");
    }

    /// <summary>
    /// Queues a payload. The name is the tick count zero-padded to its full
    /// width followed by a random suffix, so a lexical sort is a chronological
    /// sort and two writes in the same tick cannot collide.
    /// </summary>
    public void Write(string payload)
    {
        System.IO.Directory.CreateDirectory(Directory);

        var name = $"{DateTime.UtcNow.Ticks:D19}-{Guid.NewGuid().ToString("N")[..8]}";
        var finalPath = Path.Combine(Directory, name + EntryExtension);
        var tempPath = Path.Combine(Directory, name + ".tmp");

        var json = JsonSerializer.Serialize(new { payload });
        File.WriteAllText(tempPath, json, new UTF8Encoding(false));

        // Move, not write-in-place: a watcher firing mid-write would otherwise
        // hand the GUI a truncated payload.
        File.Move(tempPath, finalPath);
    }

    public bool HasPending() => EntryPaths().Any();

    /// <summary>
    /// Removes and returns the oldest queued payload, or null when there is
    /// nothing to take. An entry that cannot be read is discarded rather than
    /// left to block the queue forever.
    /// </summary>
    public string? TakeOldest()
    {
        foreach (var path in EntryPaths())
        {
            string? payload = null;
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(path));
                if (doc.RootElement.TryGetProperty("payload", out var value))
                    payload = value.GetString();
            }
            catch (Exception)
            {
                payload = null;
            }

            try { File.Delete(path); } catch (IOException) { }

            if (!string.IsNullOrWhiteSpace(payload))
                return payload;
        }

        return null;
    }

    private string[] EntryPaths()
    {
        try
        {
            var paths = System.IO.Directory.GetFiles(Directory, "*" + EntryExtension);
            Array.Sort(paths, StringComparer.Ordinal);
            return paths;
        }
        catch (DirectoryNotFoundException)
        {
            return Array.Empty<string>();
        }
    }
}

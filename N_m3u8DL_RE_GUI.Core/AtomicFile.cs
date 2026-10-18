#nullable enable
using System;
using System.IO;
using System.Text;

namespace N_m3u8DL_RE_GUI.Core;

/// <summary>
/// Writes a file by filling a temporary sibling first and moving it into
/// place, so a reader never observes a partial file and an interrupted write
/// leaves the previous copy intact.
///
/// File.WriteAllText truncates before it writes. For a cache that is fine —
/// it is rebuilt. For config.json it is not: the secret values there are
/// DPAPI ciphertext, and a truncated file means the user re-enters every
/// header, proxy and key by hand. CaptureInbox and AbyssDownloadService
/// already write this way; this is that convention as one call.
/// </summary>
public static class AtomicFile
{
    public static void WriteAllText(string path, string contents, Encoding? encoding = null)
    {
        if (string.IsNullOrEmpty(path)) throw new ArgumentException("Path is required", nameof(path));
        if (contents is null) throw new ArgumentNullException(nameof(contents));

        var directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        // UTF-8 without a BOM matches what the existing writers emit, so a file
        // saved before this change reads back identically after it.
        encoding ??= new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

        var tempPath = path + ".tmp";
        try
        {
            File.WriteAllText(tempPath, contents, encoding);

            // File.Move with overwrite is atomic on Windows for a same-volume
            // move, which a sibling path always is.
            File.Move(tempPath, path, overwrite: true);
        }
        catch
        {
            TryDelete(tempPath);
            throw;
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch (IOException)
        {
            // Losing a stray .tmp is not worth masking the real failure.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}

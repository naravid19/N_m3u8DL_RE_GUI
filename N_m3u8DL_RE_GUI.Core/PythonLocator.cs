#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace N_m3u8DL_RE_GUI.Core;

/// <summary>
/// Finds a Python interpreter that can <c>import curl_cffi</c>, which the Cloudflare
/// bypass script needs. Candidate order matters: explicit CPython install paths come
/// first (they avoid the Windows Store stub), then conda, then names resolved via PATH.
/// </summary>
public static class PythonLocator
{
    private static readonly string[] CondaFolders = { "anaconda3", "miniconda3", "Anaconda3", "Miniconda3" };
    private static readonly string[] PathNames = { "py", "python", "python3" };

    /// <summary>How long one interpreter gets to answer before it is killed and skipped.</summary>
    public static readonly TimeSpan DefaultProbeTimeout = TimeSpan.FromSeconds(10);

    /// <summary>The first interpreter whose probe exits 0, or null when none does.</summary>
    public static Task<string?> FindWithCurlCffiAsync(CancellationToken cancellationToken = default) =>
        FindWithCurlCffiAsync(
            BuildCandidates(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)),
            DefaultProbeTimeout,
            cancellationToken);

    /// <summary>
    /// Ordered interpreter candidates: CPython installs under Program Files and
    /// %LOCALAPPDATA%\Programs\Python, conda installs in the user profile that actually
    /// exist, then the bare launcher names.
    /// </summary>
    public static List<string> BuildCandidates(string? localAppData, string? programFiles, string? programFilesX86, string? userProfile)
    {
        var candidates = new List<string>();

        foreach (var pythonRoot in new[]
                 {
                     Combine(programFiles, "Python"),
                     Combine(programFilesX86, "Python"),
                     Combine(localAppData, "Programs", "Python"),
                 })
        {
            AddInterpreters(candidates, pythonRoot);
        }

        if (!string.IsNullOrEmpty(userProfile))
        {
            foreach (var folder in CondaFolders)
            {
                var conda = Path.Combine(userProfile, folder, "python.exe");
                if (File.Exists(conda))
                    candidates.Add(conda);
            }
        }

        candidates.AddRange(PathNames);

        // Windows paths are case-insensitive, so "miniconda3" and "Miniconda3" are one install.
        return candidates.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    internal static async Task<string?> FindWithCurlCffiAsync(IEnumerable<string> candidates, TimeSpan probeTimeout, CancellationToken cancellationToken)
    {
        foreach (var candidate in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                // Skip full paths that don't exist on disk.
                if (candidate.IndexOf(Path.DirectorySeparatorChar) >= 0 && !File.Exists(candidate))
                    continue;

                if (await ProbeAsync(candidate, probeTimeout, cancellationToken).ConfigureAwait(false))
                    return candidate;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                // Expected for most candidates (not installed, Store stub); the trace
                // is what explains "No Python interpreter with curl_cffi found".
                Trace.WriteLine($"Python probe '{candidate}' failed: {ex.Message}");
            }
        }

        return null;
    }

    private static async Task<bool> ProbeAsync(string interpreter, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var psi = new ProcessStartInfo(interpreter, "-c \"import curl_cffi\"")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        using var process = Process.Start(psi);
        if (process is null)
            return false;

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(timeout);

        var outTask = process.StandardOutput.ReadToEndAsync(timeoutCts.Token);
        var errTask = process.StandardError.ReadToEndAsync(timeoutCts.Token);

        try
        {
            await process.WaitForExitAsync(timeoutCts.Token).ConfigureAwait(false);
            await Task.WhenAll(outTask, errTask).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); } catch { /* already gone */ }
            if (cancellationToken.IsCancellationRequested)
                throw;
            return false; // timed out
        }

        return process.ExitCode == 0;
    }

    private static string? Combine(string? root, params string[] parts) =>
        string.IsNullOrEmpty(root) ? null : Path.Combine(root, Path.Combine(parts));

    private static void AddInterpreters(List<string> candidates, string? pythonRoot)
    {
        try
        {
            if (pythonRoot is not null && Directory.Exists(pythonRoot))
                foreach (var directory in Directory.GetDirectories(pythonRoot))
                    candidates.Add(Path.Combine(directory, "python.exe"));
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"Could not list Python installs under '{pythonRoot}': {ex.Message}");
        }
    }
}

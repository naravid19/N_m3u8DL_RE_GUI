#nullable enable
using N_m3u8DL_RE_GUI.Core;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace N_m3u8DL_RE_GUI.Services;

/// <summary>
/// Implementation of download service using N_m3u8DL-RE executable or arbitrary processes.
/// Owns process lifecycle and process-tree cancellation safety.
/// </summary>
public class DownloadService : IDownloadService
{
    private Process? _currentProcess;
    private CancellationTokenSource? _cancellationTokenSource;
    private readonly object _lockObject = new();

    private static bool SafeIsRunning(Process? process)
    {
        if (process == null) return false;
        try
        {
            return !process.HasExited;
        }
        catch
        {
            return false;
        }
    }

    public bool IsDownloading
    {
        get
        {
            lock (_lockObject)
            {
                return SafeIsRunning(_currentProcess);
            }
        }
    }

    public async Task<bool> StartDownloadAsync(
        DownloadOptions options,
        IProgress<int>? progressCallback = null,
        Action<string>? logCallback = null,
        CancellationToken cancellationToken = default)
    {
        if (IsDownloading)
        {
            logCallback?.Invoke("Download is already in progress. Please wait for it to complete.");
            return false;
        }

        if (string.IsNullOrWhiteSpace(options.Input))
        {
            logCallback?.Invoke("Please enter a URL to download.");
            return false;
        }

        var exePath = string.IsNullOrWhiteSpace(options.ExePath) ? "N_m3u8DL-RE.exe" : options.ExePath;
        if (!System.IO.File.Exists(exePath))
        {
            logCallback?.Invoke($"File not found: {exePath}");
            logCallback?.Invoke("Please download N_m3u8DL-RE.exe from: https://github.com/nilaoda/N_m3u8DL-RE/releases");
            return false;
        }

        var startedUtc = DateTime.UtcNow;

        // Orchestration loop: direct engine attempts, then an optional CF-bypass
        // fallback when the engine's failures look like TLS-fingerprint blocking.
        // Every failed attempt leaves its segments in the temp dir, so each retry
        // only fetches what is still missing.
        var maxEngineAttempts = 1 + Math.Max(0, options.AutoRetryCount);
        EngineRunResult result = new(false, ConsoleOutputParser.EngineOutcome.None);

        for (var attempt = 1; attempt <= maxEngineAttempts; attempt++)
        {
            if (attempt > 1)
            {
                var delay = TimeSpan.FromSeconds(Math.Min(30, 5 * (attempt - 1)));
                logCallback?.Invoke($"Retry {attempt - 1}/{maxEngineAttempts - 1} in {delay.TotalSeconds:0}s — existing segments are reused.");
                try { await Task.Delay(delay, cancellationToken); }
                catch (OperationCanceledException) { return false; }
            }

            logCallback?.Invoke(attempt == 1
                ? "Starting download..."
                : $"Download attempt {attempt} of {maxEngineAttempts}...");
            var args = ArgsBuilder.Build(options);
            logCallback?.Invoke($"Command: {exePath} {args}");

            var startInfo = new ProcessStartInfo
            {
                FileName = exePath,
                Arguments = args,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                // N_m3u8DL-RE writes localized text in the system ANSI code page when its
                // output is redirected (no console); decoding as UTF-8 garbles Chinese text.
                StandardOutputEncoding = TextEncodingDetector.AnsiFallback,
                StandardErrorEncoding = TextEncodingDetector.AnsiFallback
            };

            result = await StartTrackedProcessWithOutcomeAsync(startInfo, logCallback, progressCallback, cancellationToken);
            if (result.Success)
                return true;

            if (result.Outcome != ConsoleOutputParser.EngineOutcome.FatalError
                && result.Outcome != ConsoleOutputParser.EngineOutcome.HttpBlocked
                && !result.Incomplete)
                break; // cancelled or crashed — retrying will not help
        }

        // CF-bypass fallback: 404/403 from a CF-protected CDN usually means the
        // engine's TLS fingerprint was blocked, not that segments are gone.
        if (options.AutoCfFallback
            && (result.Outcome == ConsoleOutputParser.EngineOutcome.HttpBlocked
                || result.Outcome == ConsoleOutputParser.EngineOutcome.FatalError
                || result.Incomplete))
        {
            logCallback?.Invoke("Engine failed with HTTP-blocking symptoms — trying the Cloudflare bypass path (browser TLS fingerprint) once.");
            var cfOk = await TryCfFallbackAsync(options, logCallback, progressCallback, cancellationToken);
            if (cfOk)
                return true;
        }

        // Last resort: merge what we have when the user tolerates missing segments.
        if (options.AllowMissingSegments)
        {
            var merged = TryMergePartialSegments(options, startedUtc, logCallback);
            if (merged != null)
            {
                progressCallback?.Report(100);
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// One engine (or fallback) run's outcome: overall success, the most severe failure
    /// signature observed in its output, and whether the video bar was left short of its
    /// segment total (which needs a retry even though the engine logged no failure).
    /// </summary>
    private readonly record struct EngineRunResult(
        bool Success,
        ConsoleOutputParser.EngineOutcome Outcome,
        bool Incomplete = false);

    /// <summary>
    /// Runs m3u8_cf_bypass.py once through the existing batch wrapper path. Kept
    /// minimal: the CF script manages its own segment reuse via --seg-dir.
    /// </summary>
    private async Task<bool> TryCfFallbackAsync(
        DownloadOptions options,
        Action<string>? logCallback,
        IProgress<int>? progressCallback,
        CancellationToken cancellationToken)
    {
        var scriptPath = FindScriptPath("m3u8_cf_bypass.py");
        if (scriptPath == null)
        {
            logCallback?.Invoke("m3u8_cf_bypass.py not found next to the GUI executable — cannot attempt the CF-bypass fallback.");
            return false;
        }

        var python = FindPythonExecutable();
        if (python == null)
        {
            logCallback?.Invoke("No Python with curl_cffi found — cannot attempt the CF-bypass fallback. Install it via: pip install curl_cffi");
            return false;
        }

        var workDir = string.IsNullOrWhiteSpace(options.SaveDir) ? Environment.CurrentDirectory : options.SaveDir;
        var saveName = string.IsNullOrWhiteSpace(options.SaveName) ? "output" : options.SaveName!;
        if (!saveName.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase))
            saveName += ".mp4";

        var cfOptions = new CfCommandOptions(
            PythonExe: python,
            ScriptPath: scriptPath,
            Url: options.Input!,
            OutputName: saveName,
            WorkDir: workDir,
            SegDir: Path.Combine(AppContext.BaseDirectory, "cf_segments"),
            Referer: CfCommandBuilder.DeriveReferer(null, options.Input ?? string.Empty),
            Cookie: string.Empty,
            Impersonate: "chrome",
            KeepSegments: true);

        var command = CfCommandBuilder.BuildCommand(cfOptions);
        var bat = Path.Combine(Path.GetTempPath(), "cf_dl_" + DateTime.Now.ToString("yyyyMMddHHmmss") + ".bat");
        System.IO.File.WriteAllText(bat, CfCommandBuilder.BuildBatchScript(command), new UTF8Encoding(false));

        return await StartProcessAsync(bat, string.Empty, logCallback, progressCallback, cancellationToken);
    }

    private static string? FindScriptPath(string scriptName)
    {
        var candidate = Path.Combine(AppContext.BaseDirectory, scriptName);
        if (System.IO.File.Exists(candidate))
            return candidate;
        candidate = Path.Combine(Environment.CurrentDirectory, scriptName);
        return System.IO.File.Exists(candidate) ? candidate : null;
    }

    private static string? FindPythonExecutable()
    {
        // The GUI's interactive CF path probes interpreters in depth; the automatic
        // fallback keeps it simple and only trusts a real python on PATH.
        foreach (var name in new[] { "python", "python3", "py" })
        {
            try
            {
                using var probe = Process.Start(new ProcessStartInfo
                {
                    FileName = name,
                    Arguments = "-c \"import curl_cffi\"",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                });
                if (probe == null)
                    continue;
                if (!probe.WaitForExit(15000))
                {
                    try { probe.Kill(); } catch { }
                    continue;
                }
                if (probe.ExitCode == 0)
                    return name;
            }
            catch { }
        }

        return null;
    }

    /// <summary>
    /// Merges whatever segments the finished (failed) run left in the temp directory
    /// into an output file. Returns the output path, or null when nothing useful is
    /// on disk. The result may contain gaps where segments never arrived.
    /// </summary>
    private static string? TryMergePartialSegments(
        DownloadOptions options,
        DateTime startedUtc,
        Action<string>? logCallback)
    {
        var saveDir = string.IsNullOrWhiteSpace(options.SaveDir) ? Environment.CurrentDirectory : options.SaveDir!;
        var tmpDir = SegmentAuditService.ResolveTmpDir(options.TmpDir, saveDir, startedUtc);
        if (tmpDir == null)
        {
            logCallback?.Invoke("No temp directory found — cannot merge partial segments.");
            return null;
        }

        var audit = SegmentAuditService.Audit(tmpDir);
        if (audit == null || audit.PresentCount == 0)
        {
            logCallback?.Invoke("No downloaded segments found — nothing to merge.");
            return null;
        }

        logCallback?.Invoke(
            $"Segment audit: {audit.PresentCount}/{audit.ManifestCount} segments on disk " +
            $"({audit.MissingIndexes.Count} missing: {SummaryOf(audit.MissingIndexes)}).");

        var ffmpeg = string.IsNullOrWhiteSpace(options.FFmpegBinaryPath)
            ? FindScriptPath("ffmpeg.exe") ?? "ffmpeg.exe"
            : options.FFmpegBinaryPath!;
        if (!System.IO.File.Exists(ffmpeg))
        {
            logCallback?.Invoke($"ffmpeg not found at '{ffmpeg}' — cannot merge partial segments.");
            return null;
        }

        var saveName = string.IsNullOrWhiteSpace(options.SaveName) ? "partial_" + DateTime.Now.ToString("yyyyMMddHHmmss") : options.SaveName!;
        if (!saveName.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase))
            saveName += ".mp4";
        var outputPath = Path.Combine(saveDir, saveName);
        var uniquePath = UniquePath(outputPath);

        var concatList = Path.Combine(Path.GetTempPath(), "nre_partial_" + Guid.NewGuid().ToString("N")[..8] + ".txt");
        if (SegmentMergeService.BuildConcatList(audit.SegmentsDir, concatList) == null)
        {
            logCallback?.Invoke("Failed to build the concat list — cannot merge partial segments.");
            return null;
        }

        logCallback?.Invoke($"Merging {audit.PresentCount} segments into '{uniquePath}' (gaps possible)...");
        try
        {
            using var merge = Process.Start(new ProcessStartInfo
            {
                FileName = ffmpeg,
                Arguments = $"-hide_banner -loglevel error -y -f concat -safe 0 -i \"{concatList}\" -c copy \"{uniquePath}\"",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            });
            if (merge == null || !merge.WaitForExit(600000) || merge.ExitCode != 0)
            {
                logCallback?.Invoke("ffmpeg merge failed — see the engine log for details.");
                return null;
            }
        }
        catch (Exception ex)
        {
            logCallback?.Invoke($"ffmpeg merge error: {ex.Message}");
            return null;
        }
        finally
        {
            try { System.IO.File.Delete(concatList); } catch { }
        }

        logCallback?.Invoke($"Partial merge finished: {uniquePath}");
        return uniquePath;
    }

    private static string SummaryOf(IReadOnlyList<int> missing)
    {
        if (missing.Count <= 8)
            return string.Join(", ", missing);
        var head = new List<int>();
        for (var i = 0; i < 8; i++)
            head.Add(missing[i]);
        return string.Join(", ", head) + ", …";
    }

    private static string UniquePath(string path)
    {
        if (!System.IO.File.Exists(path))
            return path;
        var dir = Path.GetDirectoryName(path) ?? ".";
        var stem = Path.GetFileNameWithoutExtension(path);
        var ext = Path.GetExtension(path);
        for (var i = 1; i < 1000; i++)
        {
            var candidate = Path.Combine(dir, $"{stem}_{i}{ext}");
            if (!System.IO.File.Exists(candidate))
                return candidate;
        }
        return path;
    }

    public async Task<bool> StartProcessAsync(
        string fileName,
        string arguments,
        Action<string>? logCallback = null,
        IProgress<int>? progressCallback = null,
        CancellationToken cancellationToken = default)
    {
        if (IsDownloading)
        {
            logCallback?.Invoke("A process is already in progress. Please wait for it to complete.");
            return false;
        }

        if (string.IsNullOrWhiteSpace(fileName))
        {
            logCallback?.Invoke("Process target file path is required.");
            return false;
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = TextEncodingDetector.AnsiFallback,
            StandardErrorEncoding = TextEncodingDetector.AnsiFallback
        };

        var result = await StartTrackedProcessAsync(startInfo, logCallback, progressCallback, redirect: true, cancellationToken);
        return result.Success;
    }

    private async Task<EngineRunResult> StartTrackedProcessAsync(
        ProcessStartInfo startInfo,
        Action<string>? logCallback,
        IProgress<int>? progressCallback,
        bool redirect,
        CancellationToken cancellationToken)
    {
        Process? process = null;
        CancellationTokenSource? cts = null;

        lock (_lockObject)
        {
            if (SafeIsRunning(_currentProcess))
            {
                logCallback?.Invoke("A process is already in progress. Please wait for it to complete.");
                return new EngineRunResult(false, ConsoleOutputParser.EngineOutcome.None);
            }

            cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _cancellationTokenSource = cts;

            process = new Process { StartInfo = startInfo };
            _currentProcess = process;
        }

        try
        {
            var forwarder = new OutputForwarder(logCallback, progressCallback);

            if (!process.Start())
            {
                logCallback?.Invoke($"Failed to start process: {startInfo.FileName}");
                return new EngineRunResult(false, ConsoleOutputParser.EngineOutcome.None);
            }

            if (redirect)
            {
                // N_m3u8DL-RE's redirected progress frames contain no newline at all, so
                // BeginOutputReadLine never fires until the process exits. Pump the raw
                // byte streams manually and classify each chunk via the parser.
                var pumpOut = PumpStreamAsync(process.StandardOutput.BaseStream, forwarder, cts.Token);
                var pumpErr = PumpStreamAsync(process.StandardError.BaseStream, forwarder, cts.Token);
                try
                {
                    await process.WaitForExitAsync(cts.Token);
                    await Task.WhenAll(pumpOut, pumpErr);
                }
                catch (OperationCanceledException)
                {
                    logCallback?.Invoke("Process execution was cancelled.");
                    return new EngineRunResult(false, ConsoleOutputParser.EngineOutcome.None);
                }
                forwarder.FlushPending();
            }
            else
            {
                try
                {
                    await process.WaitForExitAsync(cts.Token);
                }
                catch (OperationCanceledException)
                {
                    logCallback?.Invoke("Process execution was cancelled.");
                    return new EngineRunResult(false, ConsoleOutputParser.EngineOutcome.None);
                }
            }

            // N_m3u8DL-RE (Beta) exits 0 even when the download failed, so the exit code
            // alone is not evidence: a failure signature OR an incomplete video bar both
            // mean the run did not succeed.
            var outcome = forwarder.Outcome;
            var incomplete = forwarder.VideoSegmentsIncomplete;
            var success = process.ExitCode == 0
                && outcome == ConsoleOutputParser.EngineOutcome.None
                && !incomplete;

            logCallback?.Invoke(success
                ? "Process finished successfully!"
                : process.ExitCode != 0
                    ? $"Process exited with code: {process.ExitCode}"
                    : outcome != ConsoleOutputParser.EngineOutcome.None
                        ? $"Process failed (engine reported {outcome}) but exited with code 0."
                        : "Process failed: the engine exited with code 0 but left segments undownloaded.");

            if (success)
                progressCallback?.Report(100);

            return new EngineRunResult(success, outcome, incomplete);
        }
        catch (OperationCanceledException)
        {
            logCallback?.Invoke("Process execution was cancelled.");
            return new EngineRunResult(false, ConsoleOutputParser.EngineOutcome.None);
        }
        catch (Exception ex)
        {
            logCallback?.Invoke($"Process execution error: {ex.Message}");
            return new EngineRunResult(false, ConsoleOutputParser.EngineOutcome.None);
        }
        finally
        {
            lock (_lockObject)
            {
                if (_currentProcess == process) _currentProcess = null;
                if (_cancellationTokenSource == cts) _cancellationTokenSource = null;
            }

            if (process != null)
            {
                try
                {
                    if (!process.HasExited)
                        process.Kill(entireProcessTree: true);
                }
                catch { }
                try { process.Dispose(); } catch { }
            }

            try { cts?.Dispose(); } catch { }
        }
    }

    private Task<EngineRunResult> StartTrackedProcessWithOutcomeAsync(
        ProcessStartInfo startInfo,
        Action<string>? logCallback,
        IProgress<int>? progressCallback,
        CancellationToken cancellationToken)
    {
        return StartTrackedProcessAsync(startInfo, logCallback, progressCallback, redirect: true, cancellationToken);
    }

    /// <summary>
    /// Reads a redirected stream in chunks, decodes it and hands the text to the
    /// forwarder. Uses the raw byte stream because BeginOutputReadLine only fires on
    /// LF and N_m3u8DL-RE progress frames contain none. Encoding: the engine writes
    /// localized text in the system ANSI code page when output is not a console.
    /// </summary>
    private static async Task PumpStreamAsync(Stream stream, OutputForwarder forwarder, CancellationToken token)
    {
        var buffer = new byte[8192];
        var decodeBuffer = new char[8192];
        var encoding = TextEncodingDetector.AnsiFallback;
        // Stateful decoder: multibyte ANSI characters (GBK) can straddle chunk boundaries.
        var decoder = encoding.GetDecoder();

        while (true)
        {
            try
            {
                var read = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), token);
                if (read <= 0)
                    break;

                // Decoder.Convert safely carries partial multibyte sequences across chunks;
                // plain GetChars throws when the output buffer is undersized.
                decoder.Convert(buffer, 0, read, decodeBuffer, 0, decodeBuffer.Length, flush: false,
                    out _, out var charCount, out _);
                forwarder.AcceptChunk(decodeBuffer, charCount);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception)
            {
                break; // stream closed (process killed)
            }
        }
    }

    /// <summary>
    /// Turns raw engine output into GUI log entries and progress reports. Each pump
    /// chunk is split by <see cref="ConsoleOutputParser.SplitOutput"/>; segment types
    /// drive the behaviour: complete records are emitted to the log immediately,
    /// progress frames are collapsed to at most one snapshot per interval (progress
    /// bar still updates on every change), and an unterminated tail fragment is held
    /// in the pending buffer until the next chunk completes it or the stream ends.
    /// </summary>
    private sealed class OutputForwarder
    {
        private const int ProgressThrottleMs = 500;

        private readonly Action<string>? _logCallback;
        private readonly IProgress<int>? _progressCallback;
        private readonly object _lock = new();
        private readonly StringBuilder _pending = new();
        private DateTime _lastProgressLog = DateTime.MinValue;
        private int _lastReportedPercent = -1;
        private int? _lastShownDone;
        private string? _lastProgressSignature;
        private ConsoleOutputParser.EngineOutcome _outcome = ConsoleOutputParser.EngineOutcome.None;
        private (int Done, int Total)? _lastVideoCount;

        // Repetitive-warning suppression: ffmpeg merge phases can emit thousands of
        // identical "Packet corrupt" lines in seconds, drowning the rest of the log.
        // ffmpeg alternates two shapes per bad packet ("Packet corrupt (stream = 0,
        // dts = X)." / "corrupt input packet in stream 0"), so the state holds two
        // slots — one per alternating shape — that share one sliding burst window.
        private sealed class SuppressionSlot
        {
            public string? Key;
            public int Count;
        }

        private readonly SuppressionSlot[] _suppressionSlots = new SuppressionSlot[2];
        private DateTime _burstLastEmit = DateTime.MinValue;

        public OutputForwarder(Action<string>? logCallback, IProgress<int>? progressCallback)
        {
            _logCallback = logCallback;
            _progressCallback = progressCallback;
        }

        /// <summary>
        /// The most severe failure signature observed in the engine output. N_m3u8DL-RE
        /// (Beta) exits 0 even on failure, so this supplements the exit code.
        /// </summary>
        public ConsoleOutputParser.EngineOutcome Outcome
        {
            get { lock (_lock) return _outcome; }
        }

        /// <summary>
        /// True when the run left the video bar short of its segment total, i.e. the
        /// engine exited 0 without ever logging a failure signature. Guards against a
        /// silently truncated download. False when no video bar was ever seen.
        /// </summary>
        public bool VideoSegmentsIncomplete
        {
            get { lock (_lock) return _lastVideoCount is { } c && c.Done < c.Total; }
        }

        /// <summary>Accepts decoded characters from a pump chunk. Thread-safe.</summary>
        public void AcceptChunk(char[] chars, int count)
        {
            List<string> toEmit = new();
            lock (_lock)
            {
                _pending.Append(chars, 0, count);
                var text = _pending.ToString();
                _pending.Clear();

                var segments = ConsoleOutputParser.SplitOutput(text);
                for (var i = 0; i < segments.Count; i++)
                {
                    var segment = segments[i];
                    var isLast = i == segments.Count - 1;

                    if (isLast && !segment.IsComplete)
                    {
                        // Unterminated fragment — hold it until the next chunk completes it.
                        _pending.Append(segment.Text);
                        continue;
                    }

                    if (segment.IsProgress)
                        HandleProgressFrameLocked(segment.Text, toEmit);
                    else
                        EmitRecordLocked(segment.Text, toEmit);
                }
            }

            // Invoked outside the lock so log handlers can re-enter safely.
            Emit(toEmit);
        }

        /// <summary>Callers must hold _lock.</summary>
        private void EmitRecordLocked(string record, List<string> toEmit)
        {
            foreach (var cleaned in ConsoleOutputParser.SplitGluedLogRecords(record))
            {
                var entry = ConsoleOutputParser.Clean(cleaned);
                if (entry.Length > 0)
                {
                    var outcome = ConsoleOutputParser.ClassifyOutcome(entry);
                    if (outcome > _outcome)
                        _outcome = outcome;

                    var percent = ConsoleOutputParser.TryExtractPercent(entry);
                    if (percent.HasValue && percent.Value != _lastReportedPercent)
                    {
                        _lastReportedPercent = percent.Value;
                        _progressCallback?.Report(percent.Value);
                    }

                    entry = SuppressRepeatLocked(entry, toEmit);
                    if (entry != null)
                        toEmit.Add(entry);
                }
            }
        }

        /// <summary>
        /// Collapses bursts of same-shaped, timestamped log records (ffmpeg's "Packet
        /// corrupt" storm during the merge phase) into one line per burst, plus a
        /// running suppression count. Comparison uses the skeleton key (stamp and
        /// volatile numbers stripped). ffmpeg alternates two shapes per bad packet, so
        /// the state holds two key slots sharing one sliding 500ms burst window — the
        /// pair A/B/A/B is treated as one burst, not four shape switches, and each
        /// suppressed member extends the window so a sustained storm stays collapsed.
        /// Slot counts hold *suppressed* members (0 on the burst's visible first
        /// record); the flush merges every slot into a single total line. Returns the
        /// entry to log, or null when suppressed. Callers must hold _lock.
        /// </summary>
        private string? SuppressRepeatLocked(string entry, List<string> toEmit)
        {
            var key = ConsoleOutputParser.RepetitionKey(entry);
            if (key == null)
                return entry;

            var now = DateTime.Now;
            var inWindow = (now - _burstLastEmit).TotalMilliseconds < ProgressThrottleMs;

            // Find this shape's slot; remember at most two shapes per burst.
            SuppressionSlot? slot = null;
            foreach (var s in _suppressionSlots)
            {
                if (s != null && string.Equals(s.Key, key, StringComparison.Ordinal))
                {
                    slot = s;
                    break;
                }
            }

            if (slot == null)
            {
                var emptyIndex = Array.FindIndex(_suppressionSlots, s => s == null || s.Count == 0);
                if (emptyIndex < 0)
                {
                    emptyIndex = 0; // third distinct shape: evict the oldest slot
                }
                slot = _suppressionSlots[emptyIndex] ??= new SuppressionSlot();
                slot.Key = key;
            }

            if (inWindow)
            {
                // Suppress and count; the held-back total is flushed when the storm
                // finally pauses (or the stream ends via FlushPending). The window
                // slides with every suppressed member.
                slot.Count++;
                _burstLastEmit = now;
                return null;
            }

            // Window elapsed: flush what the previous burst held back, then start a
            // new burst with this record as its visible representative.
            FlushSuppressionLocked(toEmit);
            slot.Count = 0;
            _burstLastEmit = now;
            return entry;
        }

        /// <summary>Callers must hold _lock. Emits the held-back total and resets slots.</summary>
        private void FlushSuppressionLocked(List<string> toEmit)
        {
            var total = 0;
            foreach (var s in _suppressionSlots)
            {
                if (s != null)
                {
                    total += s.Count;
                    s.Count = 0;
                }
            }

            if (total > 0)
                toEmit.Add($"… {total} similar messages suppressed.");
        }

        /// <summary>
        /// Callers must hold _lock. Collects throttled progress entries into
        /// <paramref name="toEmit"/>. When the throttle window skipped segment counts
        /// since the last shown frame, the newest row is annotated with the jump
        /// ("12/101 (+9)") so the sequence stays semantically continuous instead of
        /// silently jumping 12→21.
        /// </summary>
        private void HandleProgressFrameLocked(string frame, List<string> toEmit)
        {
            // Failure signatures can be glued onto the tail of a progress frame when the
            // engine omits the newline between them. Classify here too, otherwise the
            // whole run is written off as successful.
            var frameOutcome = ConsoleOutputParser.ClassifyOutcome(ConsoleOutputParser.Clean(frame));
            if (frameOutcome > _outcome)
                _outcome = frameOutcome;

            var videoCount = ConsoleOutputParser.TryExtractVideoSegmentCount(frame);
            if (videoCount.HasValue)
                _lastVideoCount = videoCount;

            var percent = ConsoleOutputParser.TryExtractPercent(frame);
            if (percent.HasValue && percent.Value != _lastReportedPercent)
            {
                _lastReportedPercent = percent.Value;
                _progressCallback?.Report(percent.Value);
            }

            if ((DateTime.Now - _lastProgressLog).TotalMilliseconds < ProgressThrottleMs)
                return;

            var count = ConsoleOutputParser.TryExtractSegmentCount(frame);
            var jump = 0;
            if (count.HasValue)
            {
                if (_lastShownDone.HasValue && count.Value.Done > _lastShownDone.Value)
                    jump = count.Value.Done - _lastShownDone.Value;
                _lastShownDone = count.Value.Done;
            }

            var rows = ConsoleOutputParser.SplitProgressFrame(frame);
            var cleanedRows = new List<string>(rows.Count);
            for (var i = 0; i < rows.Count; i++)
            {
                var cleaned = ConsoleOutputParser.RepairFieldSpacing(ConsoleOutputParser.Clean(rows[i]));
                if (cleaned.Length == 0)
                    continue;

                // Annotate only the video row (the one carrying done/total) and only
                // when the throttle actually skipped work.
                if (jump > 0 && count.HasValue && i == VideoRowIndex(rows, count.Value))
                    cleaned = $"{cleaned} (+{jump} since last)";
                cleanedRows.Add(cleaned);
            }

            // The engine keeps redrawing identical frames after the download completes
            // (100% idle during the ffmpeg merge). Skip emitting a frame identical to
            // the previously shown one, so the log does not fill with "1268/1268 100%"
            // repeats — real changes (speed, ETA) always differ somewhere.
            var signature = string.Join("\n", cleanedRows);
            if (cleanedRows.Count > 0 && string.Equals(signature, _lastProgressSignature, StringComparison.Ordinal))
                return;

            _lastProgressSignature = signature;
            _lastProgressLog = DateTime.Now;
            toEmit.AddRange(cleanedRows);
        }

        /// <summary>Index of the row holding the video bar's done/total counters.</summary>
        private static int VideoRowIndex(List<string> rows, (int Done, int Total) count)
        {
            for (var i = 0; i < rows.Count; i++)
            {
                var row = ConsoleOutputParser.TryExtractSegmentCount(rows[i]);
                if (row.HasValue && row.Value.Done == count.Done && row.Value.Total == count.Total)
                    return i;
            }
            return -1;
        }

        /// <summary>
        /// Emits whatever is still pending after the stream ends: the newest throttled
        /// progress frame and any unterminated tail fragment.
        /// </summary>
        public void FlushPending()
        {
            List<string> lines;
            lock (_lock)
            {
                lines = new List<string>();
                if (_pending.Length > 0)
                {
                    // The tail fragment can still carry the terminal failure record.
                    var tailOutcome = ConsoleOutputParser.ClassifyOutcome(ConsoleOutputParser.Clean(_pending.ToString()));
                    if (tailOutcome > _outcome)
                        _outcome = tailOutcome;

                    var tailCount = ConsoleOutputParser.TryExtractVideoSegmentCount(_pending.ToString());
                    if (tailCount.HasValue)
                        _lastVideoCount = tailCount;

                    foreach (var row in ConsoleOutputParser.SplitProgressFrame(_pending.ToString()))
                    {
                        var cleaned = ConsoleOutputParser.RepairFieldSpacing(ConsoleOutputParser.Clean(row));
                        if (cleaned.Length > 0)
                            lines.Add(cleaned);
                    }
                    _pending.Clear();
                }

                // Report any burst that was still being suppressed when the stream ended.
                FlushSuppressionLocked(lines);
            }

            Emit(lines);
        }

        private void Emit(List<string> entries)
        {
            foreach (var entry in entries)
                _logCallback?.Invoke(entry);
        }
    }

    public void StopDownload()
    {
        Process? procToKill = null;
        CancellationTokenSource? ctsToCancel = null;

        lock (_lockObject)
        {
            procToKill = _currentProcess;
            ctsToCancel = _cancellationTokenSource;
        }

        if (ctsToCancel != null)
        {
            try
            {
                ctsToCancel.Cancel();
            }
            catch (ObjectDisposedException) { }
            catch (Exception ex)
            {
                Debug.WriteLine($"Token cancel error: {ex.Message}");
            }
        }

        if (procToKill != null)
        {
            try
            {
                if (SafeIsRunning(procToKill))
                {
                    // Kill the entire process tree to also terminate child processes
                    // (ffmpeg, mp4decrypt, python, etc.)
                    procToKill.Kill(entireProcessTree: true);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to stop process tree: {ex.Message}");
            }
        }
    }
}

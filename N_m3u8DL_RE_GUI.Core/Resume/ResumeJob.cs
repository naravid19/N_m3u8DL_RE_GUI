#nullable enable
using System;

namespace N_m3u8DL_RE_GUI.Core.Resume;

/// <summary>
/// Represents metadata for an active or interrupted download job.
/// Note: Stream URLs are intentionally never stored here to protect credentials
/// and avoid expired signed tokens.
/// </summary>
public record ResumeJob(
    string SaveName,
    string SaveDir,
    string TmpDir,
    string SourceHost,
    DateTimeOffset StartedAt,
    long ExistingBytes = 0
);

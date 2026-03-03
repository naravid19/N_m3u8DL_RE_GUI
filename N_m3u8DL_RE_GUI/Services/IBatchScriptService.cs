#nullable enable
using System;
using System.Threading;
using System.Threading.Tasks;

namespace N_m3u8DL_RE_GUI.Services;

/// <summary>
/// Creates and persists batch scripts for batch download mode.
/// </summary>
public interface IBatchScriptService
{
    bool IsBatchInput(string inputPath);

    Task<BatchScriptBuildResult> BuildScriptAsync(
        string inputPath,
        string exePath,
        Func<string, Task<string>> resolveTitleAsync,
        Func<string, string> buildArgsForInput,
        Action<string>? onTitleResolved = null,
        CancellationToken cancellationToken = default);

    void SaveScript(string filePath, string content);
}

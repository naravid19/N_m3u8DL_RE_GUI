#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using N_m3u8DL_RE_GUI.Core;

namespace N_m3u8DL_RE_GUI.Services;

/// <summary>
/// Default implementation for batch script generation.
/// </summary>
public class BatchScriptService : IBatchScriptService
{
    public bool IsBatchInput(string inputPath)
    {
        if (string.IsNullOrWhiteSpace(inputPath))
            return false;

        return (!inputPath.StartsWith("http", StringComparison.OrdinalIgnoreCase) &&
                inputPath.EndsWith(".txt", StringComparison.OrdinalIgnoreCase) &&
                File.Exists(inputPath))
            || Directory.Exists(inputPath);
    }

    public async Task<BatchScriptBuildResult> BuildScriptAsync(
        string inputPath,
        string exePath,
        Func<string, Task<string>> resolveTitleAsync,
        Func<string, string> buildArgsForInput,
        Action<string>? onTitleResolved = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(inputPath))
            throw new ArgumentException("Input path is required.", nameof(inputPath));
        if (string.IsNullOrWhiteSpace(exePath))
            throw new ArgumentException("Executable path is required.", nameof(exePath));

        var lines = Directory.Exists(inputPath)
            ? BuildFromDirectory(inputPath, exePath, buildArgsForInput, onTitleResolved, cancellationToken)
            : await BuildFromTextFileAsync(inputPath, exePath, resolveTitleAsync, buildArgsForInput, onTitleResolved, cancellationToken);

        var filePath = "Batch-" + DateTime.Now.ToString("yyyy.MM.dd-HH.mm.ss") + ".bat";
        return new BatchScriptBuildResult(filePath, lines);
    }

    public void SaveScript(string filePath, string content)
    {
        File.WriteAllText(filePath, content, new UTF8Encoding(false));
    }

    private static string BuildFromDirectory(
        string inputDirectory,
        string exePath,
        Func<string, string> buildArgsForInput,
        Action<string>? onTitleResolved,
        CancellationToken cancellationToken)
    {
        var items = Directory
            .GetFiles(inputDirectory)
            .Where(file =>
            {
                var extension = Path.GetExtension(file);
                return extension.Equals(".m3u8", StringComparison.OrdinalIgnoreCase) ||
                       extension.Equals(".mpd", StringComparison.OrdinalIgnoreCase);
            })
            .OrderBy(file => file, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var builder = new StringBuilder();
        builder.AppendLine("@echo off");
        builder.AppendLine("::Created by N_m3u8DL_RE_GUI\r\n");
        var index = 0;
        foreach (var item in items)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var title = Path.GetFileNameWithoutExtension(item);
            if (string.IsNullOrWhiteSpace(title))
                title = item;

            onTitleResolved?.Invoke(title);
            builder.AppendLine($"TITLE \"[{++index}/{items.Count}] - {EscapeBatchTitle(title)}\"");

            var argsPerItem = buildArgsForInput(item).Replace("%", "%%");
            builder.AppendLine($"\"{exePath}\" {argsPerItem}");
        }

        return builder.ToString();
    }

    private static async Task<string> BuildFromTextFileAsync(
        string inputPath,
        string exePath,
        Func<string, Task<string>> resolveTitleAsync,
        Func<string, string> buildArgsForInput,
        Action<string>? onTitleResolved,
        CancellationToken cancellationToken)
    {
        var rawLines = File.ReadAllLines(inputPath, TextEncodingDetector.DetectFromFile(inputPath)).ToList();

        var builder = new StringBuilder();
        builder.AppendLine("@echo off");
        builder.AppendLine("::Created by N_m3u8DL_RE_GUI");
        var index = 0;
        foreach (var line in rawLines)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!BatchInputParser.TryParse(line, out var parsed) || parsed == null)
                continue;

            var title = parsed.HasCustomTitle
                ? parsed.Title
                : await resolveTitleAsync(parsed.Url);

            if (string.IsNullOrWhiteSpace(title))
            {
                title = parsed.HasCustomTitle
                    ? parsed.Title
                    : parsed.Url;
            }

            onTitleResolved?.Invoke(title);
            builder.AppendLine($"TITLE \"[{++index}/{rawLines.Count}] - {EscapeBatchTitle(title)}\"");

            var argsPerItem = buildArgsForInput(parsed.Url).Replace("%", "%%");
            builder.AppendLine($"\"{exePath}\" {argsPerItem}");
        }

        return builder.ToString();
    }

    private static string EscapeBatchTitle(string title)
    {
        if (string.IsNullOrWhiteSpace(title))
            return "Untitled";

        return title
            .Replace("\r", " ")
            .Replace("\n", " ")
            .Replace("%", "%%")
            .Replace("\"", "'");
    }
}

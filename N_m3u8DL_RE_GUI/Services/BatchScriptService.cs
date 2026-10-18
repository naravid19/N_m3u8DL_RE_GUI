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

        var filePath = Path.Combine(Path.GetTempPath(), $"batch_{DateTime.Now:yyyyMMddHHmmss}_{Guid.NewGuid():N}.bat");
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
        builder.AppendLine("chcp 65001 >nul");
        builder.AppendLine("::Created by N_m3u8DL_RE_GUI");
        var index = 0;
        foreach (var item in items)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var title = Path.GetFileNameWithoutExtension(item);
            if (string.IsNullOrWhiteSpace(title))
                title = item;

            var argsPerItem = buildArgsForInput(item).Replace("%", "%%");
            var commandLine = $"\"{exePath}\" {argsPerItem}";
            if (WouldCmdRunAnExtraCommand(commandLine))
            {
                AppendSkippedEntry(builder);
                continue;
            }

            onTitleResolved?.Invoke(title);
            builder.AppendLine($"TITLE \"[{++index}/{items.Count}] - {EscapeBatchTitle(title)}\"");
            builder.AppendLine(commandLine);
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
        var rawLines = await File.ReadAllLinesAsync(inputPath, TextEncodingDetector.DetectFromFile(inputPath), cancellationToken);
        var validItems = new List<BatchInputEntry>();
        foreach (var line in rawLines)
        {
            if (BatchInputParser.TryParse(line, out var parsed) && parsed != null)
            {
                validItems.Add(parsed);
            }
        }

        var builder = new StringBuilder();
        builder.AppendLine("@echo off");
        builder.AppendLine("chcp 65001 >nul");
        builder.AppendLine("::Created by N_m3u8DL_RE_GUI");

        var titles = new string[validItems.Count];
        var options = new ParallelOptions
        {
            MaxDegreeOfParallelism = 4,
            CancellationToken = cancellationToken
        };

        await Parallel.ForEachAsync(Enumerable.Range(0, validItems.Count), options, async (i, ct) =>
        {
            var item = validItems[i];
            if (item.HasCustomTitle)
            {
                titles[i] = item.Title;
                return;
            }

            try
            {
                var title = await resolveTitleAsync(item.Url);
                titles[i] = string.IsNullOrWhiteSpace(title) ? item.Url : title;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                titles[i] = item.Url;
            }
            catch
            {
                titles[i] = item.Url;
            }
        });

        var index = 0;
        for (int i = 0; i < validItems.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var parsed = validItems[i];
            var title = string.IsNullOrWhiteSpace(titles[i])
                ? (parsed.HasCustomTitle ? parsed.Title : parsed.Url)
                : titles[i];

            var argsPerItem = buildArgsForInput(parsed.Url).Replace("%", "%%");
            var commandLine = $"\"{exePath}\" {argsPerItem}";
            if (WouldCmdRunAnExtraCommand(commandLine))
            {
                AppendSkippedEntry(builder);
                continue;
            }

            onTitleResolved?.Invoke(title);
            builder.AppendLine($"TITLE \"[{++index}/{validItems.Count}] - {EscapeBatchTitle(title)}\"");
            builder.AppendLine(commandLine);
        }

        return builder.ToString();
    }

    /// <summary>
    /// True when cmd.exe would read this line as more than one command.
    /// <para>
    /// cmd.exe has no backslash escape: every <c>"</c> simply toggles whether it is
    /// inside a quoted region. <c>ArgsBuilder</c> escapes an embedded quote as
    /// <c>\"</c> because that is what CreateProcess/argv wants — but cmd reads the
    /// same sequence as <em>closing</em> the quotes, which leaves any following
    /// <c>&amp;</c> <c>|</c> <c>&lt;</c> <c>&gt;</c> acting as a command separator
    /// rather than as data. A crafted URL in a shared list would then run as its own
    /// command the moment the generated batch file is executed, which the caller does
    /// immediately and without a preview.
    /// </para>
    /// <para>
    /// Note this runs <em>after</em> <c>%</c> has been doubled, so the <c>%%</c> pairs
    /// it sees carry no separator characters of their own.
    /// </para>
    /// </summary>
    private static bool WouldCmdRunAnExtraCommand(string commandLine)
    {
        var insideQuotes = false;
        foreach (var c in commandLine)
        {
            if (c == '"')
            {
                insideQuotes = !insideQuotes;
                continue;
            }

            // Harmless while quoted -- an ampersand in a query string is ordinary and
            // must keep working, so the test is position-aware rather than a blanklist.
            if (!insideQuotes && (c is '&' or '|' or '<' or '>' or '^' or '(' or ')'))
                return true;
        }

        // A line that ends still inside a quote leaves everything after it undefined.
        return insideQuotes;
    }

    /// <summary>
    /// Records a dropped entry. The offending text is deliberately not echoed back —
    /// writing it into the batch file is the very thing being prevented.
    /// </summary>
    private static void AppendSkippedEntry(StringBuilder builder)
    {
        builder.AppendLine(":: Skipped one entry: its arguments cannot be written safely to a batch file.");
        builder.AppendLine("echo [SKIPPED] An entry was omitted because it contained characters cmd.exe treats as a command separator.");
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

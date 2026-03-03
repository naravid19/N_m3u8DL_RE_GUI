#nullable enable
using N_m3u8DL_RE_GUI.Services;
using Xunit;

namespace N_m3u8DL_RE_GUI.Tests;

public class BatchScriptServiceTests
{
    [Fact]
    public void IsBatchInput_ShouldMatchLegacyConditions()
    {
        var service = new BatchScriptService();
        var tempDir = Directory.CreateTempSubdirectory("batch-input-test");
        var txtFile = Path.Combine(tempDir.FullName, "list.txt");
        var txtUpperFile = Path.Combine(tempDir.FullName, "list_upper.TXT");
        File.WriteAllText(txtFile, "https://example.com/video.m3u8");
        File.WriteAllText(txtUpperFile, "https://example.com/video.m3u8");

        try
        {
            Assert.True(service.IsBatchInput(txtFile));
            Assert.True(service.IsBatchInput(txtUpperFile));
            Assert.True(service.IsBatchInput(tempDir.FullName));
            Assert.False(service.IsBatchInput("https://example.com/list.txt"));
            Assert.False(service.IsBatchInput(string.Empty));
        }
        finally
        {
            tempDir.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task BuildScriptAsync_WithDirectoryInput_ShouldIncludeOnlyM3u8AndMpd()
    {
        var service = new BatchScriptService();
        var tempDir = Directory.CreateTempSubdirectory("batch-dir-test");
        var m3u8Path = Path.Combine(tempDir.FullName, "video_a.m3u8");
        var mpdPath = Path.Combine(tempDir.FullName, "video_b.mpd");
        var ignoredPath = Path.Combine(tempDir.FullName, "ignored.txt");

        File.WriteAllText(m3u8Path, string.Empty);
        File.WriteAllText(mpdPath, string.Empty);
        File.WriteAllText(ignoredPath, string.Empty);

        var resolvedTitles = new List<string>();
        try
        {
            var result = await service.BuildScriptAsync(
                inputPath: tempDir.FullName,
                exePath: @"C:\Tools\N_m3u8DL-RE.exe",
                resolveTitleAsync: input => Task.FromResult("Title-" + Path.GetFileNameWithoutExtension(input)),
                buildArgsForInput: _ => "--test 100%",
                onTitleResolved: title => resolvedTitles.Add(title));

            var lines = result.Content
                .Split(new[] { "\r\n", "\n" }, StringSplitOptions.None)
                .Where(line => !string.IsNullOrWhiteSpace(line))
                .ToList();

            Assert.StartsWith("Batch-", result.FilePath);
            Assert.EndsWith(".bat", result.FilePath);
            Assert.Contains("@echo off", lines);
            Assert.Contains("::Created by N_m3u8DL_RE_GUI", result.Content);
            Assert.Equal(2, lines.Count(line => line.StartsWith("TITLE \"[", StringComparison.Ordinal)));
            Assert.Equal(2, lines.Count(line => line.StartsWith("\"C:\\Tools\\N_m3u8DL-RE.exe\" ", StringComparison.Ordinal)));
            Assert.Contains(lines, line => line.Contains("--test 100%%", StringComparison.Ordinal));
            Assert.Equal(2, resolvedTitles.Count);
        }
        finally
        {
            tempDir.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task BuildScriptAsync_WithDirectoryInput_ShouldBeDeterministicByPathOrder()
    {
        var service = new BatchScriptService();
        var tempDir = Directory.CreateTempSubdirectory("batch-dir-order");
        var second = Path.Combine(tempDir.FullName, "b_video.m3u8");
        var first = Path.Combine(tempDir.FullName, "a_video.m3u8");
        File.WriteAllText(second, string.Empty);
        File.WriteAllText(first, string.Empty);

        try
        {
            var result = await service.BuildScriptAsync(
                inputPath: tempDir.FullName,
                exePath: @"C:\Tools\N_m3u8DL-RE.exe",
                resolveTitleAsync: input => Task.FromResult("Title-" + Path.GetFileNameWithoutExtension(input)),
                buildArgsForInput: input => $"--input \"{input}\"");

            var titleLines = result.Content
                .Split(new[] { "\r\n", "\n" }, StringSplitOptions.None)
                .Where(line => line.StartsWith("TITLE \"[", StringComparison.Ordinal))
                .ToList();

            Assert.Equal(2, titleLines.Count);
            Assert.Contains("a_video", titleLines[0]);
            Assert.Contains("b_video", titleLines[1]);
        }
        finally
        {
            tempDir.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task BuildScriptAsync_WithDirectoryInput_ShouldUseFileNameAsTitle_WithoutResolverCall()
    {
        var service = new BatchScriptService();
        var tempDir = Directory.CreateTempSubdirectory("batch-dir-title");
        var filePath = Path.Combine(tempDir.FullName, "episode_01.m3u8");
        File.WriteAllText(filePath, string.Empty);

        var resolverCallCount = 0;
        try
        {
            var result = await service.BuildScriptAsync(
                inputPath: tempDir.FullName,
                exePath: @"C:\Tools\N_m3u8DL-RE.exe",
                resolveTitleAsync: _ =>
                {
                    resolverCallCount++;
                    return Task.FromResult("ShouldNotBeUsed");
                },
                buildArgsForInput: _ => "--test");

            Assert.Contains("TITLE \"[1/1] - episode_01\"", result.Content);
            Assert.Equal(0, resolverCallCount);
        }
        finally
        {
            tempDir.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task BuildScriptAsync_WithTxtInput_ShouldPreserveLegacyIndexDenominator()
    {
        var service = new BatchScriptService();
        var tempDir = Directory.CreateTempSubdirectory("batch-txt-test");
        var txtPath = Path.Combine(tempDir.FullName, "list.txt");
        File.WriteAllLines(txtPath, new[]
        {
            "#comment",
            "Episode 1,https://example.com/ep1.m3u8",
            "https://example.com/ep2.m3u8",
            "invalid-format"
        });

        var resolverCallCount = 0;
        try
        {
            var result = await service.BuildScriptAsync(
                inputPath: txtPath,
                exePath: @"C:\Tools\N_m3u8DL-RE.exe",
                resolveTitleAsync: _ =>
                {
                    resolverCallCount++;
                    return Task.FromResult("Auto-Title");
                },
                buildArgsForInput: _ => "--arg 9%");

            Assert.Contains("TITLE \"[1/4] - Episode 1\"", result.Content);
            Assert.Contains("TITLE \"[2/4] - Auto-Title\"", result.Content);
            Assert.Equal(1, resolverCallCount);
            Assert.Equal(2, result.Content.Split('\n').Count(line =>
                line.StartsWith("\"C:\\Tools\\N_m3u8DL-RE.exe\" ", StringComparison.Ordinal)));
            Assert.Contains("--arg 9%%", result.Content);
        }
        finally
        {
            tempDir.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task BuildScriptAsync_WithTxtInput_ShouldEscapeTitleForBatchContext()
    {
        var service = new BatchScriptService();
        var tempDir = Directory.CreateTempSubdirectory("batch-txt-escape");
        var txtPath = Path.Combine(tempDir.FullName, "list.txt");
        File.WriteAllLines(txtPath, new[]
        {
            "Episode \"100%\",https://example.com/ep1.m3u8"
        });

        try
        {
            var result = await service.BuildScriptAsync(
                inputPath: txtPath,
                exePath: @"C:\Tools\N_m3u8DL-RE.exe",
                resolveTitleAsync: _ => Task.FromResult(string.Empty),
                buildArgsForInput: _ => "--arg");

            Assert.Contains("TITLE \"[1/1] - Episode '100%%'\"", result.Content);
        }
        finally
        {
            tempDir.Delete(recursive: true);
        }
    }

    [Fact]
    public void SaveScript_ShouldWriteUtf8WithoutBom()
    {
        var service = new BatchScriptService();
        var tempFile = Path.GetTempFileName();
        try
        {
            service.SaveScript(tempFile, "hello");

            var bytes = File.ReadAllBytes(tempFile);
            Assert.True(bytes.Length >= 5);
            Assert.False(bytes.Length >= 3 &&
                         bytes[0] == 0xEF &&
                         bytes[1] == 0xBB &&
                         bytes[2] == 0xBF);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }
}

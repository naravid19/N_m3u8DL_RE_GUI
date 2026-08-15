#nullable enable
using System.Text;
using N_m3u8DL_RE_GUI.Services;
using Xunit;

namespace N_m3u8DL_RE_GUI.Tests.Unit.Services;

/// <summary>
/// Batch-file escaping and input-filtering rules for <see cref="BatchScriptService"/>.
/// Generated <c>.bat</c> content is interpreted by cmd.exe, where <c>%</c> is an expansion
/// character and <c>"</c> terminates a TITLE argument — both need doubling/rewriting.
/// </summary>
public class BatchScriptServiceEscapingTests
{
    private static readonly Func<string, Task<string>> NoTitleResolver = _ => Task.FromResult(string.Empty);

    [Fact]
    public async Task BuildScriptAsync_ShouldEmitUtf8ConsoleHeader()
    {
        await WithTxtAsync("https://example.com/a.m3u8", async path =>
        {
            var result = await BuildAsync(path, url => $"\"{url}\"");

            Assert.StartsWith("@echo off", result.Content);
            Assert.Contains("chcp 65001 >nul", result.Content);
            Assert.Contains("::Created by N_m3u8DL_RE_GUI", result.Content);
        });
    }

    [Fact]
    public async Task BuildScriptAsync_ShouldDoublePercentSignsInArguments()
    {
        // A percent-encoded URL must survive cmd.exe expansion.
        await WithTxtAsync("https://example.com/a%20b.m3u8", async path =>
        {
            var result = await BuildAsync(path, url => $"\"{url}\"");

            Assert.Contains("a%%20b.m3u8", result.Content);
            Assert.DoesNotContain("a%20b.m3u8", result.Content.Replace("%%", "\u0000"));
        });
    }

    [Fact]
    public async Task BuildScriptAsync_ShouldDoublePercentSignsInTitles()
    {
        await WithTxtAsync("100%% Complete,https://example.com/a.m3u8", async path =>
        {
            var result = await BuildAsync(path, url => $"\"{url}\"");

            Assert.Contains("TITLE \"[1/1] - 100%%%% Complete\"", result.Content);
        });
    }

    [Fact]
    public async Task BuildScriptAsync_ShouldReplaceDoubleQuotesInTitlesWithApostrophes()
    {
        await WithTxtAsync("The \"Best\" Show,https://example.com/a.m3u8", async path =>
        {
            var result = await BuildAsync(path, url => $"\"{url}\"");

            Assert.Contains("TITLE \"[1/1] - The 'Best' Show\"", result.Content);
        });
    }

    [Fact]
    public async Task BuildScriptAsync_ShouldFlattenNewLinesInResolvedTitles()
    {
        await WithTxtAsync("https://example.com/a.m3u8", async path =>
        {
            var result = await BuildAsync(
                path,
                url => $"\"{url}\"",
                resolver: _ => Task.FromResult("line one\r\nline two"));

            Assert.Contains("TITLE \"[1/1] - line one  line two\"", result.Content);
            var titleLine = result.Content
                .Split('\n')
                .First(l => l.StartsWith("TITLE", StringComparison.Ordinal));
            Assert.EndsWith("\"", titleLine.TrimEnd('\r'));
        });
    }

    [Theory]
    [InlineData("# hash comment")]
    [InlineData("// slash comment")]
    [InlineData("")]
    [InlineData("     ")]
    [InlineData("just some prose")]
    public async Task BuildScriptAsync_ShouldSkipNonInputLines(string line)
    {
        await WithTxtAsync($"{line}\nhttps://example.com/a.m3u8", async path =>
        {
            var result = await BuildAsync(path, url => $"\"{url}\"");

            Assert.Single(TitleLines(result.Content));
            Assert.Contains("[1/1]", result.Content);
        });
    }

    [Fact]
    public async Task BuildScriptAsync_ShouldAcceptLocalFilePathsAsBatchItems()
    {
        await WithTxtAsync(@"C:\media\show.m3u8" + "\n" + @"\\nas\share\other.mpd", async path =>
        {
            var result = await BuildAsync(path, url => $"\"{url}\"");

            Assert.Equal(2, TitleLines(result.Content).Count);
            Assert.Contains(@"C:\media\show.m3u8", result.Content);
            Assert.Contains(@"\\nas\share\other.mpd", result.Content);
        });
    }

    [Fact]
    public async Task BuildScriptAsync_WithEmptyTxtFile_ShouldEmitHeaderOnly()
    {
        await WithTxtAsync("# nothing but a comment", async path =>
        {
            var result = await BuildAsync(path, url => $"\"{url}\"");

            Assert.Empty(TitleLines(result.Content));
            Assert.Contains("chcp 65001 >nul", result.Content);
        });
    }

    [Fact]
    public async Task BuildScriptAsync_ShouldWrapTheExecutablePathInQuotes()
    {
        await WithTxtAsync("https://example.com/a.m3u8", async path =>
        {
            var result = await BuildAsync(path, url => $"\"{url}\"", exePath: @"C:\Program Files\RE\N_m3u8DL-RE.exe");

            Assert.Contains("\"C:\\Program Files\\RE\\N_m3u8DL-RE.exe\" ", result.Content);
        });
    }

    [Fact]
    public async Task BuildScriptAsync_ShouldNumberItemsSequentiallyWithTheFilteredCount()
    {
        var lines = string.Join("\n", new[]
        {
            "# header",
            "https://example.com/1.m3u8",
            "",
            "https://example.com/2.m3u8",
            "// trailing comment",
            "https://example.com/3.m3u8"
        });

        await WithTxtAsync(lines, async path =>
        {
            var result = await BuildAsync(path, url => $"\"{url}\"");

            Assert.Contains("[1/3]", result.Content);
            Assert.Contains("[2/3]", result.Content);
            Assert.Contains("[3/3]", result.Content);
        });
    }

    [Fact]
    public async Task BuildScriptAsync_ShouldReportEveryResolvedTitleThroughTheCallback()
    {
        await WithTxtAsync("A,https://example.com/1.m3u8\nB,https://example.com/2.m3u8", async path =>
        {
            var seen = new List<string>();

            await BuildAsync(path, url => $"\"{url}\"", onTitleResolved: seen.Add);

            Assert.Equal(new[] { "A", "B" }, seen);
        });
    }

    [Fact]
    public async Task BuildScriptAsync_WhenCancelled_ShouldThrowOperationCanceled()
    {
        await WithTxtAsync("https://example.com/a.m3u8", async path =>
        {
            using var cts = new CancellationTokenSource();
            cts.Cancel();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => BuildAsync(path, url => $"\"{url}\"", cancellationToken: cts.Token));
        });
    }

    [Fact]
    public void SaveScript_ShouldOverwriteAnExistingFile()
    {
        var path = Path.Combine(Path.GetTempPath(), $"batchtest_{Guid.NewGuid():N}.bat");
        try
        {
            var service = new BatchScriptService();
            service.SaveScript(path, "first");
            service.SaveScript(path, "second");

            Assert.Equal("second", File.ReadAllText(path));
            Assert.Equal(new byte[] { (byte)'s' }, File.ReadAllBytes(path).Take(1).ToArray());
        }
        finally
        {
            try { File.Delete(path); } catch { }
        }
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("   ", false)]
    [InlineData("https://example.com/list.txt", false)]
    [InlineData("HTTPS://EXAMPLE.COM/list.txt", false)]
    [InlineData(@"C:\does\not\exist.txt", false)]
    [InlineData("plain.m3u8", false)]
    public void IsBatchInput_ShouldRejectNonBatchInputs(string? input, bool expected)
    {
        Assert.Equal(expected, new BatchScriptService().IsBatchInput(input!));
    }

    [Fact]
    public void IsBatchInput_ShouldAcceptAnExistingTxtFileAndAnExistingDirectory()
    {
        var service = new BatchScriptService();
        var txt = Path.Combine(Path.GetTempPath(), $"batchtest_{Guid.NewGuid():N}.txt");
        File.WriteAllText(txt, "https://example.com/a.m3u8");
        try
        {
            Assert.True(service.IsBatchInput(txt));
            Assert.True(service.IsBatchInput(txt.ToUpperInvariant()));
            Assert.True(service.IsBatchInput(Path.GetTempPath()));
        }
        finally
        {
            try { File.Delete(txt); } catch { }
        }
    }

    // -------------------------------------------------------------------------

    private static List<string> TitleLines(string content) =>
        content.Split('\n')
            .Select(l => l.TrimEnd('\r'))
            .Where(l => l.StartsWith("TITLE ", StringComparison.Ordinal))
            .ToList();

    private static Task<BatchScriptBuildResult> BuildAsync(
        string inputPath,
        Func<string, string> buildArgs,
        string exePath = @"C:\RE\N_m3u8DL-RE.exe",
        Func<string, Task<string>>? resolver = null,
        Action<string>? onTitleResolved = null,
        CancellationToken cancellationToken = default)
    {
        return new BatchScriptService().BuildScriptAsync(
            inputPath,
            exePath,
            resolver ?? NoTitleResolver,
            buildArgs,
            onTitleResolved,
            cancellationToken);
    }

    private static async Task WithTxtAsync(string content, Func<string, Task> body)
    {
        var path = Path.Combine(Path.GetTempPath(), $"batchtest_{Guid.NewGuid():N}.txt");
        await File.WriteAllTextAsync(path, content, new UTF8Encoding(false));
        try
        {
            await body(path);
        }
        finally
        {
            try { File.Delete(path); } catch { }
        }
    }
}

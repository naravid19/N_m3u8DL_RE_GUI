#nullable enable
using System.Text;
using N_m3u8DL_RE_GUI.Services;
using Xunit;

namespace N_m3u8DL_RE_GUI.Tests.Unit.Services;

/// <summary>
/// Format-level coverage for the legacy <c>config.txt</c> codec
/// (<c>key=value;key=value</c>). Several GUI fields are stored raw rather than base64,
/// so these tests pin down exactly which characters survive a round trip.
/// </summary>
public class ConfigServiceFormatTests
{
    [Fact]
    public void Load_WithEmptyFile_ShouldReturnEmptyState()
    {
        WithTempFile(string.Empty, path =>
        {
            Assert.Empty(new ConfigService().Load(path).Entries);
        });
    }

    [Fact]
    public void Load_WithWhitespaceOnlyFile_ShouldReturnEmptyState()
    {
        WithTempFile("   \r\n\t ", path =>
        {
            Assert.Empty(new ConfigService().Load(path).Entries);
        });
    }

    [Fact]
    public void Load_ShouldKeepEverythingAfterTheFirstEqualsSign()
    {
        WithTempFile("SavePattern=$Title_$Id=$Res", path =>
        {
            Assert.Equal("$Title_$Id=$Res", new ConfigService().Load(path).Get("SavePattern"));
        });
    }

    [Fact]
    public void Load_WithLeadingEqualsSign_ShouldSkipTheSegment()
    {
        WithTempFile("=orphan;NoLog=1", path =>
        {
            var state = new ConfigService().Load(path);

            Assert.True(state.GetBool("NoLog"));
            Assert.Single(state.Entries);
        });
    }

    [Fact]
    public void Load_ShouldTrimWhitespaceAroundKeysButNotValues()
    {
        WithTempFile("  AdKeyword  = spaced value ", path =>
        {
            var state = new ConfigService().Load(path);

            Assert.Equal(" spaced value ", state.Get("AdKeyword"));
        });
    }

    [Fact]
    public void Load_WithDuplicateKeys_ShouldKeepTheLastOccurrence()
    {
        WithTempFile("LogLevel=0;LogLevel=3", path =>
        {
            Assert.Equal("3", new ConfigService().Load(path).Get("LogLevel"));
        });
    }

    [Fact]
    public void Load_WithSegmentThatHasNoEqualsSign_ShouldIgnoreIt()
    {
        WithTempFile("garbage;NoLog=1;alsoGarbage", path =>
        {
            var state = new ConfigService().Load(path);

            Assert.Single(state.Entries);
            Assert.True(state.GetBool("NoLog"));
        });
    }

    [Fact]
    public void RoundTrip_WithSemicolonInAValue_ShouldPreserveTheWholeValue()
    {
        WithTempFile(string.Empty, path =>
        {
            var service = new ConfigService();
            var state = new AppConfigState();
            state.Set("AdKeyword", "ads;sponsor");
            state.Set("SavePattern", "100% of $Title");
            state.Set("NoLog", "1");

            service.Save(path, state);
            var loaded = service.Load(path);

            Assert.Equal("ads;sponsor", loaded.Get("AdKeyword"));
            Assert.Equal("100% of $Title", loaded.Get("SavePattern"));
            Assert.True(loaded.GetBool("NoLog"));
        });
    }

    [Fact]
    public void Load_ShouldStillReadFilesWrittenBeforeEscapingExisted()
    {
        WithTempFile("AdKeyword=plain value;NoLog=1", path =>
        {
            var loaded = new ConfigService().Load(path);

            Assert.Equal("plain value", loaded.Get("AdKeyword"));
            Assert.True(loaded.GetBool("NoLog"));
        });
    }

    [Fact]
    public void RoundTrip_WithBase64EncodedFields_IsSafeBecauseBase64HasNoSeparators()
    {
        WithTempFile(string.Empty, path =>
        {
            var service = new ConfigService();
            var state = new AppConfigState();
            state.SetEncodedBase64("请求头", "Referer: https://example.com/;X-Token: a=b;c");

            service.Save(path, state);

            Assert.Equal(
                "Referer: https://example.com/;X-Token: a=b;c",
                service.Load(path).GetDecodedBase64("请求头"));
        });
    }

    [Fact]
    public void RoundTrip_WithUnicodeValues_ShouldPreserveCharacters()
    {
        WithTempFile(string.Empty, path =>
        {
            var service = new ConfigService();
            var state = new AppConfigState();
            state.SetEncodedBase64("保存路径", @"D:\ดาวน์โหลด\影片");
            state.Set("SavePattern", "ตอนที่_$Id");

            service.Save(path, state);
            var loaded = service.Load(path);

            Assert.Equal(@"D:\ดาวน์โหลด\影片", loaded.GetDecodedBase64("保存路径"));
            Assert.Equal("ตอนที่_$Id", loaded.Get("SavePattern"));
        });
    }

    [Fact]
    public void Save_WithEmptyState_ShouldWriteAnEmptyFile()
    {
        WithTempFile("stale content", path =>
        {
            new ConfigService().Save(path, new AppConfigState());

            Assert.Equal(string.Empty, File.ReadAllText(path));
        });
    }

    [Fact]
    public void Save_ShouldNotEmitATrailingSeparator()
    {
        WithTempFile(string.Empty, path =>
        {
            var state = new AppConfigState();
            state.Set("A", "1");
            state.Set("B", "2");

            new ConfigService().Save(path, state);

            var content = File.ReadAllText(path);
            Assert.DoesNotContain(";;", content);
            Assert.False(content.EndsWith(";", StringComparison.Ordinal));
            Assert.Equal(2, content.Split(';').Length);
        });
    }

    [Fact]
    public void Load_WithEmptyOrNullPath_ShouldReturnEmptyStateWithoutThrowing()
    {
        var service = new ConfigService();

        Assert.Empty(service.Load(string.Empty).Entries);
        Assert.Empty(service.Load("   ").Entries);
        Assert.Empty(service.Load(null!).Entries);
    }

    [Fact]
    public void Save_WithEmptyOrNullPath_ShouldNotThrow()
    {
        var service = new ConfigService();
        var state = new AppConfigState();
        state.Set("A", "1");

        Assert.Null(Record.Exception(() => service.Save(string.Empty, state)));
        Assert.Null(Record.Exception(() => service.Save(null!, state)));
    }

    [Fact]
    public void Load_WithDirectoryPathInsteadOfFile_ShouldReturnEmptyState()
    {
        Assert.Empty(new ConfigService().Load(Path.GetTempPath()).Entries);
    }

    private static void WithTempFile(string initialContent, Action<string> body)
    {
        var path = Path.Combine(Path.GetTempPath(), $"cfgtest_{Guid.NewGuid():N}.txt");
        File.WriteAllText(path, initialContent, new UTF8Encoding(false));
        try
        {
            body(path);
        }
        finally
        {
            try { File.Delete(path); } catch { }
        }
    }
}

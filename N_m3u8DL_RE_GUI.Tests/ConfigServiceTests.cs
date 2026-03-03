using N_m3u8DL_RE_GUI.Services;
using Xunit;

namespace N_m3u8DL_RE_GUI.Tests;

public class ConfigServiceTests
{
    [Fact]
    public void Load_WithInvalidPath_ShouldReturnEmptyState()
    {
        var service = new ConfigService();

        var state = service.Load("\0invalid-path");

        Assert.Empty(state.Entries);
    }

    [Fact]
    public void Save_WithInvalidPath_ShouldNotThrow()
    {
        var service = new ConfigService();
        var state = new AppConfigState();
        state.Set("key", "value");

        var exception = Record.Exception(() => service.Save("\0invalid-path", state));

        Assert.Null(exception);
    }

    [Fact]
    public void Load_WithMalformedConfig_ShouldNotThrowAndShouldParseKnownPairs()
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            File.WriteAllText(
                tempFile,
                "程序路径=%%%invalid%%%;;badSegment;删除临时文件=1;最大线程=12;NoLog=0");

            var service = new ConfigService();

            var state = service.Load(tempFile);

            Assert.Equal(string.Empty, state.GetDecodedBase64("程序路径"));
            Assert.True(state.GetBool("删除临时文件"));
            Assert.Equal(12, state.GetInt("最大线程"));
            Assert.False(state.GetBool("NoLog", defaultValue: true));
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public void SaveAndLoad_ShouldRoundTripLegacyFlagsAndBase64Fields()
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            var service = new ConfigService();
            var state = new AppConfigState();
            state.SetEncodedBase64("程序路径", "N_m3u8DL-RE.exe");
            state.SetEncodedBase64("保存路径", @"C:\");
            state.Set("删除临时文件", "1");
            state.Set("ForceAnsiConsole", "1");

            service.Save(tempFile, state);
            var loaded = service.Load(tempFile);

            Assert.Equal("N_m3u8DL-RE.exe", loaded.GetDecodedBase64("程序路径"));
            Assert.Equal(@"C:\", loaded.GetDecodedBase64("保存路径"));
            Assert.True(loaded.GetBool("删除临时文件"));
            Assert.True(loaded.GetBool("ForceAnsiConsole"));
        }
        finally
        {
            File.Delete(tempFile);
        }
    }
}

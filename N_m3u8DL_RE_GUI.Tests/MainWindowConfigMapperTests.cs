#nullable enable
using N_m3u8DL_RE_GUI.Services;
using Xunit;

namespace N_m3u8DL_RE_GUI.Tests;

public class MainWindowConfigMapperTests
{
    [Fact]
    public void ResolveMuxImport_ShouldPreferMuxImportAndFallbackToMuxJson()
    {
        var state = new AppConfigState();
        state.Set("MuxImport", @"C:\new\mux.json");
        state.Set("MuxJson", @"C:\legacy\mux.json");

        var resolved = MainWindowConfigMapper.ResolveMuxImport(state);

        Assert.Equal(@"C:\new\mux.json", resolved);
    }

    [Fact]
    public void ResolveMuxImport_ShouldUseLegacyKey_WhenNewKeyIsMissing()
    {
        var state = new AppConfigState();
        state.Set("MuxJson", @"C:\legacy\mux.json");

        var resolved = MainWindowConfigMapper.ResolveMuxImport(state);

        Assert.Equal(@"C:\legacy\mux.json", resolved);
    }

    [Fact]
    public void ResolveCustomHlsIv_ShouldPreferCustomKeyAndFallbackToLegacyIv()
    {
        var state = new AppConfigState();
        state.Set("CustomHLSIv", "NEW_IV");
        state.Set("IV", "LEGACY_IV");

        var resolved = MainWindowConfigMapper.ResolveCustomHlsIv(state);

        Assert.Equal("NEW_IV", resolved);
    }

    [Fact]
    public void ResolveCustomHlsIv_ShouldUseLegacyKey_WhenNewKeyIsMissing()
    {
        var state = new AppConfigState();
        state.Set("IV", "LEGACY_IV");

        var resolved = MainWindowConfigMapper.ResolveCustomHlsIv(state);

        Assert.Equal("LEGACY_IV", resolved);
    }
}

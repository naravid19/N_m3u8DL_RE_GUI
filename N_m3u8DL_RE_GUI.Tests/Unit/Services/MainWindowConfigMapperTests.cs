#nullable enable
using N_m3u8DL_RE_GUI.Services;
using Xunit;

namespace N_m3u8DL_RE_GUI.Tests.Unit.Services;

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
    public void ResolveMuxImport_ShouldReturnEmpty_WhenBothMissing()
    {
        var state = new AppConfigState();

        var resolved = MainWindowConfigMapper.ResolveMuxImport(state);

        Assert.Equal(string.Empty, resolved);
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

    [Fact]
    public void ResolveCustomHlsIv_ShouldReturnEmpty_WhenBothMissing()
    {
        var state = new AppConfigState();

        var resolved = MainWindowConfigMapper.ResolveCustomHlsIv(state);

        Assert.Equal(string.Empty, resolved);
    }

    [Fact]
    public void ResolveCustomHlsIv_ShouldStillReadConfigsWrittenBeforeTheIvDuplicateWasDropped()
    {
        // Old configs have only "IV"; new ones have only "CustomHLSIv". Both must load.
        var legacyOnly = new AppConfigState();
        legacyOnly.Set("IV", "00112233445566778899aabbccddeeff");
        Assert.Equal("00112233445566778899aabbccddeeff", MainWindowConfigMapper.ResolveCustomHlsIv(legacyOnly));

        var modernOnly = new AppConfigState();
        modernOnly.Set("CustomHLSIv", "ffeeddccbbaa99887766554433221100");
        Assert.Equal("ffeeddccbbaa99887766554433221100", MainWindowConfigMapper.ResolveCustomHlsIv(modernOnly));
    }
}

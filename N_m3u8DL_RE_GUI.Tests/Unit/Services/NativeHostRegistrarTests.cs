#nullable enable
using System;
using System.IO;
using Microsoft.Win32;
using N_m3u8DL_RE_GUI.Core.NativeBridge;
using N_m3u8DL_RE_GUI.Services;
using Xunit;

namespace N_m3u8DL_RE_GUI.Tests.Unit.Services;

public class NativeHostRegistrarTests
{
    [Fact]
    public void Register_DoesNotThrow()
    {
        // Calling Register() should be completely exception-safe even if registry or file IO has issues.
        var exception = Record.Exception(() => NativeHostRegistrar.Register());
        Assert.Null(exception);
    }

    [Fact]
    public void Register_WritesManifestToLocalAppData()
    {
        NativeHostRegistrar.Register();

        var manifestPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "N_m3u8DL_RE_GUI",
            "native-host.json");

        Assert.True(File.Exists(manifestPath), "Manifest file should be written to LocalAppData");
        var content = File.ReadAllText(manifestPath);
        Assert.Contains(NativeHostManifest.HostName, content);
        Assert.Contains(NativeHostManifest.AllowedOrigin, content);
    }
}

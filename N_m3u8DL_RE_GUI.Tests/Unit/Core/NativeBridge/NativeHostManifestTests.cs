#nullable enable
using System.Text.Json;
using System.Threading;
using N_m3u8DL_RE_GUI.Core.NativeBridge;
using Xunit;

namespace N_m3u8DL_RE_GUI.Tests.Unit.Core.NativeBridge;

public class NativeHostManifestTests
{
    [Fact]
    public void BuildsAManifestChromeAccepts()
    {
        var json = NativeHostManifest.BuildJson(@"C:\Apps\N_m3u8DL_RE_GUI.exe");

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.Equal("com.nm3u8dlre.gui", root.GetProperty("name").GetString());
        Assert.Equal("stdio", root.GetProperty("type").GetString());
        Assert.Equal(@"C:\Apps\N_m3u8DL_RE_GUI.exe", root.GetProperty("path").GetString());

        var origins = root.GetProperty("allowed_origins");
        Assert.Equal(1, origins.GetArrayLength());
        Assert.Equal(
            "chrome-extension://ecdmbdclbopgpbbhdelncjhbnapnblgd/",
            origins[0].GetString());
    }

    [Fact]
    public void AllowedOriginKeepsItsTrailingSlash()
    {
        // Chrome matches this string exactly; without the trailing slash it
        // silently refuses every connection.
        Assert.EndsWith("/", NativeHostManifest.AllowedOrigin);
        Assert.StartsWith("chrome-extension://", NativeHostManifest.AllowedOrigin);
    }

    [Fact]
    public void RegistersUnderChromeEdgeAndBrave()
    {
        var paths = NativeHostManifest.RegistryKeyPaths;

        Assert.Contains(@"Software\Google\Chrome\NativeMessagingHosts\com.nm3u8dlre.gui", paths);
        Assert.Contains(@"Software\Microsoft\Edge\NativeMessagingHosts\com.nm3u8dlre.gui", paths);
        Assert.Contains(@"Software\BraveSoftware\Brave-Browser\NativeMessagingHosts\com.nm3u8dlre.gui", paths);
    }

    [Theory]
    [InlineData("chrome-extension://ecdmbdclbopgpbbhdelncjhbnapnblgd/", true)]
    [InlineData("chrome-extension://someotherid/", true)]
    [InlineData("--native-host", false)]
    [InlineData(@"C:\video\playlist.m3u8", false)]
    public void RecognisesANativeHostLaunchByItsFirstArgument(string arg, bool expected)
    {
        Assert.Equal(expected, NativeHostManifest.IsNativeHostLaunch(new[] { arg }));
    }

    [Fact]
    public void ANormalLaunchHasNoArguments()
    {
        Assert.False(NativeHostManifest.IsNativeHostLaunch(System.Array.Empty<string>()));
    }

    [Fact]
    public void EscapesBackslashesInThePath()
    {
        // A Windows path in JSON must survive as a real path, not as escapes.
        var json = NativeHostManifest.BuildJson(@"C:\Program Files\N_m3u8DL_RE_GUI\app.exe");

        using var doc = JsonDocument.Parse(json);
        Assert.Equal(
            @"C:\Program Files\N_m3u8DL_RE_GUI\app.exe",
            doc.RootElement.GetProperty("path").GetString());
    }

    [Fact]
    public void SingleInstanceMutexNameIsNotBlank()
    {
        Assert.False(string.IsNullOrWhiteSpace(NativeHostManifest.SingleInstanceMutexName));
    }

    [Fact]
    public void ASecondProcessCanDetectTheMutexAnExistingGuiHolds()
    {
        // This is the exact mechanism EnsureGuiRunning relies on: a real GUI
        // creates the mutex once and keeps it alive for its whole lifetime; a
        // native-host process asks whether it exists rather than scanning
        // process names, which cannot tell a real GUI apart from another
        // transient host process sharing the same executable name.
        using var owner = new Mutex(initiallyOwned: false, NativeHostManifest.SingleInstanceMutexName);

        Assert.True(Mutex.TryOpenExisting(NativeHostManifest.SingleInstanceMutexName, out var seenByOther));
        seenByOther.Dispose();
    }

    [Fact]
    public void TheMutexStopsExistingOnceTheOwnerDisposesIt()
    {
        var mutex = new Mutex(initiallyOwned: false, NativeHostManifest.SingleInstanceMutexName + ".DisposeTest");
        mutex.Dispose();

        Assert.False(Mutex.TryOpenExisting(NativeHostManifest.SingleInstanceMutexName + ".DisposeTest", out _));
    }

    [Fact]
    public void NativeHostSubprocess_AnswersPingWithoutEverShowingAWindow()
    {
        var exePath = LocateBuiltExecutable();
        Assert.True(
            exePath is not null,
            "N_m3u8DL_RE_GUI.exe was not found next to the test assembly. " +
            "Build the GUI project before running this test; returning early here " +
            "would let the test pass while verifying nothing.");

        var psi = new System.Diagnostics.ProcessStartInfo
        {
            FileName = exePath!,
            Arguments = NativeHostManifest.AllowedOrigin,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var proc = System.Diagnostics.Process.Start(psi);
        Assert.NotNull(proc);

        NativeMessageCodec.WriteMessage(proc!.StandardInput.BaseStream, "{\"type\":\"ping\"}");
        proc.StandardInput.BaseStream.Flush();

        // Chrome launches the host for every popup probe. A window appearing
        // here would flash on the user's screen each time they open the popup.
        var sawWindow = false;
        for (var i = 0; i < 40 && !proc.HasExited; i++)
        {
            System.Threading.Thread.Sleep(50);
            proc.Refresh();
            if (proc.MainWindowHandle != IntPtr.Zero)
            {
                sawWindow = true;
                break;
            }
        }

        var response = NativeMessageCodec.ReadMessage(proc.StandardOutput.BaseStream);
        Assert.True(proc.WaitForExit(5000), "the native host did not exit on its own");

        Assert.False(sawWindow, "native-host mode must not create a window");
        Assert.Equal(0, proc.ExitCode);
        Assert.NotNull(response);
        Assert.Contains("\"ok\":true", response);
    }

    /// <summary>
    /// Walks up from the test assembly to the GUI executable built in the SAME
    /// configuration this test is running under.
    ///
    /// Deliberately does not fall back to another configuration. Doing so lets
    /// the test satisfy itself with a stale binary: renaming a freshly built
    /// Debug exe made this test pass against a weeks-old Release build, which
    /// is the same "green while verifying the wrong thing" failure the early
    /// return here used to cause.
    /// </summary>
    private static string? LocateBuiltExecutable()
    {
        // BaseDirectory is ...\<project>\bin\<configuration>\<tfm>\, so the
        // configuration is two segments up from the trailing separator. Taking
        // one segment too many yields "bin", which matches no directory and
        // silently sends every lookup to the fallbacks.
        var tfmDirectory = System.AppContext.BaseDirectory.TrimEnd(System.IO.Path.DirectorySeparatorChar);
        var configuration = System.IO.Path.GetFileName(System.IO.Path.GetDirectoryName(tfmDirectory));
        var targetFramework = System.IO.Path.GetFileName(tfmDirectory);

        if (string.IsNullOrEmpty(configuration) || string.IsNullOrEmpty(targetFramework))
            return null;

        var dir = new System.IO.DirectoryInfo(System.AppContext.BaseDirectory);
        while (dir != null)
        {
            var candidate = System.IO.Path.Combine(
                dir.FullName, "N_m3u8DL_RE_GUI", "bin", configuration, targetFramework, "N_m3u8DL_RE_GUI.exe");

            if (System.IO.File.Exists(candidate))
                return candidate;

            dir = dir.Parent;
        }
        return null;
    }
}

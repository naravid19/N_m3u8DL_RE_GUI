#nullable enable
using System.Text.RegularExpressions;
using N_m3u8DL_RE_GUI.Tests.Fixtures;
using Xunit;

namespace N_m3u8DL_RE_GUI.Tests.Unit.Services;

/// <summary>
/// Guards what has to happen when the main window closes.
/// <para>
/// <c>Window_Closing</c> needs a live WPF window and an STA pump, so it cannot be
/// driven from a unit test. This reads the source instead — the same approach
/// <see cref="DurableWriteGuardTests"/> already uses for code it cannot execute.
/// It is a weaker check than running the code, but it fails loudly if either call
/// is removed, which is exactly how these regressed in the first place.
/// </para>
/// </summary>
public class ShutdownGuardTests
{
    [Fact]
    public void ClosingTheWindowStopsWorkThatOutlivesIt()
    {
        // A download runs as a separate process (N_m3u8DL-RE, python, ffmpeg). There is
        // no Job Object tying it to this app, and DownloadService is not IDisposable, so
        // disposing the DI container on exit does not reach it either. Unless the close
        // path explicitly stops the run, those processes keep going after the window is
        // gone -- still writing files, with no UI left to stop them.
        var body = ClosingBody();

        Assert.Contains("StopDownload()", body);
        Assert.Matches(@"_activeOperationCts\?\.Cancel\(\)", body);
    }

    [Fact]
    public void ClosingTheWindowDoesNotDiscardASaveFailure()
    {
        // IConfigService.Save returns bool precisely so a failed write is not silent.
        // Ignoring it here loses every setting and secret with the window already gone.
        var body = ClosingBody();

        // (?m) so ^ anchors to each line, not just the start of the extracted body.
        Assert.DoesNotMatch(@"(?m)^\s*_configService\.Save\(", body);
    }

    private static string ClosingBody() =>
        ProjectSource.MethodBody("MainWindow.xaml.cs", "private void Window_Closing(");
}

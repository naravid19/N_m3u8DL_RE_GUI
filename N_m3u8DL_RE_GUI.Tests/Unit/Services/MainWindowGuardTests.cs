#nullable enable
using N_m3u8DL_RE_GUI.Tests.Fixtures;
using Xunit;

namespace N_m3u8DL_RE_GUI.Tests.Unit.Services;

/// <summary>
/// Guards on MainWindow paths that need a live window to run; see <see cref="ProjectSource"/>.
/// </summary>
public class MainWindowGuardTests
{
    [Theory]
    [InlineData("string BuildArgsRE(")]
    [InlineData("private DownloadOptions BuildDownloadOptions(")]
    public void TheReSaveNameIsSanitizedLikeTheAbyssAndCfNames(string builder)
    {
        // The title can arrive from the browser's nre-save-name directive with only a
        // Trim(), so the raw text would put "..\" or <>:"|?* straight into --save-name.
        var body = ProjectSource.MethodBody("MainWindow.xaml.cs", builder);

        Assert.Contains("GetValidFileName(TextBox_Title.Text)", body);
        Assert.DoesNotContain("saveName = TextBox_Title.Text", body);
    }

    [Fact]
    public void ACaptureResetsEveryStreamScopedField_ButNotTheUsersProxy()
    {
        // The Key, the Cloudflare bypass and the TLS profile belong to one Stream. A
        // capture starts its download at once, so a leftover from the previous capture
        // would run the next Stream through the wrong engine or with the wrong key.
        var body = ProjectSource.MethodBody("MainWindow.xaml.cs", "private bool TryApplyCapturedRequest(");

        Assert.Contains("TextBox_Key.Text = string.Empty;", body);
        Assert.Contains("Combo_CFImpersonate.SelectedIndex = 0;", body);
        Assert.Contains("CheckBox_BypassCF.IsChecked = plan.EnableCfBypass;", body);
        Assert.DoesNotContain("TextBox_Proxy", body);
    }

    [Fact]
    public void REOnlyTalksToTheLoopbackProxy_SoNoOtherProxyIsInsertedInFrontOfIt()
    {
        var body = ProjectSource.MethodBody("MainWindow.xaml.cs", "private async System.Threading.Tasks.Task RunCfProxyThenReAsync(");

        Assert.Contains("reOptions.Proxy = string.Empty;", body);
        Assert.Contains("reOptions.UseSystemProxy = false;", body);
    }

    [Fact]
    public void ADirectEngineRefusalHandsOverToTheProxyEngineUnlessDirectWasPinned()
    {
        // Exit 6 is m3u8_cf_bypass.py saying the manifest needs RE (init segment,
        // separate rendition, byte ranges). A pinned "direct" engine is reported, not overridden.
        var body = ProjectSource.MethodBody("MainWindow.xaml.cs", "private async System.Threading.Tasks.Task StartCloudflareDownloadAsync(");

        Assert.Contains("cfExitCode == 6", body);
        Assert.Contains("cfOptions with { Serve = true }", body);
        Assert.Contains("pinnedTag == \"direct\"", body);
    }

    [Fact]
    public void ALateTitleFetchNeverOverwritesTheTitleOfACaptureAppliedMeanwhile()
    {
        // Cold start: Window_Loaded awaits the title of the clipboard URL while the inbox
        // drain applies a browser capture and queues its download. The late fetch used to
        // replace nre-save-name after the download had already started under it.
        var fetch = ProjectSource.MethodBody("MainWindow.xaml.cs", "private async Task PopulateTitleForInputAsync(");
        var apply = ProjectSource.MethodBody("MainWindow.xaml.cs", "private bool TryApplyCapturedRequest(");

        Assert.Contains("var generation = _captureGeneration;", fetch);
        Assert.Contains("generation == _captureGeneration", fetch);
        Assert.Contains("_captureGeneration++;", apply);
    }

    [Fact]
    public void AnUnattendedCaptureRunNeverOpensTheAbyssQualityDialog()
    {
        // Nobody is at the keyboard when the inbox starts a capture. The flag is set by the
        // drain, consumed before the first await of Button_GO_Click, and only a run without it
        // may show the picker.
        var drain = ProjectSource.MethodBody("MainWindow.xaml.cs", "private void DrainCaptureInbox(");
        var click = ProjectSource.MethodBody("MainWindow.xaml.cs", "private async void Button_GO_Click(");
        var abyss = ProjectSource.MethodBody("MainWindow.xaml.cs", "private async Task StartAbyssDownloadAsync(");

        Assert.Matches(@"_startingFromCapture = true;\s*Button_GO_Click", drain);
        Assert.True(
            click.IndexOf("_startingFromCapture = false;", System.StringComparison.Ordinal)
                < click.IndexOf("await ", System.StringComparison.Ordinal),
            "the flag must be cleared before the first await");
        Assert.Contains("StartAbyssDownloadAsync(TextBox_URL.Text, unattended)", click);
        Assert.Contains("ranked.Count > 1 && !unattended", abyss);
        Assert.Contains("new Views.AbyssQualityWindow(ranked)", abyss);
    }

    [Fact]
    public void ACaptureWaitingToStartHoldsTheNextOneInTheInbox()
    {
        // The start is queued on the dispatcher and Button_GO stays enabled until it
        // runs. A second capture drained in that gap overwrote the first one's fields
        // and queued a second start: one capture lost, the other started twice.
        var body = ProjectSource.MethodBody("MainWindow.xaml.cs", "private void DrainCaptureInbox(");
        var guard = body[..body.IndexOf("while", System.StringComparison.Ordinal)];

        Assert.Contains("_captureStartPending", guard);
        Assert.Matches(@"_captureStartPending = true;\s*Dispatcher\.BeginInvoke", body);
        Assert.Contains("_captureStartPending = false", body);
    }
}

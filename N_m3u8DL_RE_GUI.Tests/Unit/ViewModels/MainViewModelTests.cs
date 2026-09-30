#nullable enable
using System;
using System.Threading.Tasks;
using N_m3u8DL_RE_GUI.Core;
using N_m3u8DL_RE_GUI.Services;
using N_m3u8DL_RE_GUI.ViewModels;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

namespace N_m3u8DL_RE_GUI.Tests.Unit.ViewModels;

public class MainViewModelTests
{
    private readonly IDownloadService _downloadService;
    private readonly IUtilityService _utilityService;
    private readonly IDragDropService _dragDropService;
    private readonly MainViewModel _viewModel;

    public MainViewModelTests()
    {
        _downloadService = Substitute.For<IDownloadService>();
        _utilityService = Substitute.For<IUtilityService>();
        _dragDropService = Substitute.For<IDragDropService>();

        _viewModel = new MainViewModel(_downloadService, _utilityService, _dragDropService);
    }

    [Fact]
    public void RefreshParametersCommand_ShouldBuildArgsFromOptions()
    {
        _viewModel.DownloadOptions.Input = "https://surrit.com/33ece07f-3229-41eb-b189-ec2485619e02/360p/video.m3u8";
        _viewModel.DownloadOptions.SaveName = "SurritTest";

        _viewModel.RefreshParametersCommand.Execute(null);

        Assert.Contains("surrit.com", _viewModel.Parameters);
        Assert.Contains("--save-name \"SurritTest\"", _viewModel.Parameters);
    }

    [Fact]
    public void StopDownloadCommand_ShouldCallServiceStopDownload()
    {
        _viewModel.StopDownloadCommand.Execute(null);

        _downloadService.Received(1).StopDownload();
        Assert.False(_viewModel.IsDownloading);
        Assert.Contains("Download stopped", _viewModel.LogOutput);
    }

    [Fact]
    public void ClearLogCommand_ShouldResetLogOutput()
    {
        _viewModel.LogOutput = "Some previous log entries";

        _viewModel.ClearLogCommand.Execute(null);

        Assert.Empty(_viewModel.LogOutput);
    }

    [Fact]
    public void ResetOptionsCommand_ShouldCreateFreshDownloadOptions()
    {
        _viewModel.DownloadOptions.Input = "https://example.com/video.m3u8";
        _viewModel.DownloadOptions.ThreadCount = 99;

        _viewModel.ResetOptionsCommand.Execute(null);

        Assert.Null(_viewModel.DownloadOptions.Input);
    }

    [Fact]
    public async Task GetTitleFromUrlCommand_WithValidUrl_ShouldUpdateSaveName()
    {
        const string inputUrl = "https://example.com/video.m3u8";
        const string expectedTitle = "My Extracted Video Title";

        _viewModel.DownloadOptions.Input = inputUrl;
        _utilityService.GetTitleFromUrlAsync(inputUrl).Returns(Task.FromResult(expectedTitle));

        await _viewModel.GetTitleFromUrlCommand.ExecuteAsync(null);

        Assert.Equal(expectedTitle, _viewModel.DownloadOptions.SaveName);
        Assert.Contains(expectedTitle, _viewModel.Parameters);
    }

    [Fact]
    public async Task GetTitleFromUrlCommand_WithEmptyInput_ShouldNotCallService()
    {
        _viewModel.DownloadOptions.Input = "";

        await _viewModel.GetTitleFromUrlCommand.ExecuteAsync(null);

        await _utilityService.DidNotReceive().GetTitleFromUrlAsync(Arg.Any<string>());
    }

    [Fact]
    public async Task GetTitleFromUrlCommand_WhenServiceThrows_ShouldLogWithoutCrashing()
    {
        const string inputUrl = "https://example.com/error.m3u8";
        _viewModel.DownloadOptions.Input = inputUrl;
        _utilityService.GetTitleFromUrlAsync(inputUrl).ThrowsAsync(new InvalidOperationException("Network failure"));

        var exception = await Record.ExceptionAsync(() => _viewModel.GetTitleFromUrlCommand.ExecuteAsync(null));

        Assert.Null(exception);
        Assert.Contains("Failed to get title", _viewModel.LogOutput);
    }

    [Fact]
    public void SelectWorkingDirectoryCommand_WhenFolderSelected_ShouldUpdateDirectories()
    {
        const string chosenFolder = @"D:\Downloads\M3U8";
        _utilityService.SelectFolder("Select download folder", Arg.Any<string>()).Returns(chosenFolder);

        _viewModel.SelectWorkingDirectoryCommand.Execute(null);

        Assert.Equal(chosenFolder, _viewModel.WorkingDirectory);
        Assert.Equal(chosenFolder, _viewModel.DownloadOptions.SaveDir);
    }

    [Fact]
    public void SelectWorkingDirectoryCommand_WhenCancelled_ShouldNotChangeWorkingDirectory()
    {
        _viewModel.WorkingDirectory = @"C:\Original";
        _utilityService.SelectFolder("Select download folder", Arg.Any<string>()).Returns((string?)null);

        _viewModel.SelectWorkingDirectoryCommand.Execute(null);

        Assert.Equal(@"C:\Original", _viewModel.WorkingDirectory);
    }

    [Fact]
    public void HandleUrlDropCommand_WithValidText_ShouldSetInputUrl()
    {
        const string droppedUrl = "https://hls.animeindy.com:8443/vid/MN8fWZAdg/video.mp4/playlist.m3u8";
        var dummyData = new object();
        _dragDropService.HandleFileDrop(dummyData).Returns(droppedUrl);

        _viewModel.HandleUrlDropCommand.Execute(dummyData);

        Assert.Equal(droppedUrl, _viewModel.DownloadOptions.Input);
    }

    [Fact]
    public void HandleUrlDropCommand_WithNullOrEmpty_ShouldNotChangeInput()
    {
        _viewModel.DownloadOptions.Input = "original-url";
        var dummyData = new object();
        _dragDropService.HandleFileDrop(dummyData).Returns((string?)null);

        _viewModel.HandleUrlDropCommand.Execute(dummyData);

        Assert.Equal("original-url", _viewModel.DownloadOptions.Input);
    }

    [Fact]
    public void HandleKeyDropCommand_WithExistingFile_ShouldSetKeyProperty()
    {
        const string dummyFilePath = @"C:\keys\sample.key";
        var dummyData = new object();
        _dragDropService.HandleFileDrop(dummyData).Returns(dummyFilePath);
        _utilityService.FileExists(dummyFilePath).Returns(true);

        _viewModel.HandleKeyDropCommand.Execute(dummyData);

        Assert.Equal(dummyFilePath, _viewModel.DownloadOptions.Key);
    }

    [Fact]
    public void HandleKeyDropCommand_WithNonExistentFile_ShouldNotSetKey()
    {
        const string dummyFilePath = @"C:\keys\nonexistent.key";
        var dummyData = new object();
        _dragDropService.HandleFileDrop(dummyData).Returns(dummyFilePath);
        _utilityService.FileExists(dummyFilePath).Returns(false);

        _viewModel.HandleKeyDropCommand.Execute(dummyData);

        Assert.Null(_viewModel.DownloadOptions.Key);
    }

    [Fact]
    public void HandleMuxJsonDropCommand_WithExistingFile_ShouldSetMuxImportProperty()
    {
        const string dummyFilePath = @"C:\configs\mux.json";
        var dummyData = new object();
        _dragDropService.HandleFileDrop(dummyData).Returns(dummyFilePath);
        _utilityService.FileExists(dummyFilePath).Returns(true);

        _viewModel.HandleMuxJsonDropCommand.Execute(dummyData);

        Assert.Equal(dummyFilePath, _viewModel.DownloadOptions.MuxImport);
    }

    [Fact]
    public void HandleMuxJsonDropCommand_WithNonExistentFile_ShouldNotSetMuxImport()
    {
        const string dummyFilePath = @"C:\configs\nonexistent.json";
        var dummyData = new object();
        _dragDropService.HandleFileDrop(dummyData).Returns(dummyFilePath);
        _utilityService.FileExists(dummyFilePath).Returns(false);

        _viewModel.HandleMuxJsonDropCommand.Execute(dummyData);

        Assert.Null(_viewModel.DownloadOptions.MuxImport);
    }

    [Fact]
    public void DownloadOptions_PropertyChange_ShouldAutoRefreshParameters()
    {
        var newOptions = new DownloadOptions
        {
            Input = "https://example.com/auto-refresh.m3u8",
            SaveName = "AutoRefreshVideo"
        };

        _viewModel.DownloadOptions = newOptions;

        Assert.Contains("auto-refresh.m3u8", _viewModel.Parameters);
        Assert.Contains("AutoRefreshVideo", _viewModel.Parameters);
    }

    [Fact]
    public void Properties_InitialState_ShouldBeDefaulted()
    {
        Assert.Equal("N_m3u8DL-RE.exe", _viewModel.ExecutablePath);
        Assert.Equal(string.Empty, _viewModel.WorkingDirectory);
        Assert.Equal(0, _viewModel.Progress);
        Assert.False(_viewModel.IsDownloading);
        Assert.Equal(string.Empty, _viewModel.LogOutput);
    }
}

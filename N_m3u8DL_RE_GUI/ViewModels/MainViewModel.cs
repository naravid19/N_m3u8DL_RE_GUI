using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using N_m3u8DL_RE_GUI.Core;
using N_m3u8DL_RE_GUI.Services;
using System.ComponentModel;
using System.Windows;
using Application = System.Windows.Application;
using MessageBox = System.Windows.MessageBox;

namespace N_m3u8DL_RE_GUI.ViewModels;

/// <summary>
/// Main ViewModel for the application using MVVM pattern.
/// </summary>
public partial class MainViewModel : ObservableObject
{
    private readonly IDownloadService _downloadService;
    private readonly IUtilityService _utilityService;
    private readonly IDragDropService _dragDropService;

    private readonly System.Text.StringBuilder _logBuilder = new();
    private bool _isLogUpdatePending = false;
    private readonly object _logLock = new object();

    public MainViewModel(IDownloadService downloadService, IUtilityService utilityService, IDragDropService dragDropService)
    {
        _downloadService = downloadService;
        _utilityService = utilityService;
        _dragDropService = dragDropService;
    }

    [ObservableProperty]
    private DownloadOptions _downloadOptions = new();

    [ObservableProperty]
    private string _parameters = string.Empty;

    [ObservableProperty]
    private string _logOutput = string.Empty;

    [ObservableProperty]
    private bool _isDownloading = false;

    [ObservableProperty]
    private double _progress = 0;

    [ObservableProperty]
    private string _executablePath = "N_m3u8DL-RE.exe";

    [ObservableProperty]
    private string _workingDirectory = string.Empty;

    /// <summary>
    /// Refresh command line parameters based on current options.
    /// </summary>
    [RelayCommand]
    private void RefreshParameters()
    {
        Parameters = ArgsBuilder.Build(DownloadOptions);
        OnPropertyChanged(nameof(Parameters));
    }

    /// <summary>
    /// Start download process.
    /// </summary>
    [RelayCommand]
    private async Task StartDownloadAsync()
    {
        if (string.IsNullOrWhiteSpace(DownloadOptions.Input))
        {
            MessageBox.Show("Please enter a URL to download", "Error", 
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        try
        {
            IsDownloading = true;
            Progress = 0;
            lock (_logLock)
            {
                _logBuilder.Clear();
                _logBuilder.AppendLine("Starting download...");
            }
            LogOutput = "Starting download...\n";

            var progress = new Progress<int>(value => Progress = value);
            var logCallback = new Action<string>(message => 
            {
                lock (_logLock)
                {
                    _logBuilder.AppendLine(message);
                    if (!_isLogUpdatePending)
                    {
                        _isLogUpdatePending = true;
                        Task.Run(async () => 
                        {
                            await Task.Delay(100);
                            string text;
                            lock (_logLock)
                            {
                                text = _logBuilder.ToString();
                                _isLogUpdatePending = false;
                            }
                            UpdateLogOutput(text);
                        });
                    }
                }
            });

            var success = await _downloadService.StartDownloadAsync(
                DownloadOptions, 
                progress, 
                logCallback);

            if (!success)
            {
                MessageBox.Show("Download failed", "Error", 
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        catch (Exception ex)
        {
            lock (_logLock) { _logBuilder.AppendLine($"Error: {ex.Message}"); }
            UpdateLogOutput(_logBuilder.ToString());
            MessageBox.Show($"Error: {ex.Message}", "Error",  
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            IsDownloading = false;
        }
    }

    /// <summary>
    /// Stop download process.
    /// </summary>
    [RelayCommand]
    private void StopDownload()
    {
        _downloadService.StopDownload();
        IsDownloading = false;
        lock (_logLock) { _logBuilder.AppendLine("Download stopped."); }
        UpdateLogOutput(_logBuilder.ToString());
    }

    /// <summary>
    /// Clear log output.
    /// </summary>
    [RelayCommand]
    private void ClearLog()
    {
        lock (_logLock) { _logBuilder.Clear(); }
        LogOutput = string.Empty;
    }

    /// <summary>
    /// Reset all options to default values.
    /// </summary>
    [RelayCommand]
    private void ResetOptions()
    {
        DownloadOptions = new DownloadOptions();
        RefreshParameters();
    }

    /// <summary>
    /// Get title from URL.
    /// </summary>
    [RelayCommand]
    private async Task GetTitleFromUrlAsync()
    {
        if (string.IsNullOrWhiteSpace(DownloadOptions.Input))
            return;

        try
        {
            var title = await _utilityService.GetTitleFromUrlAsync(DownloadOptions.Input);
            if (!string.IsNullOrWhiteSpace(title))
            {
                DownloadOptions.SaveName = title;
                RefreshParameters();
            }
        }
        catch (Exception ex)
        {
            lock (_logLock) { _logBuilder.AppendLine($"Failed to get title from URL: {ex.Message}"); }
            UpdateLogOutput(_logBuilder.ToString());
        }
    }

    private void UpdateLogOutput(string text)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher != null)
        {
            dispatcher.InvokeAsync(() => LogOutput = text);
        }
        else
        {
            LogOutput = text;
        }
    }

    /// <summary>
    /// Select working directory.
    /// </summary>
    [RelayCommand]
    private void SelectWorkingDirectory()
    {
        var selectedPath = _utilityService.SelectFolder("Select download folder", WorkingDirectory);
        if (!string.IsNullOrWhiteSpace(selectedPath))
        {
            WorkingDirectory = selectedPath;
            DownloadOptions.SaveDir = selectedPath;
            RefreshParameters();
        }
    }

    /// <summary>
    /// Handle URL drop event.
    /// </summary>
    [RelayCommand]
    private void HandleUrlDrop(object data)
    {
        var droppedText = _dragDropService.HandleFileDrop(data);
        if (!string.IsNullOrWhiteSpace(droppedText))
        {
            DownloadOptions.Input = droppedText;
            RefreshParameters();
        }
    }

    /// <summary>
    /// Handle key file drop event.
    /// </summary>
    [RelayCommand]
    private void HandleKeyDrop(object data)
    {
        var droppedFile = _dragDropService.HandleFileDrop(data);
        if (!string.IsNullOrWhiteSpace(droppedFile) && _utilityService.FileExists(droppedFile))
        {
            DownloadOptions.Key = droppedFile;
            RefreshParameters();
        }
    }

    /// <summary>
    /// Handle mux JSON drop event.
    /// </summary>
    [RelayCommand]
    private void HandleMuxJsonDrop(object data)
    {
        var droppedFile = _dragDropService.HandleFileDrop(data);
        if (!string.IsNullOrWhiteSpace(droppedFile) && _utilityService.FileExists(droppedFile))
        {
            DownloadOptions.MuxImport = droppedFile;
            RefreshParameters();
        }
    }

    /// <summary>
    /// Property changed handler for DownloadOptions.
    /// </summary>
    protected override void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        
        // Auto-refresh parameters when download options change
        if (e.PropertyName == nameof(DownloadOptions))
        {
            RefreshParameters();
        }
    }
} 
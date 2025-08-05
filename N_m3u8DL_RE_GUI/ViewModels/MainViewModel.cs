using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using N_m3u8DL_RE_GUI.Core;
using System.ComponentModel;
using System.Windows;
using MessageBox = System.Windows.MessageBox;

namespace N_m3u8DL_RE_GUI.ViewModels;

/// <summary>
/// Main ViewModel for the application using MVVM pattern.
/// </summary>
public partial class MainViewModel : ObservableObject
{
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
            MessageBox.Show("กรุณาใส่ URL ที่ต้องการดาวน์โหลด", "ข้อผิดพลาด", 
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        try
        {
            IsDownloading = true;
            Progress = 0;
            LogOutput = "เริ่มการดาวน์โหลด...\n";
            
            // TODO: Implement actual download logic here
            // For now, just simulate the process
            await SimulateDownloadAsync();
            
            LogOutput += "ดาวน์โหลดเสร็จสิ้น!\n";
        }
        catch (Exception ex)
        {
            LogOutput += $"เกิดข้อผิดพลาด: {ex.Message}\n";
            MessageBox.Show($"เกิดข้อผิดพลาด: {ex.Message}", "ข้อผิดพลาด", 
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
        IsDownloading = false;
        LogOutput += "หยุดการดาวน์โหลด\n";
    }

    /// <summary>
    /// Clear log output.
    /// </summary>
    [RelayCommand]
    private void ClearLog()
    {
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
    /// Simulate download process for demonstration.
    /// </summary>
    private async Task SimulateDownloadAsync()
    {
        for (int i = 0; i <= 100; i += 10)
        {
            if (!IsDownloading) break;
            
            Progress = i;
            LogOutput += $"ดาวน์โหลด... {i}%\n";
            await Task.Delay(500);
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
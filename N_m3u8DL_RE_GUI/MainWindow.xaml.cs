#nullable enable
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Application = System.Windows.Application;
using Clipboard = System.Windows.Clipboard;
using DataFormats = System.Windows.DataFormats;
using DragDropEffects = System.Windows.DragDropEffects;
using MessageBox = System.Windows.MessageBox;
using Path = System.IO.Path;
using TextBox = System.Windows.Controls.TextBox;
using WpfComboBox = System.Windows.Controls.ComboBox;
using Forms = System.Windows.Forms;
using Media = System.Windows.Media;
using MediaColor = System.Windows.Media.Color;
using Anim = System.Windows.Media.Animation;
using N_m3u8DL_RE_GUI.Core;
using Services = N_m3u8DL_RE_GUI.Services;

namespace N_m3u8DL_RE_GUI
{
    /// <summary>
    /// MainWindow.xaml interaction logic.
    /// 
    /// Changelog:
    /// 2019-06-17: Refactored UI and fixed iQiyi title extraction bug
    /// 2019-06-18: Added application icon
    /// 2019-06-23: Improved executable search logic, URL regex matching,
    ///             auto-detect URL and title on startup, focus on URL textbox,
    ///             Enter key support for URL/title fields, ALT+S shortcut for GO button
    /// 2019-07-24: Optimized video title extraction, added downloadRange parameter
    /// 2019-08-11: Batch txt supports custom filenames
    /// 2019-08-17: Added iQiyi DASH direct download, fixed Tencent Video title bug
    /// 2019-09-18: Added speed limit, new UI design, control tooltips
    /// 2019-09-28: URL comparison before assignment on double-click
    /// 2019-10-09: Auto-detect file encoding
    /// 2019-10-24: Read iqiyicookie.txt for DASH requests
    /// 2019-12-16: Skip empty lines in batch txt, Tencent Unicode conversion
    /// 2020-02-01: Fixed WeTV title detection issues
    /// 2020-02-17: Auto-name from meta.json, KEY file validation, resizable window
    /// 2020-04-17: Changed BAT encoding to UTF-8
    /// 2020-11-21: UI fixes
    /// 2021-01-24: Multi-language support (CN/TW/EN)
    /// 2021-03-04: Proxy settings, save proxy and headers
    /// 2021-03-21: MPD batch support
    /// </summary>
    public partial class MainWindow : Window
    {
        private readonly Services.IUtilityService _utilityService;
        private readonly Services.IConfigService _configService;
        private readonly Services.IBatchScriptService _batchScriptService;
        private readonly Services.IDragDropService _dragDropService;
        private readonly Services.IDownloadService _downloadService;
        private bool _suspendParameterRefresh;
        private bool _isCheckingUpdate;
        // One token source for whatever long-running operation is currently cancellable.
        // Each operation creates its own, publishes it here for Button_Stop, and clears
        // the field only if it is still the owner. Sharing a single field across the
        // async void handlers previously let one flow dispose another's live token.
        private System.Threading.CancellationTokenSource? _activeOperationCts;
        // Captured from the XAML so the batch flow can restore the real label (icon and
        // access key included) instead of hard-coding a second, drifting copy of it.
        private object? _downloadButtonLabel;
        private static readonly Media.SolidColorBrush ErrorBorderBrush = CreateFrozenBrush(MediaColor.FromRgb(231, 76, 60));
        // DefaultBorderBrush is gone: the resting border now comes from TextBoxStyle, which
        // is the only place it should ever have been defined.

        /// <summary>
        /// Creates a token source for a new cancellable operation and publishes it so
        /// Button_Stop can reach it. Cancels any operation already in flight.
        /// </summary>
        private System.Threading.CancellationTokenSource BeginCancellableOperation()
        {
            var previous = _activeOperationCts;
            var cts = new System.Threading.CancellationTokenSource();
            _activeOperationCts = cts;

            if (previous != null)
            {
                try { previous.Cancel(); } catch (ObjectDisposedException) { }
                try { previous.Dispose(); } catch (ObjectDisposedException) { }
            }

            return cts;
        }

        /// <summary>Retires a token source, clearing the shared field only if we still own it.</summary>
        private void EndCancellableOperation(System.Threading.CancellationTokenSource cts)
        {
            if (ReferenceEquals(_activeOperationCts, cts))
                _activeOperationCts = null;

            try { cts.Dispose(); } catch (ObjectDisposedException) { }
        }

        /// <summary>Alt+S / Enter — routed to Button_GO_Click, the real download path.</summary>
        public static readonly RoutedUICommand StartDownloadRoutedCommand =
            new("Start Download", nameof(StartDownloadRoutedCommand), typeof(MainWindow));

        /// <summary>Escape — routed to Button_Stop_Click.</summary>
        public static readonly RoutedUICommand StopDownloadRoutedCommand =
            new("Stop Download", nameof(StopDownloadRoutedCommand), typeof(MainWindow));

        private static Media.SolidColorBrush CreateFrozenBrush(MediaColor color)
        {
            var brush = new Media.SolidColorBrush(color);
            if (brush.CanFreeze) brush.Freeze();
            return brush;
        }

        public MainWindow()
        {
            InitializeComponent();
            _downloadButtonLabel = Button_GO.Content;

            CommandBindings.Add(new CommandBinding(
                StartDownloadRoutedCommand,
                (_, _) => Button_GO_Click(Button_GO, new RoutedEventArgs()),
                (_, e) => e.CanExecute = IsEnabled && Button_GO.IsEnabled));

            CommandBindings.Add(new CommandBinding(
                StopDownloadRoutedCommand,
                (_, _) => Button_Stop_Click(Button_Stop, new RoutedEventArgs()),
                (_, e) => e.CanExecute = Button_Stop.Visibility == Visibility.Visible));

            TextBox_URL.Focus();
            var serviceProvider = ViewModels.ViewModelLocator.ServiceProvider;
            _utilityService = Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<Services.IUtilityService>(serviceProvider);
            _configService = Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<Services.IConfigService>(serviceProvider);
            _batchScriptService = Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<Services.IBatchScriptService>(serviceProvider);
            _dragDropService = Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<Services.IDragDropService>(serviceProvider);
            _downloadService = Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<Services.IDownloadService>(serviceProvider);
        }

        private void Button_SelectDir_Click(object sender, RoutedEventArgs e)
        {
            var selectedPath = _utilityService.SelectFolder("Choose a folder — downloads will be saved here");
            if (!string.IsNullOrEmpty(selectedPath))
            {
                TextBox_WorkDir.Text = selectedPath;
            }
        }



        private void GetParameter()
        {
            if (_suspendParameterRefresh || TextBox_Parameter == null) return;
            // In Cloudflare bypass mode, preview the python command instead of N_m3u8DL-RE args
            if (CheckBox_BypassCF?.IsChecked == true)
                TextBox_Parameter.Text = CfCommandBuilder.BuildCommand(BuildCfOptions());
            else
                TextBox_Parameter.Text = BuildArgsRE(TextBox_URL.Text);
        }

        private void ApplyValidationState(TextBox? textBox, bool isValid)
        {
            if (textBox == null)
                return;
            // Tag, not BorderBrush: see the "invalid" trigger in TextBoxStyle. Writing
            // BorderBrush here set a local value that outranked the style's IsFocused
            // trigger forever, leaving the primary URL field with no focus indicator.
            textBox.Tag = isValid ? null : "invalid";
        }

        private void RefreshValidationState(object? sender = null)
        {
            if (sender == null || sender == TextBox_URL)
                ApplyValidationState(TextBox_URL, TextBox_URL == null || InputValidation.IsLikelyValidInput(TextBox_URL.Text));
            if (sender == null || sender == TextBox_Proxy)
                ApplyValidationState(TextBox_Proxy, TextBox_Proxy == null || InputValidation.IsValidProxy(TextBox_Proxy.Text));
            if (sender == null || sender == TextBox_EXE)
                ApplyValidationState(TextBox_EXE, TextBox_EXE == null || string.IsNullOrWhiteSpace(TextBox_EXE.Text) || File.Exists(TextBox_EXE.Text));
        }

        string BuildArgsRE(string? inputOverride = null)
        {
            var options = new DownloadOptions
            {
                // Basic Settings
                Input = string.IsNullOrWhiteSpace(inputOverride) ? TextBox_URL.Text : inputOverride,
                SaveDir = OptionValueNormalizer.NormalizeSaveDir(TextBox_WorkDir.Text),
                TmpDir = TextBox_TmpDir?.Text?.Trim(),
                SaveName = TextBox_Title.Text,
                Headers = TextBox_Headers.Text,
                BaseUrl = TextBox_Baseurl.Text,
                MuxImport = TextBox_MuxJson.Text?.Trim(),
                
                // Encryption
                Key = TextBox_Key.Text?.Trim(),
                CustomHLSKey = TextBox_CustomHLSKey?.Text?.Trim(),
                CustomHLSIv = TextBox_IV.Text?.Trim(),
                KeyTextFile = TextBox_KeyTextFile?.Text?.Trim(),
                DecryptionEngine = (Combo_DecryptionEngine?.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "MP4DECRYPT",
                DecryptionBinaryPath = TextBox_DecryptionBinPath?.Text?.Trim(),
                MP4RealTimeDecryption = CheckBox_MP4RealTimeDecryption?.IsChecked == true,
                CustomHLSMethod = GetComboValue(Combo_CustomHLSMethod),
                
                // Network
                Proxy = TextBox_Proxy.Text?.Trim(),
                UseSystemProxy = CheckBox_DisableProxy?.IsChecked != true,
                
                // Time Range
                RangeStart = TextBox_RangeStart.Text,
                RangeEnd = TextBox_RangeEnd.Text,
                
                // Thread Settings
                ThreadCount = int.TryParse(TextBox_Max.Text, out var threadCount) ? threadCount : Environment.ProcessorCount,
                DownloadRetryCount = int.TryParse(TextBox_Retry.Text, out var retryCount) ? retryCount : 3,
                
                // Timeout & Speed
                HttpRequestTimeout = int.TryParse(TextBox_Timeout.Text, out var timeout) ? timeout : 100,
                MaxSpeed = TextBox_MaxSpeed.Text?.Trim(),
                
                // Boolean Options (original)
                DelAfterDone = CheckBox_Del.IsChecked == true,
                NoDateInfo = CheckBox_DisableDate.IsChecked == true,
                SkipDownload = CheckBox_ParserOnly.IsChecked == true,
                SkipMerge = CheckBox_DisableMerge.IsChecked == true,
                BinaryMerge = CheckBox_BinaryMerge.IsChecked == true,
                CheckSegmentsCount = CheckBox_DisableCheck?.IsChecked != true,
                ConcurrentDownload = CheckBox_Concurrent?.IsChecked == true,
                SubOnly = CheckBox_SubOnly?.IsChecked == true,
                AutoSubtitleFix = CheckBox_AutoSubFix?.IsChecked == true,
                AutoSelect = CheckBox_AutoSelect?.IsChecked == true,
                
                // Subtitle Format
                SubFormat = (Combo_SubFormat?.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "SRT",
                
                // Mux After Done
                MuxAfterDone = CheckBox_MuxAfterDone?.IsChecked == true,
                MuxFormat = (Combo_MuxFormat?.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "mp4",
                Muxer = (Combo_Muxer?.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "ffmpeg",
                MuxBinPath = TextBox_MuxBinPath?.Text?.Trim(),
                MuxKeepFiles = CheckBox_MuxKeepFiles?.IsChecked == true,
                MuxSkipSubtitle = CheckBox_MuxSkipSub?.IsChecked == true,
                
                // Live Recording
                LivePerformAsVod = CheckBox_LivePerformAsVod?.IsChecked == true,
                LiveRealTimeMerge = CheckBox_LiveRealTimeMerge?.IsChecked == true,
                LiveKeepSegments = CheckBox_LiveKeepSegments?.IsChecked != false,
                LivePipeMux = CheckBox_LivePipeMux?.IsChecked == true,
                LiveFixVttByAudio = CheckBox_LiveFixVttByAudio?.IsChecked == true,
                LiveRecordLimit = TextBox_LiveRecordLimit?.Text?.Trim(),
                LiveWaitTime = int.TryParse(TextBox_LiveWaitTime?.Text, out var waitTime) ? waitTime : null,
                LiveTakeCount = int.TryParse(TextBox_LiveTakeCount?.Text, out var takeCount) ? takeCount : 16,
                
                // Stream Selection
                SelectVideo = TextBox_SelectVideo?.Text?.Trim(),
                SelectAudio = CheckBox_AudioOnly?.IsChecked == true 
                    ? (string.IsNullOrWhiteSpace(TextBox_SelectAudio?.Text) ? "best" : TextBox_SelectAudio.Text.Trim()) 
                    : TextBox_SelectAudio?.Text?.Trim(),
                SelectSubtitle = TextBox_SelectSubtitle?.Text?.Trim(),
                DropVideo = CheckBox_AudioOnly?.IsChecked == true ? ".*" : TextBox_DropVideo?.Text?.Trim(),
                DropAudio = TextBox_DropAudio?.Text?.Trim(),
                DropSubtitle = TextBox_DropSubtitle?.Text?.Trim(),
                
                // Advanced Settings
                SavePattern = TextBox_SavePattern?.Text?.Trim(),
                LogFilePath = TextBox_LogFilePath?.Text?.Trim(),
                FFmpegBinaryPath = TextBox_FFmpegPath?.Text?.Trim(),
                MkvmergeBinaryPath = string.Equals((Combo_Muxer?.SelectedItem as ComboBoxItem)?.Content?.ToString(), "mkvmerge", StringComparison.OrdinalIgnoreCase) ? TextBox_MuxBinPath?.Text?.Trim() : null,
                AdKeyword = TextBox_AdKeyword?.Text?.Trim(),
                UrlProcessorArgs = TextBox_UrlProcessorArgs?.Text?.Trim(),
                TaskStartAt = TextBox_TaskStartAt?.Text?.Trim(),
                LogLevel = (Combo_LogLevel?.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "INFO",
                UILanguage = GetComboValue(Combo_UILanguage),
                AppendUrlParams = CheckBox_AppendUrlParams?.IsChecked == true,
                NoLog = CheckBox_NoLog?.IsChecked == true,
                WriteMetaJson = CheckBox_WriteMetaJson?.IsChecked != false,
                UseFFmpegConcatDemuxer = CheckBox_UseFFmpegConcat?.IsChecked == true,
                AllowHlsMultiExtMap = CheckBox_AllowHlsMultiExtMap?.IsChecked == true,
                ForceAnsiConsole = CheckBox_ForceAnsiConsole?.IsChecked == true,
                // Forced on: the GUI parses redirected output, and escape sequences make
                // it unreadable. ponytail: CheckBox_NoAnsiColor is now vestigial — remove
                // it from the XAML in the IA pass.
                NoAnsiColor = true,
                DisableUpdateCheck = CheckBox_DisableUpdateCheck?.IsChecked == true,
            };

            return ArgsBuilder.Build(options);
        }

        /// <summary>
        /// Build the full DownloadOptions object from current UI state, including ExePath.
        /// Used by StartDownloadAsync so IDownloadService manages the process lifecycle.
        /// </summary>
        private DownloadOptions BuildDownloadOptions()
        {
            return new DownloadOptions
            {
                // EXE path — lets DownloadService use the GUI-configured binary
                ExePath = string.IsNullOrWhiteSpace(TextBox_EXE?.Text) ? null : TextBox_EXE.Text.Trim(),

                // Basic Settings
                Input = TextBox_URL.Text,
                SaveDir = OptionValueNormalizer.NormalizeSaveDir(TextBox_WorkDir.Text),
                TmpDir = TextBox_TmpDir?.Text?.Trim(),
                SaveName = TextBox_Title.Text,
                Headers = TextBox_Headers.Text,
                BaseUrl = TextBox_Baseurl.Text,
                MuxImport = TextBox_MuxJson.Text?.Trim(),

                // Encryption
                Key = TextBox_Key.Text?.Trim(),
                CustomHLSKey = TextBox_CustomHLSKey?.Text?.Trim(),
                CustomHLSIv = TextBox_IV.Text?.Trim(),
                KeyTextFile = TextBox_KeyTextFile?.Text?.Trim(),
                DecryptionEngine = (Combo_DecryptionEngine?.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "MP4DECRYPT",
                DecryptionBinaryPath = TextBox_DecryptionBinPath?.Text?.Trim(),
                MP4RealTimeDecryption = CheckBox_MP4RealTimeDecryption?.IsChecked == true,
                CustomHLSMethod = GetComboValue(Combo_CustomHLSMethod),

                // Network
                Proxy = TextBox_Proxy.Text?.Trim(),
                UseSystemProxy = CheckBox_DisableProxy?.IsChecked != true,

                // Time Range
                RangeStart = TextBox_RangeStart.Text,
                RangeEnd = TextBox_RangeEnd.Text,

                // Thread Settings
                ThreadCount = int.TryParse(TextBox_Max.Text, out var threadCount) ? threadCount : Environment.ProcessorCount,
                DownloadRetryCount = int.TryParse(TextBox_Retry.Text, out var retryCount) ? retryCount : 3,

                // Timeout & Speed
                HttpRequestTimeout = int.TryParse(TextBox_Timeout.Text, out var timeout) ? timeout : 100,
                MaxSpeed = TextBox_MaxSpeed.Text?.Trim(),

                // Boolean Options
                DelAfterDone = CheckBox_Del.IsChecked == true,
                NoDateInfo = CheckBox_DisableDate.IsChecked == true,
                SkipDownload = CheckBox_ParserOnly.IsChecked == true,
                SkipMerge = CheckBox_DisableMerge.IsChecked == true,
                BinaryMerge = CheckBox_BinaryMerge.IsChecked == true,
                CheckSegmentsCount = CheckBox_DisableCheck?.IsChecked != true,
                ConcurrentDownload = CheckBox_Concurrent?.IsChecked == true,
                SubOnly = CheckBox_SubOnly?.IsChecked == true,
                AutoSubtitleFix = CheckBox_AutoSubFix?.IsChecked == true,
                AutoSelect = CheckBox_AutoSelect?.IsChecked == true,

                // Subtitle Format
                SubFormat = (Combo_SubFormat?.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "SRT",

                // Mux After Done
                MuxAfterDone = CheckBox_MuxAfterDone?.IsChecked == true,
                MuxFormat = (Combo_MuxFormat?.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "mp4",
                Muxer = (Combo_Muxer?.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "ffmpeg",
                MuxBinPath = TextBox_MuxBinPath?.Text?.Trim(),
                MuxKeepFiles = CheckBox_MuxKeepFiles?.IsChecked == true,
                MuxSkipSubtitle = CheckBox_MuxSkipSub?.IsChecked == true,

                // Live Recording
                LivePerformAsVod = CheckBox_LivePerformAsVod?.IsChecked == true,
                LiveRealTimeMerge = CheckBox_LiveRealTimeMerge?.IsChecked == true,
                LiveKeepSegments = CheckBox_LiveKeepSegments?.IsChecked != false,
                LivePipeMux = CheckBox_LivePipeMux?.IsChecked == true,
                LiveFixVttByAudio = CheckBox_LiveFixVttByAudio?.IsChecked == true,
                LiveRecordLimit = TextBox_LiveRecordLimit?.Text?.Trim(),
                LiveWaitTime = int.TryParse(TextBox_LiveWaitTime?.Text, out var waitTime) ? waitTime : null,
                LiveTakeCount = int.TryParse(TextBox_LiveTakeCount?.Text, out var takeCount) ? takeCount : 16,

                // Stream Selection
                SelectVideo = TextBox_SelectVideo?.Text?.Trim(),
                SelectAudio = CheckBox_AudioOnly?.IsChecked == true
                    ? (string.IsNullOrWhiteSpace(TextBox_SelectAudio?.Text) ? "best" : TextBox_SelectAudio.Text.Trim())
                    : TextBox_SelectAudio?.Text?.Trim(),
                SelectSubtitle = TextBox_SelectSubtitle?.Text?.Trim(),
                DropVideo = CheckBox_AudioOnly?.IsChecked == true ? ".*" : TextBox_DropVideo?.Text?.Trim(),
                DropAudio = TextBox_DropAudio?.Text?.Trim(),
                DropSubtitle = TextBox_DropSubtitle?.Text?.Trim(),

                // Advanced Settings
                SavePattern = TextBox_SavePattern?.Text?.Trim(),
                LogFilePath = TextBox_LogFilePath?.Text?.Trim(),
                FFmpegBinaryPath = TextBox_FFmpegPath?.Text?.Trim(),
                MkvmergeBinaryPath = string.Equals((Combo_Muxer?.SelectedItem as ComboBoxItem)?.Content?.ToString(), "mkvmerge", StringComparison.OrdinalIgnoreCase) ? TextBox_MuxBinPath?.Text?.Trim() : null,
                AdKeyword = TextBox_AdKeyword?.Text?.Trim(),
                UrlProcessorArgs = TextBox_UrlProcessorArgs?.Text?.Trim(),
                TaskStartAt = TextBox_TaskStartAt?.Text?.Trim(),
                LogLevel = (Combo_LogLevel?.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "INFO",
                UILanguage = GetComboValue(Combo_UILanguage),
                AppendUrlParams = CheckBox_AppendUrlParams?.IsChecked == true,
                NoLog = CheckBox_NoLog?.IsChecked == true,
                WriteMetaJson = CheckBox_WriteMetaJson?.IsChecked != false,
                UseFFmpegConcatDemuxer = CheckBox_UseFFmpegConcat?.IsChecked == true,
                AllowHlsMultiExtMap = CheckBox_AllowHlsMultiExtMap?.IsChecked == true,
                // Forced on: the GUI parses redirected output, and escape sequences make
                // it unreadable. ponytail: CheckBox_NoAnsiColor is now vestigial — remove
                // it from the XAML in the IA pass.
                NoAnsiColor = true,
                DisableUpdateCheck = CheckBox_DisableUpdateCheck?.IsChecked == true,
            };
        }

        /// <summary>
        /// Get ComboBox selected value, returning null for "(Default)" or empty selections.
        /// </summary>
        private static string? GetComboValue(WpfComboBox? combo)
        {
            var value = (combo?.SelectedItem as ComboBoxItem)?.Content?.ToString();
            return value == "(Default)" || string.IsNullOrWhiteSpace(value) ? null : value;
        }


        private void TextChanged(object sender, TextChangedEventArgs e)
        {
            RefreshValidationState(sender);
            GetParameter();
        }

        private void CheckBoxChanged(object sender, RoutedEventArgs e)
        {
            SyncDependentControlStates();
            GetParameter();
        }

        /// <summary>
        /// Reflects option dependencies in the UI instead of silently overriding them at
        /// build time. Every field disabled here is one BuildArgsRE would otherwise
        /// discard while the user kept looking at the value they typed.
        /// </summary>
        private void SyncDependentControlStates()
        {
            var audioOnly = CheckBox_AudioOnly?.IsChecked == true;
            if (TextBox_SelectAudio != null)
            {
                TextBox_SelectAudio.IsEnabled = !audioOnly;
                TextBox_SelectAudio.ToolTip = audioOnly
                    ? "Overridden by Audio Only, which forces the best audio track."
                    : "Regex selecting which audio track to download";
            }
            if (TextBox_DropVideo != null)
            {
                TextBox_DropVideo.IsEnabled = !audioOnly;
                TextBox_DropVideo.ToolTip = audioOnly
                    ? "Overridden by Audio Only, which drops every video track."
                    : "Regex selecting which video tracks to discard";
            }

            var bypassCf = CheckBox_BypassCF?.IsChecked == true;
            if (Border_CfScopeWarning != null)
                Border_CfScopeWarning.Visibility = bypassCf ? Visibility.Visible : Visibility.Collapsed;

            // The dependent CF fields are meaningless until the mode is on.
            foreach (var control in new System.Windows.Controls.Control?[]
                     { Combo_CFImpersonate, TextBox_CFReferer, TextBox_CFCookie, CheckBox_CFKeepSegs })
            {
                if (control != null)
                    control.IsEnabled = bypassCf;
            }
        }

        private void Combo_SubFormat_SelectionChanged(object sender, SelectionChangedEventArgs e) => GetParameter();
        private void Combo_MuxFormat_SelectionChanged(object sender, SelectionChangedEventArgs e) => GetParameter();
        private void Combo_Muxer_SelectionChanged(object sender, SelectionChangedEventArgs e) => GetParameter();
        private void Combo_DecryptionEngine_SelectionChanged(object sender, SelectionChangedEventArgs e) => GetParameter();
        private void Combo_HLSMethod_SelectionChanged(object sender, SelectionChangedEventArgs e) => GetParameter();
        private void Combo_LogLevel_SelectionChanged(object sender, SelectionChangedEventArgs e) => GetParameter();
        private void Combo_UILanguage_SelectionChanged(object sender, SelectionChangedEventArgs e) => GetParameter();
        private void Combo_CFImpersonate_SelectionChanged(object sender, SelectionChangedEventArgs e) => GetParameter();

        private void FlashTextBox(TextBox textBox)
        {
            var orgBrush = textBox.Background as Media.SolidColorBrush;
            var originalColor = orgBrush?.Color ?? Media.Colors.White;

            var animatedBrush = new Media.SolidColorBrush(originalColor);
            textBox.Background = animatedBrush;

            var toGreen = new Anim.ColorAnimation
            {
                To = (MediaColor)Media.ColorConverter.ConvertFromString("#2ecc71"),
                Duration = TimeSpan.FromMilliseconds(300)
            };

            var backToOriginal = new Anim.ColorAnimation
            {
                To = originalColor,
                BeginTime = TimeSpan.FromMilliseconds(300),
                Duration = TimeSpan.FromMilliseconds(1000)
            };

            var sb = new Anim.Storyboard();
            sb.Children.Add(toGreen);
            sb.Children.Add(backToOriginal);

            Anim.Storyboard.SetTarget(toGreen, animatedBrush);
            Anim.Storyboard.SetTargetProperty(toGreen, new PropertyPath(Media.SolidColorBrush.ColorProperty));
            Anim.Storyboard.SetTarget(backToOriginal, animatedBrush);
            Anim.Storyboard.SetTargetProperty(backToOriginal, new PropertyPath(Media.SolidColorBrush.ColorProperty));

            sb.Begin();
        }


        private void TextBox_URL_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            // Read URL from clipboard on double-click
            string str = InputValidation.ExtractFirstUrl(SafeGetClipboardText());
            if (str != "" && str != TextBox_URL.Text)
            {
                TextBox_URL.Text = str;
                FlashTextBox(TextBox_URL);
            }
        }


        private async void TextBox_Title_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (!string.IsNullOrEmpty(TextBox_URL.Text))
                await PopulateTitleForInputAsync(TextBox_URL.Text, clearWhenUnknown: false);
        }

        private async Task PopulateTitleForInputAsync(string input, bool clearWhenUnknown)
        {
            if (string.IsNullOrWhiteSpace(input))
                return;

            if (InputValidation.IsHttpUrl(input))
            {
                var cts = BeginCancellableOperation();
                try
                {
                    TextBox_Title.Text = await _utilityService.GetTitleFromUrlAsync(input, cts.Token);
                }
                catch (OperationCanceledException)
                {
                }
                finally
                {
                    EndCancellableOperation(cts);
                }
                return;
            }

            if (File.Exists(input) && DropInputRules.ShouldAutoFillTitleFromFileName(input))
            {
                TextBox_Title.Text = Path.GetFileNameWithoutExtension(input);
                return;
            }

            if (clearWhenUnknown)
                TextBox_Title.Text = string.Empty;
        }

        private static bool HasFileDropData(System.Windows.DragEventArgs e)
        {
            return e.Data.GetDataPresent(DataFormats.FileDrop, false);
        }

        private static void MarkDragCopy(System.Windows.DragEventArgs e)
        {
            e.Effects = DragDropEffects.Copy;
            e.Handled = true;
        }

        private bool TryGetFirstDroppedPath(System.Windows.DragEventArgs e, out string path)
        {
            path = string.Empty;
            if (!HasFileDropData(e))
                return false;

            var droppedPaths = _dragDropService.GetFilePaths(e.Data);
            if (droppedPaths.Length == 0 || string.IsNullOrWhiteSpace(droppedPaths[0]))
                return false;

            path = droppedPaths[0];
            return true;
        }

        private void TextBox_URL_PreviewDragOver(object sender, System.Windows.DragEventArgs e)
        {
            if (HasFileDropData(e))
                MarkDragCopy(e);
        }

        private void TextBox_URL_PreviewDragEnter(object sender, System.Windows.DragEventArgs e)
        {
            if (HasFileDropData(e))
                MarkDragCopy(e);
        }

        private void TextBox_URL_PreviewDrop(object sender, System.Windows.DragEventArgs e)
        {
            if (TryGetFirstDroppedPath(e, out var path) && DropInputRules.IsSupportedUrlInputPath(path))
            {
                MarkDragCopy(e);
                if (TextBox_URL.Text != path) FlashTextBox(TextBox_URL);
                TextBox_URL.Text = path;
                if (DropInputRules.ShouldAutoFillTitleFromFileName(path))
                    TextBox_Title.Text = Path.GetFileNameWithoutExtension(path);
            }
        }

        private void TextBox_MuxJson_PreviewDragEnter(object sender, System.Windows.DragEventArgs e)
        {
            if (HasFileDropData(e))
                MarkDragCopy(e);
        }

        private void TextBox_MuxJson_PreviewDragOver(object sender, System.Windows.DragEventArgs e)
        {
            if (HasFileDropData(e))
                MarkDragCopy(e);
        }

        private void TextBox_MuxJson_PreviewDrop(object sender, System.Windows.DragEventArgs e)
        {
            if (TryGetFirstDroppedPath(e, out var path) && DropInputRules.IsValidMuxImportPath(path))
            {
                MarkDragCopy(e);
                if (TextBox_MuxJson.Text != path) FlashTextBox(TextBox_MuxJson);
                TextBox_MuxJson.Text = path;
            }
        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            var state = Services.MainWindowConfigMapper.Capture(this);
            _configService.Save("config.txt", state);
        }

        private async void Window_Loaded(object sender, RoutedEventArgs e)
        {
            _suspendParameterRefresh = true;
            try
            {
                SetCurrentDirectoryToAppBase();
                var config = _configService.Load("config.txt");
                Services.MainWindowConfigMapper.Restore(this, config);

                if (!File.Exists(TextBox_EXE.Text))
                {
                    var currentDir = Environment.CurrentDirectory;
                    if (!string.IsNullOrEmpty(currentDir))
                    {
                        var d = new DirectoryInfo(currentDir);
                        var re = d.GetFiles("N_m3u8DL-RE.exe").FirstOrDefault();
                        if (re != null) TextBox_EXE.Text = re.FullName;
                    }
                }

                var commandLineArgs = Environment.GetCommandLineArgs();
                if (commandLineArgs.Length > 1)
                {
                    var startupInput = commandLineArgs[1];
                    if (InputValidation.IsSupportedStartupInputArgument(startupInput))
                        TextBox_URL.Text = startupInput;
                    if (TextBox_URL.Text != "")
                    {
                        FlashTextBox(TextBox_URL);
                        await PopulateTitleForInputAsync(TextBox_URL.Text, clearWhenUnknown: true);
                    }
                }
                else
                {
                    string str = InputValidation.ExtractFirstUrl(SafeGetClipboardText());
                    TextBox_URL.Text = str;
                    if (TextBox_URL.Text != "")
                    {
                        FlashTextBox(TextBox_URL);
                        await PopulateTitleForInputAsync(TextBox_URL.Text, clearWhenUnknown: false);
                    }
                }
            }
            finally
            {
                _suspendParameterRefresh = false;
                RefreshValidationState();
                SyncDependentControlStates();
                ClampToWorkArea();
                GetParameter();

                _ = Task.Run(() => CleanStaleTempBatchFiles());

                if (CheckBox_AutoCheckGuiUpdate?.IsChecked == true)
                {
                    _ = CheckGuiUpdateAsync(isManual: false);
                }
            }
        }

        private static void SetCurrentDirectoryToAppBase()
        {
            var baseDirectory = AppContext.BaseDirectory;
            if (!string.IsNullOrWhiteSpace(baseDirectory) && Directory.Exists(baseDirectory))
            {
                Environment.CurrentDirectory = baseDirectory;
                return;
            }

            // Fallback for legacy runtime hosts that do not provide AppContext.BaseDirectory.
            var mainModule = Process.GetCurrentProcess().MainModule;
            var legacyExecutablePath = mainModule?.FileName;
            var legacyDirectory = string.IsNullOrWhiteSpace(legacyExecutablePath)
                ? null
                : Path.GetDirectoryName(legacyExecutablePath);
            if (!string.IsNullOrWhiteSpace(legacyDirectory))
                Environment.CurrentDirectory = legacyDirectory;
        }

        /// <summary>
        /// Keeps the window inside the desktop work area. At 150% scaling on a 1080p
        /// display the default height would otherwise put Zone D behind the taskbar.
        /// </summary>
        private void ClampToWorkArea()
        {
            var work = SystemParameters.WorkArea;
            if (ActualHeight > work.Height)
                Height = work.Height;
            if (ActualWidth > work.Width)
                Width = work.Width;
            if (Top + Height > work.Bottom)
                Top = Math.Max(work.Top, work.Bottom - Height);
        }

        private readonly System.Text.StringBuilder _logBuffer = new();
        private string? _lastOutputDirectory;

        private void SetStatus(string text, bool isError = false)
        {
            TextBlock_Status.Text = text;
            TextBlock_Status.Foreground = isError ? ErrorBorderBrush : DefaultStatusBrush;
        }

        private static readonly Media.SolidColorBrush DefaultStatusBrush =
            CreateFrozenBrush(MediaColor.FromRgb(0x88, 0x88, 0xA8));

        private void AppendLog(string message)
        {
            _logBuffer.AppendLine(message);
            TextBox_Log.Text = _logBuffer.ToString();
            TextBox_Log.ScrollToEnd();
        }

        private void ResetRunState()
        {
            _logBuffer.Clear();
            TextBox_Log.Text = string.Empty;
            ProgressBar_Download.Value = 0;
            Button_OpenFolder.Visibility = Visibility.Collapsed;
        }

        private void ToggleButton_Log_Changed(object sender, RoutedEventArgs e)
        {
            TextBox_Log.Visibility = ToggleButton_Log.IsChecked == true
                ? Visibility.Visible
                : Visibility.Collapsed;
        }

        private void Button_OpenFolder_Click(object sender, RoutedEventArgs e)
        {
            if (!string.IsNullOrWhiteSpace(_lastOutputDirectory) && Directory.Exists(_lastOutputDirectory))
                StartShellTarget(_lastOutputDirectory);
        }

        private async void Button_GO_Click(object sender, RoutedEventArgs e)
        {
            // Convert hex key to base64 if applicable
            if (!string.IsNullOrWhiteSpace(TextBox_Key.Text))
            {
                string rawKey = TextBox_Key.Text.Trim();
                if (rawKey.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                {
                    try
                    {
                        string hex = rawKey.Substring(2).Replace("-", "").Replace(" ", "");
                        TextBox_Key.Text = Convert.ToBase64String(Convert.FromHexString(hex));
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Key conversion failed (invalid hex format): {ex.Message}");
                        MessageBox.Show("Invalid Hex Key format. Please check your key.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                        return;
                    }
                }
                else
                {
                    string hex = rawKey.Replace("-", "").Replace(" ", "");
                    if (hex.Length > 0 && hex.Length % 2 == 0 && Regex.IsMatch(hex, @"\A\b[0-9a-fA-F]+\b\Z") && !rawKey.Contains(':') && !rawKey.EndsWith("="))
                    {
                        try
                        {
                            TextBox_Key.Text = Convert.ToBase64String(Convert.FromHexString(hex));
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"Key conversion failed: {ex.Message}");
                            MessageBox.Show("Invalid Hex Key format. Please check your key.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                            return;
                        }
                    }
                }
            }
            // Skip the N_m3u8DL-RE.exe check when using Cloudflare bypass (Python m3u8_cf_bypass.py is used instead)
            if (CheckBox_BypassCF?.IsChecked != true && !File.Exists(TextBox_EXE.Text))
            {
                MessageBox.Show(
                    "N_m3u8DL-RE.exe was not found.\n\n" +
                    "Set its path on the Download tab, or right-click the Executable field and choose Get Downloader.",
                    "Downloader Not Found", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            if (TextBox_URL.Text == "")
            {
                MessageBox.Show(
                    "Enter a URL or file path first.",
                    "Missing Input", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            if (!InputValidation.IsValidProxy(TextBox_Proxy.Text))
            {
                MessageBox.Show(
                    "Proxy must start with http:// or socks5://.",
                    "Invalid Proxy", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // Batch download mode
            if (_batchScriptService.IsBatchInput(TextBox_URL.Text))
            {
                this.IsEnabled = false;
                Button_GO.Content = "Working…";
                Services.BatchScriptBuildResult? result = null;
                var cts = BeginCancellableOperation();
                try
                {
                    var token = cts.Token;
                    result = await _batchScriptService.BuildScriptAsync(
                        inputPath: TextBox_URL.Text,
                        exePath: TextBox_EXE.Text,
                        resolveTitleAsync: url => _utilityService.GetTitleFromUrlAsync(url, token),
                        buildArgsForInput: BuildArgsRE,
                        onTitleResolved: title => TextBox_Title.Text = title,
                        cancellationToken: token);

                    _batchScriptService.SaveScript(result.FilePath, result.Content);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch (Exception ex)
                {
                    MessageBox.Show(ex.Message, "Batch build failed", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }
                finally
                {
                    EndCancellableOperation(cts);
                    Button_GO.Content = _downloadButtonLabel;
                    this.IsEnabled = true;
                }

                if (result != null)
                {
                    Button_GO.IsEnabled = false;
                    Button_Stop.Visibility = Visibility.Visible;

                    _lastOutputDirectory = OptionValueNormalizer.NormalizeSaveDir(TextBox_WorkDir.Text)
                                           ?? Environment.CurrentDirectory;
                    ResetRunState();
                    SetStatus("Running batch…");

                    var batchProgress = new Progress<int>(p => ProgressBar_Download.Value = p);
                    var batchLog = new Action<string>(line => Dispatcher.InvokeAsync(() => AppendLog(line)));

                    try
                    {
                        var batchOk = await _downloadService.StartProcessAsync(
                            result.FilePath, string.Empty, batchLog, batchProgress);

                        if (batchOk)
                        {
                            ProgressBar_Download.Value = 100;
                            SetStatus($"Batch finished. Saved to {_lastOutputDirectory}");
                            Button_OpenFolder.Visibility = Visibility.Visible;
                        }
                        else
                        {
                            SetStatus("Batch failed — open the Log for details.", isError: true);
                            ToggleButton_Log.IsChecked = true;
                        }
                    }
                    catch (Exception ex)
                    {
                        SetStatus($"Batch error: {ex.Message}", isError: true);
                        ToggleButton_Log.IsChecked = true;
                    }
                    finally
                    {
                        Button_GO.IsEnabled = true;
                        Button_Stop.Visibility = Visibility.Collapsed;
                        try
                        {
                            if (File.Exists(result.FilePath))
                                File.Delete(result.FilePath);
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"Failed to delete temp batch file '{result.FilePath}': {ex.Message}");
                        }
                    }
                }
            }
            else
            {
                Button_GO.IsEnabled = false;
                Button_Stop.Visibility = Visibility.Visible;
                try
                {
                    if (CheckBox_BypassCF?.IsChecked == true)
                    {
                        await StartCloudflareDownloadAsync();
                    }
                    else
                    {
                        var argsForPreview = BuildArgsRE();
                        TextBox_Parameter.Text = argsForPreview;

                        var options = BuildDownloadOptions();
                        _lastOutputDirectory = string.IsNullOrWhiteSpace(options.SaveDir)
                            ? Environment.CurrentDirectory
                            : options.SaveDir;

                        ResetRunState();
                        SetStatus("Downloading…");

                        var progress = new Progress<int>(p => ProgressBar_Download.Value = p);
                        var log = new Action<string>(line => Dispatcher.InvokeAsync(() => AppendLog(line)));

                        var succeeded = await _downloadService.StartDownloadAsync(options, progress, log);

                        if (succeeded)
                        {
                            ProgressBar_Download.Value = 100;
                            SetStatus($"Saved to {_lastOutputDirectory}");
                            Button_OpenFolder.Visibility = Visibility.Visible;
                        }
                        else
                        {
                            SetStatus("Download failed — open the Log for details.", isError: true);
                            ToggleButton_Log.IsChecked = true;
                        }
                    }
                }
                catch (Exception ex)
                {
                    SetStatus($"Download error: {ex.Message}", isError: true);
                    ToggleButton_Log.IsChecked = true;
                }
                finally
                {
                    Button_GO.IsEnabled = true;
                    Button_Stop.Visibility = Visibility.Collapsed;
                }
            }
        }

        private void Button_Stop_Click(object sender, RoutedEventArgs e)
        {
            var cts = _activeOperationCts;
            if (cts != null)
            {
                try { cts.Cancel(); } catch (ObjectDisposedException) { }
            }

            _downloadService.StopDownload();
            Button_Stop.Visibility = Visibility.Collapsed;
        }

        private void Button_CopyCommand_Click(object sender, RoutedEventArgs e)
        {
            if (!string.IsNullOrWhiteSpace(TextBox_Parameter?.Text))
            {
                try
                {
                    Clipboard.SetText(TextBox_Parameter.Text);
                    FlashTextBox(TextBox_Parameter);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Failed to copy command: {ex.Message}");
                }
            }
        }

        private void TextBox_URL_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
                Button_GO.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
        }

        private void TextBox_Title_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
                Button_GO.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
        }

        private void SetTopMost(object sender, RoutedEventArgs e) =>
            Topmost = CheckBox_TopMost.IsChecked == true;

        private void Menu_GetDownloader(object sender, RoutedEventArgs e)
        {
            StartShellTarget("https://github.com/nilaoda/N_m3u8DL-RE/releases");
        }

        private static void StartShellTarget(string targetPath)
        {
            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = targetPath,
                    UseShellExecute = true
                };
                Process.Start(startInfo);
            }
            catch (System.ComponentModel.Win32Exception ex)
            {
                Debug.WriteLine($"Failed to open target '{targetPath}': {ex.Message}");
            }
        }

        /// <summary>
        /// Build the CF options object for m3u8_cf_bypass.py.
        /// Uses dedicated CF bypass controls (TextBox_CFReferer, TextBox_CFCookie,
        /// Combo_CFImpersonate, CheckBox_CFKeepSegs) for a clean, predictable data path.
        /// </summary>
        private CfCommandOptions BuildCfOptions(string pythonExe = "python")
        {
            string scriptPath = Path.Combine(AppContext.BaseDirectory, "m3u8_cf_bypass.py");
            if (!File.Exists(scriptPath))
                scriptPath = Path.Combine(Environment.CurrentDirectory, "m3u8_cf_bypass.py");

            var titleClean = _utilityService.GetValidFileName(TextBox_Title.Text);
            if (string.IsNullOrWhiteSpace(titleClean)) titleClean = "output";
            if (!titleClean.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase)) titleClean += ".mp4";

            return new CfCommandOptions(
                PythonExe: pythonExe,
                ScriptPath: scriptPath,
                Url: TextBox_URL.Text,
                OutputName: titleClean,
                WorkDir: string.IsNullOrWhiteSpace(TextBox_WorkDir.Text)
                    ? Environment.CurrentDirectory
                    : TextBox_WorkDir.Text,
                SegDir: Path.Combine(AppContext.BaseDirectory, "cf_segments"),
                Referer: CfCommandBuilder.DeriveReferer(TextBox_CFReferer?.Text, TextBox_URL.Text),
                Cookie: TextBox_CFCookie?.Text?.Trim() ?? string.Empty,
                Impersonate: (Combo_CFImpersonate?.SelectedItem is ComboBoxItem cfi && cfi.Tag is string tag && !string.IsNullOrEmpty(tag))
                    ? tag
                    : "chrome",
                KeepSegments: CheckBox_CFKeepSegs?.IsChecked == true);
        }

        /// <summary>
        /// Find a Python interpreter that can import curl_cffi, by probing a list of
        /// candidate interpreters and running `python -c "import curl_cffi"` for each.
        /// Returns the first interpreter whose exit code is 0, or null if none found.
        ///
        /// Candidate order:
        ///   1. Explicit full paths to common CPython installs (avoids Windows Store stub)
        ///   2. `py` launcher (reliable on Windows)
        ///   3. Bare `python` / `python3` (PATH-resolved; last resort)
        /// </summary>
        private static async System.Threading.Tasks.Task<string?> FindPythonWithCurlCffiAsync(System.Threading.CancellationToken cancellationToken = default)
        {
            var candidates = new List<string>();

            // 1. Explicit full paths to standard CPython installs
            try
            {
                string localApp = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                string progFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
                string progFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
                foreach (var baseDir in new[] { progFiles, progFilesX86 })
                {
                    if (string.IsNullOrEmpty(baseDir)) continue;
                    var pyRoot = Path.Combine(baseDir, "Python");
                    if (Directory.Exists(pyRoot))
                        foreach (var d in Directory.GetDirectories(pyRoot))
                            candidates.Add(Path.Combine(d, "python.exe"));
                }
                if (!string.IsNullOrEmpty(localApp))
                {
                    var pp = Path.Combine(localApp, "Programs", "Python");
                    if (Directory.Exists(pp))
                        foreach (var d in Directory.GetDirectories(pp))
                            candidates.Add(Path.Combine(d, "python.exe"));
                }
            }
            catch { }

            // 2. WorkBuddy & Anaconda/Miniconda managed environments
            try
            {
                string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                if (!string.IsNullOrEmpty(userProfile))
                {
                    string wbPy = Path.Combine(userProfile, ".workbuddy", "binaries", "python", "versions");
                    if (Directory.Exists(wbPy))
                        foreach (var v in Directory.GetDirectories(wbPy))
                            candidates.Add(Path.Combine(v, "python.exe"));

                    foreach (var condaName in new[] { "anaconda3", "miniconda3", "Anaconda3", "Miniconda3" })
                    {
                        var condaPath = Path.Combine(userProfile, condaName, "python.exe");
                        if (File.Exists(condaPath))
                            candidates.Add(condaPath);
                    }
                }
            }
            catch { }

            // 3. Named launchers resolved via PATH.
            candidates.Add("py");
            candidates.Add("python");
            candidates.Add("python3");

            foreach (var c in candidates)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    // Skip full paths that don't exist on disk.
                    if (c.IndexOf(Path.DirectorySeparatorChar) >= 0 && !File.Exists(c))
                        continue;

                    var psi = new ProcessStartInfo(c, "-c \"import curl_cffi\"")
                    {
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true,
                    };

                    using (var p = Process.Start(psi))
                    {
                        if (p == null) continue;
                        using var timeoutCts = System.Threading.CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                        timeoutCts.CancelAfter(TimeSpan.FromSeconds(10));

                        var outTask = p.StandardOutput.ReadToEndAsync(timeoutCts.Token);
                        var errTask = p.StandardError.ReadToEndAsync(timeoutCts.Token);

                        try
                        {
                            await p.WaitForExitAsync(timeoutCts.Token);
                            await System.Threading.Tasks.Task.WhenAll(outTask, errTask);
                        }
                        catch (OperationCanceledException)
                        {
                            try { p.Kill(entireProcessTree: true); } catch { }
                            if (cancellationToken.IsCancellationRequested)
                                throw;
                            continue;
                        }

                        if (p.ExitCode == 0)
                            return c;
                    }
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch { }
            }
            return null;
        }

        /// <summary>
        /// Clean up stale Cloudflare batch files from %TEMP% directory created in previous runs.
        /// </summary>
        private static void CleanStaleTempBatchFiles()
        {
            try
            {
                var tempDir = Path.GetTempPath();
                var dirInfo = new DirectoryInfo(tempDir);
                var staleFiles = dirInfo.GetFiles("cf_dl_*.bat")
                    .Where(f => (DateTime.Now - f.LastWriteTime).TotalHours > 1);

                foreach (var file in staleFiles)
                {
                    try { file.Delete(); } catch { }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to clean stale temp files: {ex.Message}");
            }
        }

        /// <summary>
        /// Launch m3u8_cf_bypass.py via a temp .bat so the console window stays open
        /// and the user can see download progress / errors.
        /// Tracked by IDownloadService so Button_Stop can kill the process tree if cancelled.
        /// </summary>
        private async System.Threading.Tasks.Task StartCloudflareDownloadAsync()
        {
            string scriptPath = Path.Combine(AppContext.BaseDirectory, "m3u8_cf_bypass.py");
            if (!File.Exists(scriptPath))
                scriptPath = Path.Combine(Environment.CurrentDirectory, "m3u8_cf_bypass.py");
            if (!File.Exists(scriptPath))
            {
                MessageBox.Show(
                    "m3u8_cf_bypass.py not found.\nPlease place it in the same directory as the GUI executable.",
                    "Bypass Cloudflare", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var cts = BeginCancellableOperation();
            string? pythonExe = null;
            try
            {
                pythonExe = await FindPythonWithCurlCffiAsync(cts.Token);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            finally
            {
                EndCancellableOperation(cts);
            }

            if (string.IsNullOrEmpty(pythonExe))
            {
                MessageBox.Show(
                    "No Python interpreter with curl_cffi found.\n\n" +
                    "Install the dependency once (run in your terminal):\n" +
                    "    pip install curl_cffi\n" +
                    "or: python -m pip install curl_cffi\n\n" +
                    "Then click Start Download again.",
                    "Bypass Cloudflare", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            string cfCmd = CfCommandBuilder.BuildCommand(BuildCfOptions(pythonExe));
            TextBox_Parameter.Text = cfCmd;

            string bat = Path.Combine(Path.GetTempPath(), "cf_dl_" + DateTime.Now.ToString("yyyyMMddHHmmss") + ".bat");
            File.WriteAllText(bat, CfCommandBuilder.BuildBatchScript(cfCmd), new UTF8Encoding(false));

            _lastOutputDirectory = string.IsNullOrWhiteSpace(TextBox_WorkDir.Text)
                ? Environment.CurrentDirectory
                : TextBox_WorkDir.Text;
            ResetRunState();
            SetStatus("Running Cloudflare bypass…");

            var cfProgress = new Progress<int>(p => ProgressBar_Download.Value = p);
            var cfLog = new Action<string>(line => Dispatcher.InvokeAsync(() => AppendLog(line)));

            var cfOk = await _downloadService.StartProcessAsync(bat, string.Empty, cfLog, cfProgress);

            if (cfOk)
            {
                ProgressBar_Download.Value = 100;
                SetStatus($"Saved to {_lastOutputDirectory}");
                Button_OpenFolder.Visibility = Visibility.Visible;
            }
            else
            {
                SetStatus("Cloudflare bypass failed — open the Log for details.", isError: true);
                ToggleButton_Log.IsChecked = true;
            }
        }

        private static string SafeGetClipboardText()
        {
            try
            {
                return Clipboard.ContainsText() ? Clipboard.GetText() : string.Empty;
            }
            catch (ExternalException ex)
            {
                Debug.WriteLine($"Clipboard access failed (external lock): {ex.Message}");
                return string.Empty;
            }
            catch (InvalidOperationException ex)
            {
                Debug.WriteLine($"Clipboard access failed (thread state): {ex.Message}");
                return string.Empty;
            }
        }

        private static void StartExecutableWithArguments(string executablePath, string arguments)
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = executablePath,
                Arguments = arguments,
                UseShellExecute = false
            };
            var process = Process.Start(startInfo);
            if (process == null)
                Debug.WriteLine($"Failed to start process: {executablePath}");
        }

        /// <summary>
        /// Detects the text encoding of a file by reading its byte-order mark.
        /// </summary>
        /// <param name="filePath">Absolute path to the file.</param>
        /// <returns>The detected <see cref="Encoding"/>.</returns>
        public static Encoding DetectFileEncoding(string filePath) =>
            TextEncodingDetector.DetectFromFile(filePath);

        /// <summary>
        /// Detects the text encoding from a file stream by reading its byte-order mark.
        /// </summary>
        /// <param name="stream">An open <see cref="FileStream"/>.</param>
        /// <returns>The detected <see cref="Encoding"/>.</returns>
        public static Encoding DetectFileEncoding(FileStream stream) =>
            TextEncodingDetector.DetectFromStream(stream);

        private void TextBox_Key_PreviewDragEnter(object sender, System.Windows.DragEventArgs e)
        {
            if (HasFileDropData(e))
                MarkDragCopy(e);
        }

        private void TextBox_Key_PreviewDragOver(object sender, System.Windows.DragEventArgs e)
        {
            if (HasFileDropData(e))
                MarkDragCopy(e);
        }

        private void TextBox_Key_PreviewDrop(object sender, System.Windows.DragEventArgs e)
        {
            if (TryGetFirstDroppedPath(e, out var path))
            {
                MarkDragCopy(e);
                if (DropInputRules.IsValidKeyFilePath(path))
                    TextBox_Key.Text = path;
                else
                    MessageBox.Show(
                        "That file is not a valid key file. A raw HLS key must be exactly 16 bytes.",
                        "Invalid Key File", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private async void Button_CheckUpdate_Click(object sender, RoutedEventArgs e)
        {
            await CheckGuiUpdateAsync(isManual: true);
        }

        private void Button_UpdateBadge_Click(object sender, RoutedEventArgs e)
        {
            string? url = Button_UpdateBadge.Tag as string;
            if (string.IsNullOrEmpty(url))
                url = "https://github.com/naravid19/N_m3u8DL_RE_GUI/releases/latest";
            StartShellTarget(url);
        }

        private async System.Threading.Tasks.Task CheckGuiUpdateAsync(bool isManual)
        {
            if (_isCheckingUpdate) return;
            _isCheckingUpdate = true;

            try
            {
                if (Button_CheckUpdate != null) Button_CheckUpdate.IsEnabled = false;
                if (TextBlock_UpdateStatus != null) TextBlock_UpdateStatus.Text = "Checking...";

                var service = new N_m3u8DL_RE_GUI.Core.Services.GitHubUpdateCheckService();
                var currentVer = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version ?? new Version(2, 1, 4);
                var result = await service.CheckForUpdateAsync("naravid19", "N_m3u8DL_RE_GUI", currentVer);

                if (result.HasUpdate)
                {
                    Button_UpdateBadge.Content = $"🎉 {result.LatestVersion} Available!";
                    Button_UpdateBadge.Tag = result.ReleaseUrl;
                    Button_UpdateBadge.Visibility = Visibility.Visible;
                    if (TextBlock_UpdateStatus != null)
                        TextBlock_UpdateStatus.Text = $"{result.LatestVersion} available!";
                }
                else
                {
                    if (TextBlock_UpdateStatus != null)
                    {
                        if (isManual)
                        {
                            TextBlock_UpdateStatus.Text = "✓ Latest version";
                            var timer = new System.Windows.Threading.DispatcherTimer
                            {
                                Interval = TimeSpan.FromSeconds(3)
                            };
                            timer.Tick += (s, e) =>
                            {
                                TextBlock_UpdateStatus.Text = "";
                                ((System.Windows.Threading.DispatcherTimer)s!).Stop();
                            };
                            timer.Start();
                        }
                        else
                        {
                            TextBlock_UpdateStatus.Text = "";
                        }
                    }
                }
            }
            finally
            {
                _isCheckingUpdate = false;
                if (Button_CheckUpdate != null) Button_CheckUpdate.IsEnabled = true;
            }
        }
    }
}



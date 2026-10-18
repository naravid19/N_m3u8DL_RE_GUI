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
using System.Windows.Threading;
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
using N_m3u8DL_RE_GUI.Core.Abyss;
using N_m3u8DL_RE_GUI.Core.Capture;
using N_m3u8DL_RE_GUI.Core.Resume;
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
        private ResumeJob? _activeResumeJob;
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

            UpdateAppTitle();
            Services.LanguageService.Default.LanguageChanged += OnLanguageChanged;
            SyncGuiLanguageCombo();

            CommandBindings.Add(new CommandBinding(
                StartDownloadRoutedCommand,
                (_, _) => Button_GO_Click(Button_GO, new RoutedEventArgs()),
                (_, e) => e.CanExecute = IsEnabled && Button_GO.IsEnabled));

            CommandBindings.Add(new CommandBinding(
                StopDownloadRoutedCommand,
                (_, _) => Button_Stop_Click(Button_Stop, new RoutedEventArgs()),
                (_, e) => e.CanExecute = Button_Stop.Visibility == Visibility.Visible));

            Loaded += (_, _) => StartWatchingCaptureInbox();

            TextBox_URL.Focus();
            System.Windows.DataObject.AddPastingHandler(TextBox_URL, TextBox_URL_Pasting);
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
            var saveDir = OptionValueNormalizer.NormalizeSaveDir(TextBox_WorkDir.Text);
            var saveName = _utilityService.GetValidFileName(TextBox_Title.Text);
            var tmpDir = ResumePaths.ResolveTmpDir(
                TextBox_TmpDir?.Text,
                _activeResumeJob?.TmpDir,
                _activeResumeJob?.SaveName,
                saveDir,
                saveName);

            var options = new DownloadOptions
            {
                // Basic Settings
                Input = string.IsNullOrWhiteSpace(inputOverride) ? TextBox_URL.Text : inputOverride,
                SaveDir = saveDir,
                TmpDir = tmpDir,
                SaveName = saveName,
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
                // it unreadable.
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
            var saveDir = OptionValueNormalizer.NormalizeSaveDir(TextBox_WorkDir.Text);
            var saveName = _utilityService.GetValidFileName(TextBox_Title.Text);
            var tmpDir = ResumePaths.ResolveTmpDir(
                TextBox_TmpDir?.Text,
                _activeResumeJob?.TmpDir,
                _activeResumeJob?.SaveName,
                saveDir,
                saveName);

            return new DownloadOptions
            {
                // EXE path — lets DownloadService use the GUI-configured binary
                ExePath = string.IsNullOrWhiteSpace(TextBox_EXE?.Text) ? null : TextBox_EXE.Text.Trim(),

                // Basic Settings
                Input = TextBox_URL.Text,
                SaveDir = saveDir,
                TmpDir = tmpDir,
                SaveName = saveName,
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
                // it unreadable.
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
        private void Combo_CFEngine_SelectionChanged(object sender, SelectionChangedEventArgs e) => GetParameter();
        private void Combo_GuiLanguage_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_suspendParameterRefresh) return;
            var lang = (Combo_GuiLanguage.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? Combo_GuiLanguage.SelectedValue?.ToString();
            if (!string.IsNullOrEmpty(lang))
            {
                Services.LanguageService.Default.SetLanguage(lang);
            }
        }

        private void UpdateAppTitle()
        {
            var appVersion = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
            var appTitle = Services.LanguageService.Default.GetString("Str_AppTitle", "N_m3u8DL-RE GUI");
            if (appVersion != null)
            {
                var verString = $"v{appVersion.Major}.{appVersion.Minor}.{Math.Max(0, appVersion.Build)}";
                Title = $"{appTitle} {verString}";
                if (TextBlock_AppVersion != null)
                {
                    TextBlock_AppVersion.Text = verString;
                }
            }
            else
            {
                Title = appTitle;
            }
        }

        private void SyncGuiLanguageCombo()
        {
            if (Combo_GuiLanguage == null) return;
            var cur = Services.LanguageService.Default.CurrentLanguage;
            foreach (ComboBoxItem item in Combo_GuiLanguage.Items)
            {
                if (item.Tag?.ToString() == cur)
                {
                    if (Combo_GuiLanguage.SelectedItem != item)
                        Combo_GuiLanguage.SelectedItem = item;
                    break;
                }
            }
        }

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

        /// <summary>Bumped on every applied capture so an in-flight title fetch can tell it is stale.</summary>
        private int _captureGeneration;

        private async Task PopulateTitleForInputAsync(string input, bool clearWhenUnknown)
        {
            if (string.IsNullOrWhiteSpace(input))
                return;

            if (InputValidation.IsHttpUrl(input))
            {
                var cts = BeginCancellableOperation();
                var generation = _captureGeneration;
                try
                {
                    var title = await _utilityService.GetTitleFromUrlAsync(input, cts.Token);
                    // A capture applied while this was in flight owns the title now.
                    if (generation == _captureGeneration)
                        TextBox_Title.Text = title;
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
            if (!TryGetFirstDroppedPath(e, out var path))
                return;

            // Must come first: a .har is a source to extract from, never a stream input.
            if (DropInputRules.IsHarPath(path))
            {
                ImportFromHar(path);
                e.Handled = true;
                return;
            }

            if (DropInputRules.IsSupportedUrlInputPath(path))
            {
                MarkDragCopy(e);
                if (TextBox_URL.Text != path) FlashTextBox(TextBox_URL);
                TextBox_URL.Text = path;
                if (DropInputRules.ShouldAutoFillTitleFromFileName(path))
                    TextBox_Title.Text = Path.GetFileNameWithoutExtension(path);
            }
        }

        private void ImportFromHar(string path)
        {
            IReadOnlyList<CapturedRequest> candidates;
            try
            {
                candidates = HarStreamExtractor.ExtractFromFile(path);
            }
            catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException)
            {
                SetStatus(ex.Message, isError: true);
                return;
            }

            if (candidates.Count == 0)
            {
                SetStatus(Services.LanguageService.Default.GetString("Str_StatusHarNoStream",
                    "No stream was found in that capture. Clear the network log, press play, let it run a few seconds, then save the HAR again."), isError: true);
                return;
            }

            if (candidates.Count == 1)
            {
                TryApplyCapturedRequest(candidates[0]);
                return;
            }

            var picker = new Views.StreamPickerWindow(candidates) { Owner = this };
            if (picker.ShowDialog() == true)
                TryApplyCapturedRequest(picker.Selected);
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
            Services.LanguageService.Default.LanguageChanged -= OnLanguageChanged;

            // A run outlives this window unless it is stopped here. The downloader,
            // the CF proxy and ffmpeg are separate processes with no Job Object tying
            // them to us, and DownloadService is not IDisposable, so disposing the DI
            // container in App.OnExit never reaches them either -- they would keep
            // going, still writing files, with no UI left to stop them.
            try
            {
                _downloadService.StopDownload();
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"Failed to stop the active download while closing: {ex.Message}");
            }

            // The CF proxy is killed through this token, not by StopDownload.
            try
            {
                _activeOperationCts?.Cancel();
            }
            catch (ObjectDisposedException) { }

            var state = Services.MainWindowConfigMapper.Capture(this);
            if (!_configService.Save("config.txt", state))
            {
                // Save reports failure precisely so it is not silent. The window is
                // about to disappear, so a dialog is the only place left to say it.
                MessageBox.Show(
                    Services.LanguageService.Default.GetString(
                        "Str_SaveOnExitFailedMsg",
                        "Your settings could not be saved. The previous saved settings are unchanged, but any changes made in this session will be lost."),
                    Services.LanguageService.Default.GetString("Str_SaveOnExitFailedTitle", "Could Not Save Settings"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }

        private void OnLanguageChanged(object? sender, EventArgs e)
        {
            UpdateAppTitle();
            SyncGuiLanguageCombo();
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

                CheckForResumableJob();
            }
        }

        private void CheckForResumableJob()
        {
            try
            {
                var job = Services.ResumeJobStore.Default.TryFindResumable();
                if (job != null && job.ExistingBytes > 0)
                {
                    _activeResumeJob = job;
                    var title = string.IsNullOrWhiteSpace(job.SaveName)
                        ? Localize("Str_ResumeTitleUnnamed", "Unfinished download")
                        : Localize("Str_ResumeTitleNamed", "Unfinished download — \"{0}\"", job.SaveName);
                    TextBlock_ResumeTitle.Text = $"⏸  {title}";

                    var size = FormatByteSize(job.ExistingBytes);
                    var timeAgo = FormatTimeAgo(job.StartedAt);
                    var host = string.IsNullOrWhiteSpace(job.SourceHost) ? string.Empty : Localize("Str_ResumeFromHost", " · from {0}", job.SourceHost);
                    TextBlock_ResumeDetail.Text = Localize("Str_ResumeDetailLine", "{0} already saved · stopped {1}{2}", size, timeAgo, host);

                    Border_ResumeBanner.Visibility = Visibility.Visible;
                }
                else
                {
                    _activeResumeJob = null;
                    Border_ResumeBanner.Visibility = Visibility.Collapsed;
                }
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"[MainWindow] CheckForResumableJob error: {ex.Message}");
                _activeResumeJob = null;
                Border_ResumeBanner.Visibility = Visibility.Collapsed;
            }
        }

        private static string FormatByteSize(long bytes)
        {
            if (bytes < 1024) return $"{bytes} B";
            if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
            if (bytes < 1024L * 1024L * 1024L) return $"{bytes / (1024.0 * 1024.0):F0} MB";
            return $"{bytes / (1024.0 * 1024.0 * 1024.0):F1} GB";
        }

        private static string FormatTimeAgo(DateTimeOffset startedAt)
        {
            var elapsed = DateTimeOffset.UtcNow - startedAt;
            if (elapsed.TotalMinutes < 1) return Localize("Str_TimeJustNow", "just now");
            if (elapsed.TotalMinutes < 60)
            {
                var m = Math.Max(1, (int)elapsed.TotalMinutes);
                return m == 1 ? Localize("Str_TimeMinuteAgo", "{0} minute ago", m) : Localize("Str_TimeMinutesAgo", "{0} minutes ago", m);
            }
            if (elapsed.TotalHours < 24)
            {
                var h = (int)elapsed.TotalHours;
                return h == 1 ? Localize("Str_TimeHourAgo", "{0} hour ago", h) : Localize("Str_TimeHoursAgo", "{0} hours ago", h);
            }
            var d = (int)elapsed.TotalDays;
            return d == 1 ? Localize("Str_TimeDayAgo", "{0} day ago", d) : Localize("Str_TimeDaysAgo", "{0} days ago", d);
        }

        private void Button_ResumeJob_Click(object sender, RoutedEventArgs e)
        {
            if (_activeResumeJob != null)
            {
                if (!string.IsNullOrWhiteSpace(_activeResumeJob.SaveName))
                    TextBox_Title.Text = _activeResumeJob.SaveName;
                if (!string.IsNullOrWhiteSpace(_activeResumeJob.SaveDir))
                    TextBox_WorkDir.Text = _activeResumeJob.SaveDir;

                TextBox_URL.Text = string.Empty;
                TextBox_URL.Focus();

                Border_ResumeBanner.Visibility = Visibility.Collapsed;
                SetStatus(Services.LanguageService.Default.GetString("Str_StatusResumeFreshLink", "Paste a fresh link for this video. The original link has expired — that is normal, and everything already downloaded will be kept."));
            }
        }

        private void Button_DiscardJob_Click(object sender, RoutedEventArgs e)
        {
            if (_activeResumeJob != null)
            {
                var size = FormatByteSize(_activeResumeJob.ExistingBytes);
                var targetName = string.IsNullOrWhiteSpace(_activeResumeJob.SaveName) ? "this download" : $"\"{_activeResumeJob.SaveName}\"";
                var msgTemplate = Services.LanguageService.Default.GetString("Str_DiscardDialogMsg", "Delete {0} of partial download for {1}? This cannot be undone.");
                var message = string.Format(msgTemplate, size, targetName);
                var title = Services.LanguageService.Default.GetString("Str_DiscardDialogTitle", "Discard Interrupted Download");

                var result = MessageBox.Show(message, title, MessageBoxButton.YesNo, MessageBoxImage.Warning);
                if (result == MessageBoxResult.Yes)
                {
                    var success = Services.ResumeJobStore.Default.Discard();
                    if (success)
                    {
                        Border_ResumeBanner.Visibility = Visibility.Collapsed;
                        _activeResumeJob = null;
                        SetStatus(Services.LanguageService.Default.GetString("Str_StatusResumeDiscarded", "Partial download discarded."));
                    }
                    else
                    {
                        MessageBox.Show(
                            Services.LanguageService.Default.GetString("Str_DiscardFailedMsg", "Could not delete partial files. A file may still be in use by another process."),
                            Services.LanguageService.Default.GetString("Str_DiscardFailedTitle", "Discard Failed"),
                            MessageBoxButton.OK, MessageBoxImage.Error);
                    }
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

        private string? _lastOutputDirectory;

        private void Button_PasteCurl_Click(object sender, RoutedEventArgs e)
        {
            string clipboardText;
            try
            {
                clipboardText = Clipboard.GetText();
            }
            catch (Exception ex)
            {
                // The clipboard is a shared OS resource; another process can hold it locked.
                SetStatus(Localize("Str_StatusClipboardReadFailed", "Could not read the clipboard: {0}", ex.Message), isError: true);
                return;
            }

            ApplyCapturePayload(clipboardText);
        }

        /// <summary>
        /// Applies one capture payload — the text "Copy as cURL" produces —
        /// whether it arrived through the clipboard or from the browser
        /// extension via the native host. Returns true when something was
        /// applied.
        /// </summary>
        private bool ApplyCapturePayload(string text)
        {
            if (TryApplyCapturedRequest(CurlCommandParser.Parse(text)))
                return true;

            if (BatchPasteHelper.LooksLikeBatchList(text))
            {
                var directives = CaptureDirectives.Parse(text);
                if (directives.TryGetValue("headers", out var headers) && !string.IsNullOrWhiteSpace(headers))
                {
                    TextBox_Headers.Text = headers;
                }
                if (directives.TryGetValue("save-name", out var saveName) && !string.IsNullOrWhiteSpace(saveName))
                {
                    TextBox_Title.Text = saveName;
                }

                var batchFile = BatchPasteHelper.WriteTempBatchFile(text);
                TextBox_URL.Text = batchFile;

                if (directives.TryGetValue("warn", out var warn) && !string.IsNullOrWhiteSpace(warn))
                {
                    SetStatus(warn, isWarning: true);
                }
                else
                {
                    SetStatus(Localize("Str_StatusBatchImported", "Imported batch list — click GO to download."));
                }
                return true;
            }

            SetStatus(Localize("Str_StatusClipboardNoCapture", "Clipboard does not contain a cURL command or multi-stream batch list. In your browser extension: click 'Copy as cURL' or 'Copy as list'."), isError: true);
            return false;
        }

        private void TextBox_URL_Pasting(object sender, DataObjectPastingEventArgs e)
        {
            if (!e.DataObject.GetDataPresent(DataFormats.UnicodeText))
                return;

            var pasted = e.DataObject.GetData(DataFormats.UnicodeText) as string;
            if (string.IsNullOrWhiteSpace(pasted))
                return;

            if (CurlCommandParser.LooksLikeCurl(pasted))
            {
                if (TryApplyCapturedRequest(CurlCommandParser.Parse(pasted)))
                    e.CancelCommand();
                return;
            }

            if (BatchPasteHelper.LooksLikeBatchList(pasted))
            {
                var directives = CaptureDirectives.Parse(pasted);
                if (directives.TryGetValue("headers", out var headers) && !string.IsNullOrWhiteSpace(headers))
                {
                    TextBox_Headers.Text = headers;
                }
                if (directives.TryGetValue("save-name", out var saveName) && !string.IsNullOrWhiteSpace(saveName))
                {
                    TextBox_Title.Text = saveName;
                }

                var batchFile = BatchPasteHelper.WriteTempBatchFile(pasted);
                TextBox_URL.Text = batchFile;

                if (directives.TryGetValue("warn", out var warn) && !string.IsNullOrWhiteSpace(warn))
                {
                    SetStatus(warn, isWarning: true);
                }
                else
                {
                    SetStatus(Localize("Str_StatusBatchImported", "Imported batch list — click GO to download."));
                }
                e.CancelCommand();
            }
        }

        /// <summary>
        /// Fills the URL and header fields from a capture. Returns false when there was
        /// nothing usable, so callers can report it their own way.
        /// </summary>
        private bool TryApplyCapturedRequest(CapturedRequest? captured)
        {
            var plan = CaptureApplyPlan.From(captured);
            if (plan is null)
                return false;

            _captureGeneration++;

            // Full state replacement (D1, fixes H2)
            TextBox_URL.Text = plan.Url;
            TextBox_Headers.Text = plan.HeaderLines;
            TextBox_SelectVideo.Text = plan.SelectVideo;
            TextBox_SelectAudio.Text = plan.SelectAudio;
            TextBox_SelectSubtitle.Text = plan.SelectSubtitle;
            TextBox_Title.Text = plan.SaveName;

            // CF controls (fixes C1)
            TextBox_CFReferer.Text = plan.CfReferer;
            TextBox_CFCookie.Text = plan.CfCookie;

            // The Key, the CF bypass and the TLS profile belong to this Stream only. The
            // download starts right after a capture, so nothing may carry over from the
            // previous one. The user's proxy is a preference and is left alone.
            TextBox_Key.Text = string.Empty;
            Combo_CFImpersonate.SelectedIndex = 0;
            if (plan.CfImpersonate != null)
            {
                foreach (var item in Combo_CFImpersonate.Items)
                {
                    if (item is ComboBoxItem cbi &&
                        string.Equals(cbi.Tag as string, plan.CfImpersonate, StringComparison.OrdinalIgnoreCase))
                    {
                        Combo_CFImpersonate.SelectedItem = cbi;
                        break;
                    }
                }
            }

            CheckBox_BypassCF.IsChecked = plan.EnableCfBypass;

            var kind = plan.Kind == CapturedStreamKind.Unknown
                ? "stream"
                : plan.Kind.ToString().ToUpperInvariant();

            var qualityMsg = !string.IsNullOrEmpty(plan.SelectVideo) ? Localize("Str_StatusImportedQuality", " (Quality: {0})", plan.SelectVideo) : "";

            if (plan.Warning != null)
            {
                SetStatus(plan.Warning, isWarning: true);
            }
            else if (plan.EnableCfBypass)
            {
                SetStatus(Localize("Str_StatusCfDetected", "Cloudflare detected — bypass mode enabled."), isWarning: true);
            }
            else
            {
                SetStatus(Localize("Str_StatusImported", "Imported {0} — 1 URL, {1} header(s){2}.", kind, plan.HeaderCount, qualityMsg));
            }
            return true;
        }

        /// <summary>
        /// Looks up a translated UI string and applies string.Format when arguments are given.
        /// The key must be a literal so LanguageUsageParityTests can see it.
        /// </summary>
        private static string Localize(string key, string fallback, params object[] args)
        {
            var template = Services.LanguageService.Default.GetString(key, fallback);
            return args.Length == 0 ? template : string.Format(template, args);
        }

        private void SetStatus(string text, bool isError = false, bool isWarning = false)
        {
            TextBlock_Status.Text = text;
            TextBlock_Status.Foreground = isError   ? ErrorBorderBrush
                                        : isWarning ? WarnBrush
                                        :             DefaultStatusBrush;
        }

        private static readonly Media.SolidColorBrush DefaultStatusBrush =
            CreateFrozenBrush(MediaColor.FromRgb(0x88, 0x88, 0xA8));

        private static readonly Media.SolidColorBrush WarnBrush =
            CreateFrozenBrush(MediaColor.FromRgb(0xF0, 0xA0, 0x30));

        private void AppendLog(string message)
        {
            // Appended, not rebuilt: re-assigning the whole text per line made a long
            // run's log O(n^2) on the UI thread, even with the log panel collapsed.
            TextBox_Log.AppendText(message + Environment.NewLine);
            TextBox_Log.ScrollToEnd();
        }

        private void ResetRunState()
        {
            TextBox_Log.Text = string.Empty;
            ProgressBar_Download.Value = 0;
            Button_OpenFolder.Visibility = Visibility.Collapsed;
        }

        private readonly Core.NativeBridge.CaptureInbox _captureInbox = new();

        /// <summary>Set while a capture's start is queued but has not run yet.</summary>
        private bool _captureStartPending;

        /// <summary>
        /// Set by the capture drain just before it clicks GO for a browser capture. Nobody is
        /// at the keyboard for that run, so it must not open a dialog. Consumed at the top of
        /// Button_GO_Click.
        /// </summary>
        private bool _startingFromCapture;
        private FileSystemWatcher? _inboxWatcher;

        /// <summary>
        /// The single place a download path returns control to the user. It is
        /// also where the capture queue advances, so a payload that arrived
        /// mid-download starts as soon as the current one finishes.
        /// </summary>
        private void SetDownloadIdle()
        {
            Button_GO.IsEnabled = true;
            Button_Stop.Visibility = Visibility.Collapsed;
            DrainCaptureInbox();
        }

        /// <summary>
        /// Applies one queued capture and starts it. Refuses while a download
        /// is running and deliberately leaves the entry in the inbox — that
        /// refusal is what makes the inbox behave as a queue.
        /// </summary>
        private void DrainCaptureInbox()
        {
            // Button_GO stays enabled until the queued start below runs, so on its own
            // it would let a second drain in that gap overwrite this capture's fields.
            if (!Button_GO.IsEnabled || _captureStartPending)
                return;

            while (Button_GO.IsEnabled)
            {
                var payload = _captureInbox.TakeOldest();
                if (payload is null)
                    return;

                if (!ApplyCapturePayload(payload))
                {
                    SetStatus(Localize("Str_StatusCaptureUnreadable", "Received a capture from the browser that could not be read."), isWarning: true);
                    continue;
                }

                SetStatus(Localize("Str_StatusCaptureStarting", "Received from browser — starting download…"));

                // Queued rather than called: a drain triggered from a finally block
                // would otherwise re-enter the download handler it is unwinding.
                _captureStartPending = true;
                Dispatcher.BeginInvoke(
                    new Action(() =>
                    {
                        _captureStartPending = false;
                        _startingFromCapture = true;
                        Button_GO_Click(Button_GO, new RoutedEventArgs());
                    }),
                    DispatcherPriority.Background);
                break;
            }
        }

        private void StartWatchingCaptureInbox()
        {
            try
            {
                Directory.CreateDirectory(_captureInbox.Directory);

                // ponytail: FileSystemWatcher can drop events under load. The
                // startup drain below and the drain on every download
                // completion are the recovery path; add polling only if a
                // dropped event is ever actually observed.
                _inboxWatcher = new FileSystemWatcher(_captureInbox.Directory, "*.json")
                {
                    NotifyFilter = NotifyFilters.FileName,
                    EnableRaisingEvents = true
                };
                _inboxWatcher.Created += (_, _) =>
                    Dispatcher.BeginInvoke(new Action(DrainCaptureInbox), DispatcherPriority.Background);
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"Capture inbox watcher unavailable: {ex.Message}");
            }

            DrainCaptureInbox();
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
            // Read and cleared before any asynchronous work so a stale flag can never leak into a later manual run.
            var unattended = _startingFromCapture;
            _startingFromCapture = false;

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
                        Trace.WriteLine($"Key conversion failed (invalid hex format): {ex.Message}");
                        MessageBox.Show(
                            Services.LanguageService.Default.GetString("Str_InvalidHexKey", "Invalid Hex Key format. Please check your key."),
                            Services.LanguageService.Default.GetString("Str_Error", "Error"),
                            MessageBoxButton.OK, MessageBoxImage.Error);
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
                            Trace.WriteLine($"Key conversion failed: {ex.Message}");
                            MessageBox.Show(
                                Services.LanguageService.Default.GetString("Str_InvalidHexKey", "Invalid Hex Key format. Please check your key."),
                                Services.LanguageService.Default.GetString("Str_Error", "Error"),
                                MessageBoxButton.OK, MessageBoxImage.Error);
                            return;
                        }
                    }
                }
            }
            // Skip the N_m3u8DL-RE.exe check when using Cloudflare bypass (Python m3u8_cf_bypass.py is used instead)
            if (CheckBox_BypassCF?.IsChecked != true && !File.Exists(TextBox_EXE.Text))
            {
                MessageBox.Show(
                    Services.LanguageService.Default.GetString("Str_DownloaderNotFoundMsg", "N_m3u8DL-RE.exe was not found.\n\nSet its path on the Download tab, or right-click the Executable field and choose Get Downloader."),
                    Services.LanguageService.Default.GetString("Str_DownloaderNotFoundTitle", "Downloader Not Found"),
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            if (TextBox_URL.Text == "")
            {
                MessageBox.Show(
                    Services.LanguageService.Default.GetString("Str_MissingInputMsg", "Enter a URL or file path first."),
                    Services.LanguageService.Default.GetString("Str_MissingInputTitle", "Missing Input"),
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            if (!InputValidation.IsValidProxy(TextBox_Proxy.Text))
            {
                MessageBox.Show(
                    Services.LanguageService.Default.GetString("Str_InvalidProxyMsg", "Proxy must start with http:// or socks5://."),
                    Services.LanguageService.Default.GetString("Str_InvalidProxyTitle", "Invalid Proxy"),
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // Abyss / Hydrax native download mode
            if (AbyssMetadataFetcher.IsAbyssUrl(TextBox_URL.Text))
            {
                Button_GO.IsEnabled = false;
                Button_Stop.Visibility = Visibility.Visible;
                try
                {
                    await StartAbyssDownloadAsync(TextBox_URL.Text, unattended);
                }
                finally
                {
                    SetDownloadIdle();
                }
                return;
            }

            // Batch download mode
            if (_batchScriptService.IsBatchInput(TextBox_URL.Text))
            {
                this.IsEnabled = false;
                Button_GO.SetResourceReference(ContentControl.ContentProperty, "Str_Working");
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
                    MessageBox.Show(
                        ex.Message,
                        Services.LanguageService.Default.GetString("Str_BatchBuildFailedTitle", "Batch build failed"),
                        MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }
                finally
                {
                    EndCancellableOperation(cts);
                    Button_GO.SetResourceReference(ContentControl.ContentProperty, "Str_Download");
                    this.IsEnabled = true;
                }

                if (result != null)
                {
                    Button_GO.IsEnabled = false;
                    Button_Stop.Visibility = Visibility.Visible;

                    _lastOutputDirectory = OptionValueNormalizer.NormalizeSaveDir(TextBox_WorkDir.Text)
                                           ?? Environment.CurrentDirectory;
                    ResetRunState();
                    SetStatus(Localize("Str_StatusRunningBatch", "Running batch…"));

                    var batchProgress = new Progress<int>(p => ProgressBar_Download.Value = p);
                    var batchLog = new Action<string>(line => Dispatcher.InvokeAsync(() => AppendLog(line)));

                    try
                    {
                        var batchOk = await _downloadService.StartProcessAsync(
                            result.FilePath, string.Empty, batchLog, batchProgress);

                        if (batchOk)
                        {
                            ProgressBar_Download.Value = 100;
                            var statusFmt = Services.LanguageService.Default.GetString("Str_StatusBatchDone", "Batch finished. Saved to {0}");
                            SetStatus(string.Format(statusFmt, _lastOutputDirectory));
                            Button_OpenFolder.Visibility = Visibility.Visible;
                        }
                        else
                        {
                            SetStatus(Services.LanguageService.Default.GetString("Str_StatusBatchFailed", "Batch failed — open the Log for details."), isError: true);
                            ToggleButton_Log.IsChecked = true;
                        }
                    }
                    catch (Exception ex)
                    {
                        SetStatus(Localize("Str_StatusBatchError", "Batch error: {0}", ex.Message), isError: true);
                        ToggleButton_Log.IsChecked = true;
                    }
                    finally
                    {
                        SetDownloadIdle();
                        try
                        {
                            if (File.Exists(result.FilePath))
                                File.Delete(result.FilePath);
                        }
                        catch (Exception ex)
                        {
                            Trace.WriteLine($"Failed to delete temp batch file '{result.FilePath}': {ex.Message}");
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
                        SetStatus(Services.LanguageService.Default.GetString("Str_StatusDownloading", "Downloading…"));

                        Services.ResumeJobStore.Default.Begin(
                            options.Input,
                            options.SaveName ?? string.Empty,
                            options.SaveDir ?? string.Empty,
                            options.TmpDir ?? string.Empty);

                        var progress = new Progress<int>(p => ProgressBar_Download.Value = p);
                        var log = new Action<string>(line => Dispatcher.InvokeAsync(() => AppendLog(line)));

                        var succeeded = await _downloadService.StartDownloadAsync(options, progress, log);

                        if (succeeded)
                        {
                            Services.ResumeJobStore.Default.Complete();
                            _activeResumeJob = null;
                            ProgressBar_Download.Value = 100;
                            var savedFmt = Services.LanguageService.Default.GetString("Str_StatusSavedTo", "Saved to {0}");
                            SetStatus(string.Format(savedFmt, _lastOutputDirectory));
                            Button_OpenFolder.Visibility = Visibility.Visible;
                        }
                        else
                        {
                            SetStatus(Services.LanguageService.Default.GetString("Str_StatusDownloadFailed", "Download failed — open the Log for details."), isError: true);
                            ToggleButton_Log.IsChecked = true;
                        }
                    }
                }
                catch (Exception ex)
                {
                    SetStatus(Localize("Str_StatusDownloadError", "Download error: {0}", ex.Message), isError: true);
                    ToggleButton_Log.IsChecked = true;
                }
                finally
                {
                    SetDownloadIdle();
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
                    Trace.WriteLine($"Failed to copy command: {ex.Message}");
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
                using var process = Process.Start(startInfo);
            }
            catch (System.ComponentModel.Win32Exception ex)
            {
                Trace.WriteLine($"Failed to open target '{targetPath}': {ex.Message}");
            }
        }

        /// <summary>
        /// Native C# downloader for Abyss/Hydrax video streams (abysscdn.com, playhydrax.com, short.ink, etc.).
        /// Automatically parses metadata, selects optimal quality, downloads fragmented chunks, and reassembles into MP4.
        /// </summary>
        private async Task StartAbyssDownloadAsync(string url, bool unattended)
        {
            var cts = BeginCancellableOperation();
            _lastOutputDirectory = OptionValueNormalizer.NormalizeSaveDir(TextBox_WorkDir.Text)
                                   ?? Environment.CurrentDirectory;

            ResetRunState();
            SetStatus(Localize("Str_StatusAbyssFetching", "Fetching Abyss/Hydrax video metadata…"));
            AppendLog($"{DateTime.Now:HH:mm:ss.fff} INFO : Abyss/Hydrax video stream detected: {url}");

            try
            {
                var customHeaders = HeaderParser.Parse(TextBox_Headers?.Text);
                if (customHeaders.TryGetValue("Referer", out var refererVal))
                {
                    AppendLog($"{DateTime.Now:HH:mm:ss.fff} INFO : Custom Referer: {refererVal}");
                }

                var mp4 = await AbyssMetadataFetcher.FetchMetadataAsync(url, customHeaders: customHeaders, cancellationToken: cts.Token);
                if (mp4.Sources == null || mp4.Sources.Count == 0)
                {
                    throw new InvalidOperationException("No downloadable video streams found in Abyss metadata.");
                }

                // Largest first. A person starting the download picks a quality when there is a
                // choice; an unattended capture run takes the largest.
                var ranked = AbyssSourceChoice.Ranked(mp4.Sources);
                var source = ranked[0];
                if (ranked.Count > 1 && !unattended)
                {
                    var picker = new Views.AbyssQualityWindow(ranked) { Owner = this };
                    if (picker.ShowDialog() != true || picker.Selected is null)
                    {
                        SetStatus(Localize("Str_StatusCancelled", "Cancelled."));
                        return;
                    }
                    source = picker.Selected;
                }

                var titleClean = _utilityService.GetValidFileName(TextBox_Title.Text);
                if (string.IsNullOrWhiteSpace(titleClean))
                    titleClean = $"abyss_{mp4.Slug}_{source.Label}";
                if (!titleClean.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase))
                    titleClean += ".mp4";

                string outputPath = Path.Combine(_lastOutputDirectory, titleClean);

                AppendLog($"{DateTime.Now:HH:mm:ss.fff} INFO : Video: {mp4.Slug} | Quality: {source.Label} | Size: {source.Size / (1024.0 * 1024.0):F1} MB | Codec: {source.Codec}");
                AppendLog($"{DateTime.Now:HH:mm:ss.fff} INFO : Output file: {outputPath}");
                SetStatus(Localize("Str_StatusAbyssSource", "Downloading Abyss stream: {0}…", source.Label));

                using var abyssService = new AbyssDownloadService();
                var progress = new Progress<AbyssDownloadProgress>(p =>
                {
                    Dispatcher.InvokeAsync(() =>
                    {
                        ProgressBar_Download.Value = (int)p.Percentage;
                        SetStatus(Localize("Str_StatusAbyssProgress", "Downloading Abyss: {0}", p));
                    });
                });

                var logAction = new Action<string>(line => Dispatcher.InvokeAsync(() => AppendLog(line)));

                await abyssService.DownloadAsync(
                    mp4,
                    source,
                    outputPath,
                    customHeaders: customHeaders,
                    connections: 8,
                    progress: progress,
                    log: logAction,
                    cancellationToken: cts.Token);

                ProgressBar_Download.Value = 100;
                SetStatus(Localize("Str_StatusSavedTo", "Saved to {0}", outputPath));
                Button_OpenFolder.Visibility = Visibility.Visible;
            }
            catch (OperationCanceledException)
            {
                SetStatus(Services.LanguageService.Default.GetString("Str_StatusStopped", "Download stopped by user."));
                AppendLog($"{DateTime.Now:HH:mm:ss.fff} WARN : Download cancelled by user.");
            }
            catch (Exception ex)
            {
                SetStatus(Localize("Str_StatusDownloadError", "Download error: {0}", ex.Message), isError: true);
                AppendLog($"{DateTime.Now:HH:mm:ss.fff} ERROR : {ex.Message}");
                ToggleButton_Log.IsChecked = true;
            }
            finally
            {
                EndCancellableOperation(cts);
            }
        }

        /// <summary>
        /// Build the CF options object for m3u8_cf_bypass.py.
        /// Uses dedicated CF bypass controls (TextBox_CFReferer, TextBox_CFCookie,
        /// Combo_CFImpersonate, CheckBox_CFKeepSegs) for a clean, predictable data path.
        /// </summary>
        /// <summary>
        /// The CF job id (CfCommandBuilder.ComputeJobId) is a hash of this exact string, so
        /// resume only finds last run's segments if every caller sanitizes the title
        /// identically. One method, not three copies of the same three lines.
        /// </summary>
        private string GetCfOutputFileName()
        {
            var titleClean = _utilityService.GetValidFileName(TextBox_Title.Text);
            if (string.IsNullOrWhiteSpace(titleClean)) titleClean = "output";
            if (!titleClean.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase)) titleClean += ".mp4";
            return titleClean;
        }

        private CfCommandOptions BuildCfOptions(string pythonExe = "python", string? specificSegDir = null)
        {
            string scriptPath = Path.Combine(AppContext.BaseDirectory, "m3u8_cf_bypass.py");
            if (!File.Exists(scriptPath))
                scriptPath = Path.Combine(Environment.CurrentDirectory, "m3u8_cf_bypass.py");

            var titleClean = GetCfOutputFileName();

            var headersDict = HeaderParser.Parse(TextBox_Headers?.Text);

            // Referer: CFReferer non-empty ? CFReferer : headers["Referer"] ?? DeriveReferer(url)
            string referer = "";
            if (!string.IsNullOrWhiteSpace(TextBox_CFReferer?.Text))
            {
                referer = TextBox_CFReferer.Text.Trim();
            }
            else if (headersDict.TryGetValue("Referer", out var refHeader) && !string.IsNullOrWhiteSpace(refHeader))
            {
                referer = refHeader.Trim();
            }
            else
            {
                referer = CfCommandBuilder.DeriveReferer(null, TextBox_URL.Text);
            }

            // Cookie: CFCookie non-empty ? CFCookie : headers["Cookie"] ?? ""
            string cookie = "";
            if (!string.IsNullOrWhiteSpace(TextBox_CFCookie?.Text))
            {
                cookie = TextBox_CFCookie.Text.Trim();
            }
            else if (headersDict.TryGetValue("Cookie", out var cookieHeader) && !string.IsNullOrWhiteSpace(cookieHeader))
            {
                cookie = cookieHeader.Trim();
            }

            // UserAgent: headers["User-Agent"] ?? ""
            string userAgent = "";
            if (headersDict.TryGetValue("User-Agent", out var uaHeader) && !string.IsNullOrWhiteSpace(uaHeader))
            {
                userAgent = uaHeader.Trim();
            }

            // Extra headers: every remaining header passing HeaderPolicy
            var extraHeaders = new List<CapturedHeader>();
            foreach (var kvp in headersDict)
            {
                if (kvp.Key.Equals("Referer", StringComparison.OrdinalIgnoreCase) ||
                    kvp.Key.Equals("Cookie", StringComparison.OrdinalIgnoreCase) ||
                    kvp.Key.Equals("User-Agent", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (HeaderPolicy.ShouldForward(kvp.Key))
                {
                    extraHeaders.Add(new CapturedHeader(kvp.Key, kvp.Value));
                }
            }

            string jobId = CfCommandBuilder.ComputeJobId(TextBox_URL.Text, titleClean);
            string segDir = !string.IsNullOrWhiteSpace(specificSegDir)
                ? specificSegDir
                : Path.Combine(AppContext.BaseDirectory, "cf_segments", jobId);

            string? engineTag = (Combo_CFEngine?.SelectedItem is ComboBoxItem cbe) ? cbe.Tag as string : null;
            var urlKind = CurlCommandParser.ClassifyUrl(TextBox_URL.Text ?? string.Empty);
            bool hasKey = !string.IsNullOrWhiteSpace(TextBox_Key?.Text);
            bool isProxy = CfEngineSelector.ShouldUseProxy(engineTag, urlKind, hasKey);

            return new CfCommandOptions(
                PythonExe: pythonExe,
                ScriptPath: scriptPath,
                Url: TextBox_URL.Text ?? string.Empty,
                OutputName: titleClean,
                WorkDir: string.IsNullOrWhiteSpace(TextBox_WorkDir.Text)
                    ? Environment.CurrentDirectory
                    : TextBox_WorkDir.Text,
                SegDir: segDir,
                Referer: referer,
                Cookie: cookie,
                Impersonate: (Combo_CFImpersonate?.SelectedItem is ComboBoxItem cfi && cfi.Tag is string tag && !string.IsNullOrEmpty(tag))
                    ? tag
                    : "chrome",
                KeepSegments: CheckBox_CFKeepSegs?.IsChecked == true,
                UserAgent: userAgent,
                ExtraHeaders: extraHeaders,
                Serve: isProxy);
        }

        /// <summary>
        /// Clean up stale batch files and pasted lists from %TEMP% directory created in previous runs.
        /// </summary>
        private static void CleanStaleTempBatchFiles()
        {
            try
            {
                var tempDir = Path.GetTempPath();
                var dirInfo = new DirectoryInfo(tempDir);
                var patterns = new[] { "cf_dl_*.bat", "batch_*.bat", "paste_*.txt" };

                foreach (var pattern in patterns)
                {
                    var staleFiles = dirInfo.GetFiles(pattern)
                        .Where(f => (DateTime.Now - f.LastWriteTime).TotalHours > 1);

                    foreach (var file in staleFiles)
                    {
                        try { file.Delete(); } catch { }
                    }
                }
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"Failed to clean stale temp files: {ex.Message}");
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
                    Services.LanguageService.Default.GetString("Str_CfScriptMissing", "m3u8_cf_bypass.py not found.\nPlease place it in the same directory as the GUI executable."),
                    Services.LanguageService.Default.GetString("Str_CfDialogTitle", "Cloudflare Bypass"),
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var cts = BeginCancellableOperation();
            string? pythonExe = null;
            try
            {
                pythonExe = await PythonLocator.FindWithCurlCffiAsync(cts.Token);
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
                    Services.LanguageService.Default.GetString("Str_PyCurlCffiMissing", "No Python interpreter with curl_cffi found.\n\nInstall the dependency once (run in your terminal):\n    pip install curl_cffi\nor: python -m pip install curl_cffi\n\nThen click Start Download again."),
                    Services.LanguageService.Default.GetString("Str_CfDialogTitle", "Cloudflare Bypass"),
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var titleClean = GetCfOutputFileName();

            string jobId = CfCommandBuilder.ComputeJobId(TextBox_URL.Text, titleClean);
            string jobSegDir = Path.Combine(AppContext.BaseDirectory, "cf_segments", jobId);
            var cfOptions = BuildCfOptions(pythonExe, jobSegDir);

            TextBox_Parameter.Text = CfCommandBuilder.BuildMaskedCommand(cfOptions);

            if (cfOptions.Serve)
            {
                await RunCfProxyThenReAsync(cfOptions);
                return;
            }

            // No .bat/cmd.exe wrapper: CreateNoWindow+redirected I/O below means no
            // console is ever shown, so the batch file bought nothing but a second,
            // incompatible quoting dialect (cmd.exe's, layered on top of the
            // already-correct C-runtime quoting CfCommandBuilder emits) — the exact
            // shape of bug N8/M6. Launching python.exe directly removes that layer
            // instead of adding a second escaping function to patch around it.
            Environment.SetEnvironmentVariable("PYTHONUTF8", "1");
            Environment.SetEnvironmentVariable("PYTHONIOENCODING", "utf-8");

            _lastOutputDirectory = string.IsNullOrWhiteSpace(TextBox_WorkDir.Text)
                ? Environment.CurrentDirectory
                : TextBox_WorkDir.Text;
            ResetRunState();
            SetStatus(Localize("Str_StatusCfRunning", "Running Cloudflare bypass…"));

            var cfProgress = new Progress<int>(p => ProgressBar_Download.Value = p);
            var cfLog = new Action<string>(line => Dispatcher.InvokeAsync(() => AppendLog(line)));

            var cfExitCode = await _downloadService.StartProcessWithExitCodeAsync(
                cfOptions.PythonExe, CfCommandBuilder.BuildArguments(cfOptions), cfLog, cfProgress);

            if (cfExitCode == 0)
            {
                ProgressBar_Download.Value = 100;
                SetStatus(Localize("Str_StatusSavedTo", "Saved to {0}", _lastOutputDirectory));
                Button_OpenFolder.Visibility = Visibility.Visible;
            }
            else if (cfExitCode == 3)
            {
                SetStatus(Localize("Str_StatusCfPartial", "Partial download saved to {0} (some segments failed).", _lastOutputDirectory), isWarning: true);
                Button_OpenFolder.Visibility = Visibility.Visible;
            }
            else if (cfExitCode == 4)
            {
                SetStatus(Localize("Str_StatusCfEncrypted", "Encrypted stream detected (#EXT-X-KEY). Use N_m3u8DL-RE directly with --key."), isWarning: true);
                ToggleButton_Log.IsChecked = true;
            }
            else if (cfExitCode == 5)
            {
                SetStatus(Localize("Str_StatusCfAccessDenied", "Access denied (401/403). Check Referer and Cookie credentials."), isWarning: true);
                ToggleButton_Log.IsChecked = true;
            }
            else if (cfExitCode == 6)
            {
                // The manifest has an init segment, a separate rendition or byte ranges,
                // which only N_m3u8DL-RE can assemble. Auto hands over; a pinned direct
                // engine is the user's explicit choice, so it is reported instead.
                var pinnedTag = (Combo_CFEngine?.SelectedItem is ComboBoxItem pinned) ? pinned.Tag as string : null;
                if (pinnedTag == "direct")
                {
                    SetStatus(Localize("Str_StatusCfNeedsProxyEngine", "This stream needs the proxy engine. Set the Cloudflare engine to Auto or Proxy."), isWarning: true);
                    ToggleButton_Log.IsChecked = true;
                }
                else
                {
                    AppendLog("The direct downloader cannot assemble this stream. Switching to the proxy engine.");
                    var proxyOptions = cfOptions with { Serve = true };
                    TextBox_Parameter.Text = CfCommandBuilder.BuildMaskedCommand(proxyOptions);
                    await RunCfProxyThenReAsync(proxyOptions);
                }
            }
            else
            {
                SetStatus(Localize("Str_StatusCfFailed", "Cloudflare bypass failed — open the Log for details."), isError: true);
                ToggleButton_Log.IsChecked = true;
            }
        }

        /// <summary>
        /// Runs the "CF proxy + RE" engine (N1): starts m3u8_cf_bypass.py --serve
        /// directly (no .bat, so its stdout/stdin can be read as a machine
        /// protocol rather than left for a human console), waits for the
        /// PROXY_READY handshake, then points N_m3u8DL-RE at the local proxy
        /// URL and lets it do the actual download — DASH track selection, CENC
        /// decryption and muxing all stay RE's job, Python only contributes the
        /// TLS fingerprint. The proxy is torn down (stdin EOF, then a kill if it
        /// doesn't exit) once RE finishes, whatever the outcome.
        /// </summary>
        private async System.Threading.Tasks.Task RunCfProxyThenReAsync(CfCommandOptions cfOptions)
        {
            _lastOutputDirectory = string.IsNullOrWhiteSpace(TextBox_WorkDir.Text)
                ? Environment.CurrentDirectory
                : TextBox_WorkDir.Text;
            ResetRunState();
            SetStatus(Localize("Str_StatusCfProxyStarting", "Starting Cloudflare proxy…"));

            var cts = BeginCancellableOperation();
            CfProxyHost? proxy = null;
            try
            {
                var started = await CfProxyHost.StartAsync(
                    CfProxyHost.BuildStartInfo(cfOptions.PythonExe, CfCommandBuilder.BuildArguments(cfOptions)),
                    line => Dispatcher.InvokeAsync(() => AppendLog(line)),
                    TimeSpan.FromSeconds(15),
                    cts.Token);

                switch (started.Status)
                {
                    case CfProxyStartStatus.Ready:
                        proxy = started.Host;
                        break;
                    case CfProxyStartStatus.StartFailed:
                        SetStatus(started.Error is null
                            ? Localize("Str_StatusCfProxyStartFailed", "Failed to start Cloudflare proxy.")
                            : Localize("Str_StatusCfProxyStartFailedEx", "Failed to start Cloudflare proxy: {0}", started.Error),
                            isError: true);
                        return;
                    case CfProxyStartStatus.Cancelled:
                        SetStatus(Localize("Str_StatusCancelled", "Cancelled."));
                        return;
                    default:
                        SetStatus(Localize("Str_StatusCfProxyNotReady", "Cloudflare proxy did not become ready in time — open the Log for details."), isError: true);
                        ToggleButton_Log.IsChecked = true;
                        return;
                }

                SetStatus(Localize("Str_StatusCfProxyReady", "Cloudflare proxy ready — starting N_m3u8DL-RE…"));

                var reOptions = BuildDownloadOptions();
                reOptions.Input = proxy!.ProxyUrl;
                // The proxy already authenticates the origin leg (Referer/Cookie/UA/
                // impersonation); RE only ever talks to 127.0.0.1 from here on, and a
                // stray Base URL pointing at the real CDN would route straight past
                // the proxy that is doing the TLS-fingerprint bypass.
                reOptions.Headers = string.Empty;
                reOptions.BaseUrl = string.Empty;
                // Neither a custom nor the system proxy may sit between RE and 127.0.0.1:
                // it would be handed loopback traffic it cannot route. The bypass proxy
                // has no upstream-proxy option, so a configured proxy is not applied here.
                if (!string.IsNullOrWhiteSpace(reOptions.Proxy))
                    AppendLog("Note: the custom proxy is not used by the Cloudflare proxy engine.");
                reOptions.Proxy = string.Empty;
                reOptions.UseSystemProxy = false;

                Services.ResumeJobStore.Default.Begin(reOptions.Input, reOptions.SaveName ?? string.Empty, reOptions.SaveDir ?? string.Empty, reOptions.TmpDir ?? string.Empty);

                var reProgress = new Progress<int>(p => ProgressBar_Download.Value = p);
                var reLog = new Action<string>(line => Dispatcher.InvokeAsync(() => AppendLog(line)));

                var succeeded = await _downloadService.StartDownloadAsync(reOptions, reProgress, reLog, cts.Token);

                if (succeeded)
                {
                    Services.ResumeJobStore.Default.Complete();
                    _activeResumeJob = null;
                    ProgressBar_Download.Value = 100;
                    SetStatus(Localize("Str_StatusSavedTo", "Saved to {0}", _lastOutputDirectory));
                    Button_OpenFolder.Visibility = Visibility.Visible;
                }
                else if (cts.IsCancellationRequested)
                {
                    SetStatus(Services.LanguageService.Default.GetString("Str_StatusCancelled", "Cancelled."));
                }
                else
                {
                    SetStatus(Localize("Str_StatusReFailed", "N_m3u8DL-RE failed — open the Log for details."), isError: true);
                    ToggleButton_Log.IsChecked = true;
                }
            }
            finally
            {
                EndCancellableOperation(cts);
                if (proxy != null)
                    await proxy.DisposeAsync();
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
                Trace.WriteLine($"Clipboard access failed (external lock): {ex.Message}");
                return string.Empty;
            }
            catch (InvalidOperationException ex)
            {
                Trace.WriteLine($"Clipboard access failed (thread state): {ex.Message}");
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
            using var process = Process.Start(startInfo);
            if (process == null)
                Trace.WriteLine($"Failed to start process: {executablePath}");
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
                        Services.LanguageService.Default.GetString("Str_InvalidKeyFileMsg",
                            "That file is not a valid key file. A raw HLS key must be exactly 16 bytes."),
                        Services.LanguageService.Default.GetString("Str_InvalidKeyFileTitle", "Invalid Key File"),
                        MessageBoxButton.OK, MessageBoxImage.Warning);
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
                if (TextBlock_UpdateStatus != null) TextBlock_UpdateStatus.Text = Localize("Str_UpdateChecking", "Checking...");

                var currentVer = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
                if (currentVer == null)
                {
                    // No safe guess exists: too low nags about the installed version, too
                    // high hides every real update. Say nothing rather than mislead.
                    if (isManual && TextBlock_UpdateStatus != null)
                        TextBlock_UpdateStatus.Text = Localize("Str_UpdateNoVersion", "Could not determine this app's version.");
                    return;
                }

                var service = new N_m3u8DL_RE_GUI.Core.Services.GitHubUpdateCheckService();
                var result = await service.CheckForUpdateAsync("naravid19", "N_m3u8DL_RE_GUI", currentVer);

                switch (result.Status)
                {
                    case N_m3u8DL_RE_GUI.Core.Services.UpdateCheckStatus.UpdateAvailable:
                        Button_UpdateBadge.Content = $"🎉 {result.LatestVersion} Available!";
                        Button_UpdateBadge.Tag = result.ReleaseUrl;
                        Button_UpdateBadge.Visibility = Visibility.Visible;
                        if (TextBlock_UpdateStatus != null)
                            TextBlock_UpdateStatus.Text = Localize("Str_UpdateAvailable", "{0} available!", result.LatestVersion);
                        break;

                    case N_m3u8DL_RE_GUI.Core.Services.UpdateCheckStatus.UpToDate:
                        Button_UpdateBadge.Visibility = Visibility.Collapsed;
                        if (TextBlock_UpdateStatus != null)
                        {
                            if (isManual)
                            {
                                TextBlock_UpdateStatus.Text = Localize("Str_UpdateLatest", "✓ Latest version");
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
                        break;

                    case N_m3u8DL_RE_GUI.Core.Services.UpdateCheckStatus.CheckFailed:
                    default:
                        if (TextBlock_UpdateStatus != null)
                        {
                            if (isManual)
                            {
                                TextBlock_UpdateStatus.Text = Localize("Str_UpdateCheckFailed", "Could not check for updates — check your connection.");
                            }
                            else
                            {
                                TextBlock_UpdateStatus.Text = "";
                            }
                        }
                        break;
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



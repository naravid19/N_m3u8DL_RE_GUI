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
        private bool _suspendParameterRefresh;
        private bool _isCheckingUpdate;
        private static readonly Media.SolidColorBrush ErrorBorderBrush = new(MediaColor.FromRgb(231, 76, 60));
        private static readonly Media.SolidColorBrush DefaultBorderBrush = new(MediaColor.FromRgb(63, 63, 70));

        public MainWindow()
        {
            InitializeComponent();
            TextBox_URL.Focus();
            var serviceProvider = ViewModels.ViewModelLocator.ServiceProvider;
            _utilityService = Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<Services.IUtilityService>(serviceProvider);
            _configService = Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<Services.IConfigService>(serviceProvider);
            _batchScriptService = Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<Services.IBatchScriptService>(serviceProvider);
            _dragDropService = Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<Services.IDragDropService>(serviceProvider);
        }

        private void Button_SelectDir_Click(object sender, RoutedEventArgs e)
        {
            var selectedPath = _utilityService.SelectFolder(Properties.Resources.String1);
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
                TextBox_Parameter.Text = BuildCfCommand();
            else
                TextBox_Parameter.Text = BuildArgsRE(TextBox_URL.Text);
        }

        private void ApplyValidationState(TextBox? textBox, bool isValid)
        {
            if (textBox == null)
                return;
            textBox.BorderBrush = isValid ? DefaultBorderBrush : ErrorBorderBrush;
        }

        private void RefreshValidationState()
        {
            ApplyValidationState(TextBox_URL, TextBox_URL == null || InputValidation.IsLikelyValidInput(TextBox_URL.Text));
            ApplyValidationState(TextBox_Proxy, TextBox_Proxy == null || InputValidation.IsValidProxy(TextBox_Proxy.Text));
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
                NoAnsiColor = CheckBox_NoAnsiColor?.IsChecked == true,
                DisableUpdateCheck = CheckBox_DisableUpdateCheck?.IsChecked == true,
            };

            return ArgsBuilder.Build(options);
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
            RefreshValidationState();
            GetParameter();
        }

        private void CheckBoxChanged(object sender, RoutedEventArgs e)
        {
            GetParameter();
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
                TextBox_Title.Text = await _utilityService.GetTitleFromUrlAsync(input);
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
                GetParameter();

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

        private async void Button_GO_Click(object sender, RoutedEventArgs e)
        {
            // Convert hex key to base64 if applicable
            try
            {
                string hex = TextBox_Key.Text.Replace("0x", "", StringComparison.OrdinalIgnoreCase).Replace("-", "").Replace(" ", "");
                if (hex.Length % 2 == 0 && Regex.IsMatch(hex, @"\A\b[0-9a-fA-F]+\b\Z"))
                    TextBox_Key.Text = Convert.ToBase64String(Convert.FromHexString(hex));
            }
            catch (FormatException ex)
            {
                Debug.WriteLine($"Key conversion failed (invalid hex format): {ex.Message}");
            }
            catch (ArgumentException ex)
            {
                Debug.WriteLine($"Key conversion failed (invalid key input): {ex.Message}");
            }
            // Skip the N_m3u8DL-RE.exe check when using Cloudflare bypass (Python m3u8_cf_bypass.py is used instead)
            if (CheckBox_BypassCF?.IsChecked != true && !File.Exists(TextBox_EXE.Text))
            {
                MessageBox.Show(Properties.Resources.String2);
                return;
            }
            if (TextBox_URL.Text == "")
            {
                MessageBox.Show(Properties.Resources.String3);
                return;
            }
            if (!InputValidation.IsValidProxy(TextBox_Proxy.Text))
            {
                MessageBox.Show(Properties.Resources.String7);
                return;
            }

            // Batch download mode
            if (_batchScriptService.IsBatchInput(TextBox_URL.Text))
            {
                this.IsEnabled = false;
                Button_GO.Content = Properties.Resources.String4;
                try
                {
                    var result = await _batchScriptService.BuildScriptAsync(
                        inputPath: TextBox_URL.Text,
                        exePath: TextBox_EXE.Text,
                        resolveTitleAsync: _utilityService.GetTitleFromUrlAsync,
                        buildArgsForInput: BuildArgsRE,
                        onTitleResolved: title => TextBox_Title.Text = title);

                    _batchScriptService.SaveScript(result.FilePath, result.Content);
                    StartShellTarget(result.FilePath);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(ex.Message);
                }
                finally
                {
                    Button_GO.Content = "GO";
                    this.IsEnabled = true;
                }
            }
            else
            {
                Button_GO.IsEnabled = false;
                try
                {
                    if (CheckBox_BypassCF?.IsChecked == true)
                    {
                        StartCloudflareDownload();
                    }
                    else
                    {
                        TextBox_Parameter.Text = BuildArgsRE();
                        StartExecutableWithArguments(TextBox_EXE.Text, TextBox_Parameter.Text);
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show(ex.Message);
                }
                finally
                {
                    Button_GO.IsEnabled = true;
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

        // ============================================================
        // Cloudflare Bypass via curl_cffi
        // ============================================================

        private static string EscapeBatchArg(string arg)
        {
            if (string.IsNullOrEmpty(arg)) return string.Empty;
            return arg.Replace("\"", "\\\"");
        }

        /// <summary>
        /// Build the Python command that invokes m3u8_cf_bypass.py.
        /// Uses dedicated CF bypass controls (TextBox_CFReferer, TextBox_CFCookie,
        /// Combo_CFImpersonate, CheckBox_CFKeepSegs) for a clean, predictable data path.
        /// </summary>
        private string BuildCfCommand(string pythonExe = "python")
        {
            string scriptPath = Path.Combine(AppContext.BaseDirectory, "m3u8_cf_bypass.py");
            if (!File.Exists(scriptPath))
                scriptPath = Path.Combine(Environment.CurrentDirectory, "m3u8_cf_bypass.py");

            string url = EscapeBatchArg(TextBox_URL.Text);
            string titleClean = GetValidFileName(TextBox_Title.Text);
            if (string.IsNullOrWhiteSpace(titleClean)) titleClean = "output";
            if (!titleClean.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase)) titleClean += ".mp4";
            string title = EscapeBatchArg(titleClean);

            string saveDirRaw = string.IsNullOrWhiteSpace(TextBox_WorkDir.Text)
                ? Environment.CurrentDirectory
                : TextBox_WorkDir.Text;
            string saveDir = EscapeBatchArg(saveDirRaw);

            // Segment temp directory next to GUI exe (doesn't pollute user save dir).
            // Merged successfully → m3u8_cf_bypass.py auto-deletes it.
            string segDir = EscapeBatchArg(Path.Combine(AppContext.BaseDirectory, "cf_segments"));

            // --- CF-specific controls ---
            // Referer: read from dedicated CF Referer field. If blank, auto-derive from input URL domain.
            string referer = TextBox_CFReferer?.Text?.Trim() ?? string.Empty;
            if (string.IsNullOrEmpty(referer) && !string.IsNullOrWhiteSpace(TextBox_URL.Text))
            {
                if (Uri.TryCreate(TextBox_URL.Text.Trim(), UriKind.Absolute, out var parsedUri))
                {
                    referer = parsedUri.GetLeftPart(UriPartial.Authority) + "/";
                }
            }

            // Cookie: read from dedicated CF Cookie field.
            string cookie = TextBox_CFCookie?.Text?.Trim() ?? string.Empty;

            // Impersonation fingerprint: read from ComboBox selection.
            string impersonate = "chrome";
            if (Combo_CFImpersonate?.SelectedItem is ComboBoxItem cfi && cfi.Tag is string tag && !string.IsNullOrEmpty(tag))
                impersonate = tag;

            var cmd = $"\"{pythonExe}\" \"{scriptPath}\" \"{url}\" --referer \"{EscapeBatchArg(referer)}\" -o \"{title}\" --work-dir \"{saveDir}\" --seg-dir \"{segDir}\" --impersonate \"{impersonate}\"";
            if (!string.IsNullOrEmpty(cookie))
                cmd += $" --cookie \"{EscapeBatchArg(cookie)}\"";
            // CheckBox_CFKeepSegs: when checked, keep segments after merge.
            if (CheckBox_CFKeepSegs?.IsChecked == true)
                cmd += " --keep-segs";
            return cmd;
        }

        /// <summary>
        /// Sanitise a string so it can be used as a Windows filename.
        /// </summary>
        private static string GetValidFileName(string name)
        {
            foreach (char c in Path.GetInvalidFileNameChars())
                name = name.Replace(c, '_');
            return name.Trim();
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
        private static string? FindPythonWithCurlCffi()
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
                        // Read both streams to avoid deadlock, wait up to 10 s.
                        p.StandardOutput.ReadToEnd();
                        p.StandardError.ReadToEnd();
                        if (!p.WaitForExit(10000))
                        {
                            try { p.Kill(); } catch { }
                            continue;
                        }
                        if (p.ExitCode == 0)
                            return c;
                    }
                }
                catch { }
            }
            return null;
        }

        /// <summary>
        /// Launch m3u8_cf_bypass.py via a temp .bat so the console window stays open
        /// and the user can see download progress / errors.
        /// Resolves a Python that has curl_cffi installed so the download actually runs.
        /// </summary>
        private void StartCloudflareDownload()
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

            // Resolve a Python that actually has curl_cffi installed.
            string? pythonExe = FindPythonWithCurlCffi();
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

            string cfCmd = BuildCfCommand(pythonExe);
            TextBox_Parameter.Text = cfCmd;

            var sb = new StringBuilder();
            sb.AppendLine("@echo off");
            sb.AppendLine("title N_m3u8DL-RE (Cloudflare Bypass Mode)");
            sb.AppendLine("chcp 65001 >nul");
            sb.AppendLine("set PYTHONUTF8=1");
            sb.AppendLine(cfCmd);
            sb.AppendLine("echo.");
            sb.AppendLine("pause");

            string bat = Path.Combine(Path.GetTempPath(), "cf_dl_" + DateTime.Now.ToString("yyyyMMddHHmmss") + ".bat");
            // UTF-8 without BOM + chcp 65001 + PYTHONUTF8=1 avoids the '∩╗┐@echo' warning in CMD
            File.WriteAllText(bat, sb.ToString(), new UTF8Encoding(false));
            StartShellTarget(bat);
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
                    MessageBox.Show(Properties.Resources.String6);
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
                var currentVer = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version ?? new Version(2, 1, 3);
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



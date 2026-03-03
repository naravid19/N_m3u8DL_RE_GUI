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
                SelectAudio = CheckBox_AudioOnly?.IsChecked == true ? "best" : TextBox_SelectAudio?.Text?.Trim(),
                SelectSubtitle = TextBox_SelectSubtitle?.Text?.Trim(),
                DropVideo = CheckBox_AudioOnly?.IsChecked == true ? "true" : TextBox_DropVideo?.Text?.Trim(),
                DropAudio = TextBox_DropAudio?.Text?.Trim(),
                DropSubtitle = TextBox_DropSubtitle?.Text?.Trim(),
                
                // Advanced Settings
                SavePattern = TextBox_SavePattern?.Text?.Trim(),
                FFmpegBinaryPath = TextBox_FFmpegPath?.Text?.Trim(),
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
            //从剪切板读取url
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
            if (!File.Exists(TextBox_EXE.Text))
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
                    TextBox_Parameter.Text = BuildArgsRE();
                    StartExecutableWithArguments(TextBox_EXE.Text, TextBox_Parameter.Text);
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

        private void SetTopMost(object sender, RoutedEventArgs e)
        {
            if (CheckBox_TopMost.IsChecked == true) 
            {
                Topmost = true;
            }
            else
            {
                Topmost = false;
            }
        }

        private void Menu_GetDownloader(object sender, RoutedEventArgs e)
        {
            StartShellTarget("https://github.com/nilaoda/N_m3u8DL-RE/releases");
        }

        private static void StartShellTarget(string targetPath)
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = targetPath,
                UseShellExecute = true
            };
            Process.Start(startInfo);
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
            Process.Start(startInfo);
        }

        /// <summary> 
        /// 给定文件的路径，读取文件的二进制数据，判断文件的编码类型 
        /// </summary> 
        /// <param name=“FILE_NAME“>文件路径</param> 
        /// <returns>文件的编码类型</returns> 
        public static Encoding GetType(string FILE_NAME)
        {
            return TextEncodingDetector.DetectFromFile(FILE_NAME);
        }

        /// <summary> 
        /// 通过给定的文件流，判断文件的编码类型 
        /// </summary> 
        /// <param name=“fs“>文件流</param> 
        /// <returns>文件的编码类型</returns> 
        public static Encoding GetType(FileStream fs)
        {
            return TextEncodingDetector.DetectFromStream(fs);
        }

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

    }
}


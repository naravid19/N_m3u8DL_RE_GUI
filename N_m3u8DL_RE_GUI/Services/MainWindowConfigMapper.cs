#nullable enable
using WpfCheckBox = System.Windows.Controls.CheckBox;
using WpfComboBox = System.Windows.Controls.ComboBox;
using WpfComboBoxItem = System.Windows.Controls.ComboBoxItem;
using WpfTextBox = System.Windows.Controls.TextBox;

namespace N_m3u8DL_RE_GUI.Services;

/// <summary>
/// Maps MainWindow controls to/from legacy config state.
/// Keeps key names and persistence format backward-compatible.
/// </summary>
internal static class MainWindowConfigMapper
{
    public static AppConfigState Capture(MainWindow window)
    {
        var state = new AppConfigState();
        state.SetEncodedBase64("程序路径", window.TextBox_EXE.Text);
        state.SetEncodedBase64("保存路径", window.TextBox_WorkDir.Text);
        state.SetEncodedBase64("代理", window.TextBox_Proxy.Text);
        state.SetEncodedBase64("请求头", window.TextBox_Headers.Text);

        state.Set("删除临时文件", Flag(window.CheckBox_Del.IsChecked == true));
        state.Set("二进制合并", Flag(window.CheckBox_BinaryMerge.IsChecked == true));
        state.Set("仅解析模式", Flag(window.CheckBox_ParserOnly.IsChecked == true));
        state.Set("不写入日期", Flag(window.CheckBox_DisableDate.IsChecked == true));
        state.Set("最大线程", window.TextBox_Max.Text);
        state.Set("重试次数", window.TextBox_Retry.Text);
        state.Set("超时秒数", window.TextBox_Timeout.Text);
        state.Set("最大速度", window.TextBox_MaxSpeed.Text);
        state.Set("不合并", Flag(window.CheckBox_DisableMerge.IsChecked == true));
        state.Set("不使用系统代理", Flag(window.CheckBox_DisableProxy.IsChecked == true));
        state.Set("仅合并音频", Flag(window.CheckBox_AudioOnly.IsChecked == true));
        state.Set("不检查分片", Flag(window.CheckBox_DisableCheck.IsChecked == true));
        state.Set("并发下载", Flag(window.CheckBox_Concurrent.IsChecked == true));
        state.Set("仅下载字幕", Flag(window.CheckBox_SubOnly.IsChecked == true));
        state.Set("自动修复字幕", Flag(window.CheckBox_AutoSubFix.IsChecked == true));
        state.Set("自动选择", Flag(window.CheckBox_AutoSelect.IsChecked == true));
        state.Set("字幕格式", (window.Combo_SubFormat.SelectedItem as WpfComboBoxItem)?.Content?.ToString());

        state.Set("MuxAfterDone", Flag(window.CheckBox_MuxAfterDone.IsChecked == true));
        state.Set("MuxFormat", (window.Combo_MuxFormat.SelectedItem as WpfComboBoxItem)?.Content?.ToString());
        state.Set("Muxer", (window.Combo_Muxer.SelectedItem as WpfComboBoxItem)?.Content?.ToString());
        state.Set("MuxBinPath", window.TextBox_MuxBinPath.Text);
        state.Set("MuxKeepFiles", Flag(window.CheckBox_MuxKeepFiles.IsChecked == true));
        state.Set("MuxSkipSub", Flag(window.CheckBox_MuxSkipSub.IsChecked == true));
        state.Set("MuxImport", window.TextBox_MuxJson.Text);
        state.Set("MuxJson", window.TextBox_MuxJson.Text);
        state.Set("LivePerformAsVod", Flag(window.CheckBox_LivePerformAsVod.IsChecked == true));
        state.Set("LiveRealTimeMerge", Flag(window.CheckBox_LiveRealTimeMerge.IsChecked == true));
        state.Set("LiveKeepSegments", Flag(window.CheckBox_LiveKeepSegments.IsChecked == true));
        state.Set("LivePipeMux", Flag(window.CheckBox_LivePipeMux.IsChecked == true));
        state.Set("LiveFixVttByAudio", Flag(window.CheckBox_LiveFixVttByAudio.IsChecked == true));
        state.Set("LiveRecordLimit", window.TextBox_LiveRecordLimit.Text);
        state.Set("LiveWaitTime", window.TextBox_LiveWaitTime.Text);
        state.Set("LiveTakeCount", window.TextBox_LiveTakeCount.Text);
        state.Set("SelectVideo", window.TextBox_SelectVideo.Text);
        state.Set("SelectAudio", window.TextBox_SelectAudio.Text);
        state.Set("SelectSubtitle", window.TextBox_SelectSubtitle.Text);
        state.Set("DropVideo", window.TextBox_DropVideo.Text);
        state.Set("DropAudio", window.TextBox_DropAudio.Text);
        state.Set("DropSubtitle", window.TextBox_DropSubtitle.Text);
        state.Set("DecryptionEngine", window.Combo_DecryptionEngine.SelectedIndex.ToString());
        state.Set("CustomHLSMethod", window.Combo_CustomHLSMethod.SelectedIndex.ToString());
        state.Set("DecryptionBinPath", window.TextBox_DecryptionBinPath.Text);
        state.Set("KeyTextFile", window.TextBox_KeyTextFile.Text);
        state.Set("MP4RealTimeDecryption", Flag(window.CheckBox_MP4RealTimeDecryption.IsChecked == true));
        state.Set("SavePattern", window.TextBox_SavePattern.Text);
        state.Set("FFmpegPath", window.TextBox_FFmpegPath.Text);
        state.Set("AdKeyword", window.TextBox_AdKeyword.Text);
        state.Set("LogLevel", window.Combo_LogLevel.SelectedIndex.ToString());
        state.Set("UILanguage", window.Combo_UILanguage.SelectedIndex.ToString());
        state.Set("AppendUrlParams", Flag(window.CheckBox_AppendUrlParams.IsChecked == true));
        state.Set("NoLog", Flag(window.CheckBox_NoLog.IsChecked == true));
        state.Set("WriteMetaJson", Flag(window.CheckBox_WriteMetaJson.IsChecked == true));
        state.Set("UseFFmpegConcat", Flag(window.CheckBox_UseFFmpegConcat.IsChecked == true));
        state.Set("AllowHlsMultiExtMap", Flag(window.CheckBox_AllowHlsMultiExtMap.IsChecked == true));
        state.Set("DisableUpdateCheck", Flag(window.CheckBox_DisableUpdateCheck.IsChecked == true));

        state.Set("TmpDir", window.TextBox_TmpDir.Text);
        state.Set("CustomHLSKey", window.TextBox_CustomHLSKey.Text);
        state.Set("CustomHLSIv", window.TextBox_IV.Text);
        state.Set("IV", window.TextBox_IV.Text);
        state.Set("UrlProcessorArgs", window.TextBox_UrlProcessorArgs.Text);
        state.Set("TaskStartAt", window.TextBox_TaskStartAt.Text);
        state.Set("ForceAnsiConsole", Flag(window.CheckBox_ForceAnsiConsole.IsChecked == true));
        state.Set("NoAnsiColor", Flag(window.CheckBox_NoAnsiColor.IsChecked == true));

        return state;
    }

    public static void Restore(MainWindow window, AppConfigState config)
    {
        RestoreTextBox(window.TextBox_EXE, config.GetDecodedBase64("程序路径"));
        RestoreTextBox(window.TextBox_WorkDir, config.GetDecodedBase64("保存路径"));
        RestoreTextBox(window.TextBox_Proxy, config.GetDecodedBase64("代理"));
        RestoreTextBox(window.TextBox_Headers, config.GetDecodedBase64("请求头"));

        RestoreCheckBox(window.CheckBox_Del, config.Get("删除临时文件"));
        RestoreCheckBox(window.CheckBox_BinaryMerge, config.Get("二进制合并"));
        RestoreCheckBox(window.CheckBox_ParserOnly, config.Get("仅解析模式"));
        RestoreCheckBox(window.CheckBox_DisableDate, config.Get("不写入日期"));
        RestoreTextBox(window.TextBox_Max, config.Get("最大线程"));
        RestoreTextBox(window.TextBox_Retry, config.Get("重试次数"));
        RestoreTextBox(window.TextBox_Timeout, config.Get("超时秒数"));
        RestoreTextBox(window.TextBox_MaxSpeed, config.Get("最大速度"));
        RestoreCheckBox(window.CheckBox_DisableMerge, config.Get("不合并"));
        RestoreCheckBox(window.CheckBox_DisableProxy, config.Get("不使用系统代理"));
        RestoreCheckBox(window.CheckBox_AudioOnly, config.Get("仅合并音频"));
        RestoreCheckBox(window.CheckBox_DisableCheck, config.Get("不检查分片"));
        RestoreCheckBox(window.CheckBox_Concurrent, config.Get("并发下载"));
        RestoreCheckBox(window.CheckBox_SubOnly, config.Get("仅下载字幕"));
        RestoreCheckBox(window.CheckBox_AutoSubFix, config.Get("自动修复字幕"));
        RestoreCheckBox(window.CheckBox_AutoSelect, config.Get("自动选择"));
        RestoreComboByContent(window.Combo_SubFormat, config.Get("字幕格式"));

        RestoreCheckBox(window.CheckBox_MuxAfterDone, config.Get("MuxAfterDone"));
        RestoreComboByContent(window.Combo_MuxFormat, config.Get("MuxFormat"));
        RestoreComboByContent(window.Combo_Muxer, config.Get("Muxer"));
        RestoreTextBox(window.TextBox_MuxBinPath, config.Get("MuxBinPath"));
        RestoreCheckBox(window.CheckBox_MuxKeepFiles, config.Get("MuxKeepFiles"));
        RestoreCheckBox(window.CheckBox_MuxSkipSub, config.Get("MuxSkipSub"));
        RestoreTextBox(window.TextBox_MuxJson, ResolveMuxImport(config));
        RestoreCheckBox(window.CheckBox_LivePerformAsVod, config.Get("LivePerformAsVod"));
        RestoreCheckBox(window.CheckBox_LiveRealTimeMerge, config.Get("LiveRealTimeMerge"));
        RestoreCheckBox(window.CheckBox_LiveKeepSegments, config.Get("LiveKeepSegments"));
        RestoreCheckBox(window.CheckBox_LivePipeMux, config.Get("LivePipeMux"));
        RestoreCheckBox(window.CheckBox_LiveFixVttByAudio, config.Get("LiveFixVttByAudio"));
        RestoreTextBox(window.TextBox_LiveRecordLimit, config.Get("LiveRecordLimit"));
        RestoreTextBox(window.TextBox_LiveWaitTime, config.Get("LiveWaitTime"));
        RestoreTextBox(window.TextBox_LiveTakeCount, config.Get("LiveTakeCount"));
        RestoreTextBox(window.TextBox_SelectVideo, config.Get("SelectVideo"));
        RestoreTextBox(window.TextBox_SelectAudio, config.Get("SelectAudio"));
        RestoreTextBox(window.TextBox_SelectSubtitle, config.Get("SelectSubtitle"));
        RestoreTextBox(window.TextBox_DropVideo, config.Get("DropVideo"));
        RestoreTextBox(window.TextBox_DropAudio, config.Get("DropAudio"));
        RestoreTextBox(window.TextBox_DropSubtitle, config.Get("DropSubtitle"));
        RestoreComboByIndex(window.Combo_DecryptionEngine, config.Get("DecryptionEngine"));
        RestoreComboByIndex(window.Combo_CustomHLSMethod, config.Get("CustomHLSMethod"));
        RestoreTextBox(window.TextBox_DecryptionBinPath, config.Get("DecryptionBinPath"));
        RestoreTextBox(window.TextBox_KeyTextFile, config.Get("KeyTextFile"));
        RestoreCheckBox(window.CheckBox_MP4RealTimeDecryption, config.Get("MP4RealTimeDecryption"));
        RestoreTextBox(window.TextBox_SavePattern, config.Get("SavePattern"));
        RestoreTextBox(window.TextBox_FFmpegPath, config.Get("FFmpegPath"));
        RestoreTextBox(window.TextBox_AdKeyword, config.Get("AdKeyword"));
        RestoreComboByIndex(window.Combo_LogLevel, config.Get("LogLevel"));
        RestoreComboByIndex(window.Combo_UILanguage, config.Get("UILanguage"));
        RestoreCheckBox(window.CheckBox_AppendUrlParams, config.Get("AppendUrlParams"));
        RestoreCheckBox(window.CheckBox_NoLog, config.Get("NoLog"));
        RestoreCheckBox(window.CheckBox_WriteMetaJson, config.Get("WriteMetaJson"));
        RestoreCheckBox(window.CheckBox_UseFFmpegConcat, config.Get("UseFFmpegConcat"));
        RestoreCheckBox(window.CheckBox_AllowHlsMultiExtMap, config.Get("AllowHlsMultiExtMap"));
        RestoreCheckBox(window.CheckBox_DisableUpdateCheck, config.Get("DisableUpdateCheck"));

        RestoreTextBox(window.TextBox_TmpDir, config.Get("TmpDir"));
        RestoreTextBox(window.TextBox_CustomHLSKey, config.Get("CustomHLSKey"));
        RestoreTextBox(window.TextBox_IV, ResolveCustomHlsIv(config));
        RestoreTextBox(window.TextBox_UrlProcessorArgs, config.Get("UrlProcessorArgs"));
        RestoreTextBox(window.TextBox_TaskStartAt, config.Get("TaskStartAt"));
        RestoreCheckBox(window.CheckBox_ForceAnsiConsole, config.Get("ForceAnsiConsole"));
        RestoreCheckBox(window.CheckBox_NoAnsiColor, config.Get("NoAnsiColor"));
    }

    private static string Flag(bool value) => value ? "1" : "0";

    private static void RestoreCheckBox(WpfCheckBox? cb, string value)
    {
        if (cb != null && !string.IsNullOrEmpty(value))
            cb.IsChecked = value == "1";
    }

    private static void RestoreTextBox(WpfTextBox? tb, string value)
    {
        if (tb != null && !string.IsNullOrEmpty(value))
            tb.Text = value;
    }

    private static void RestoreComboByIndex(WpfComboBox? combo, string value)
    {
        if (combo != null && int.TryParse(value, out var index) && index >= 0 && index < combo.Items.Count)
            combo.SelectedIndex = index;
    }

    private static void RestoreComboByContent(WpfComboBox? combo, string value)
    {
        if (combo == null || string.IsNullOrEmpty(value))
            return;

        foreach (var rawItem in combo.Items)
        {
            if (rawItem is WpfComboBoxItem item && item.Content?.ToString() == value)
            {
                combo.SelectedItem = item;
                break;
            }
        }
    }

    internal static string ResolveMuxImport(AppConfigState config)
    {
        var muxImport = config.Get("MuxImport");
        return string.IsNullOrWhiteSpace(muxImport) ? config.Get("MuxJson") : muxImport;
    }

    internal static string ResolveCustomHlsIv(AppConfigState config)
    {
        var customHlsIv = config.Get("CustomHLSIv");
        return string.IsNullOrWhiteSpace(customHlsIv) ? config.Get("IV") : customHlsIv;
    }
}

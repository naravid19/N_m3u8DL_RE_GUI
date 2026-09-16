#nullable enable
using System.Collections.Generic;
using System.Windows;
using N_m3u8DL_RE_GUI.Core.Capture;

namespace N_m3u8DL_RE_GUI.Views;

/// <summary>Modal list shown when a capture yields more than one stream candidate.</summary>
public partial class StreamPickerWindow : Window
{
    public CapturedRequest? Selected { get; private set; }

    public StreamPickerWindow(IReadOnlyList<CapturedRequest> candidates)
    {
        InitializeComponent();
        // Same native-title-bar handling as MainWindow: the palette below only covers
        // the client area, the system caption is DWM's and must be told separately.
        Services.ThemeManager.TrackWindow(this);
        List_Candidates.ItemsSource = candidates;
        List_Candidates.SelectedIndex = 0;
    }

    private void Button_Use_Click(object sender, RoutedEventArgs e) => Accept();

    private void List_Candidates_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e) => Accept();

    private void Accept()
    {
        Selected = List_Candidates.SelectedItem as CapturedRequest;
        if (Selected is null)
            return;

        DialogResult = true;
        Close();
    }
}

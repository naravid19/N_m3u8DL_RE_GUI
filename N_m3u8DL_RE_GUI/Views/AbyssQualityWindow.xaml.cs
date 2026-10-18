#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using N_m3u8DL_RE_GUI.Core.Abyss;

namespace N_m3u8DL_RE_GUI.Views;

/// <summary>Modal list shown when an Abyss/Hydrax video offers more than one quality.</summary>
public partial class AbyssQualityWindow : Window
{
    public AbyssSource? Selected { get; private set; }

    public AbyssQualityWindow(IReadOnlyList<AbyssSource> rankedSources)
    {
        InitializeComponent();
        List_Qualities.ItemsSource = rankedSources
            .Select(s => new QualityRow(s, AbyssSourceChoice.Describe(s)))
            .ToList();
        List_Qualities.SelectedIndex = 0;
    }

    private sealed record QualityRow(AbyssSource Source, string Description);

    private void Button_Use_Click(object sender, RoutedEventArgs e) => Accept();

    private void List_Qualities_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e) => Accept();

    private void Accept()
    {
        Selected = (List_Qualities.SelectedItem as QualityRow)?.Source;
        if (Selected is null)
            return;

        DialogResult = true;
        Close();
    }
}

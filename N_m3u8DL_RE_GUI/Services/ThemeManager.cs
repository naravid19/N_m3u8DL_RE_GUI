using System;
using System.Linq;
using System.Windows;
using Application = System.Windows.Application;

namespace N_m3u8DL_RE_GUI.Services;

/// <summary>
/// Swaps the app-wide theme dictionary (Themes/Theme.Dark.xaml or Theme.Light.xaml)
/// at runtime. Every brush reference in the XAML is a DynamicResource, so replacing
/// the merged dictionary re-colours the whole visual tree — including already
/// rendered windows and log rows that used SetResourceReference.
/// </summary>
public static class ThemeManager
{
    public const string Dark = "Dark";
    public const string Light = "Light";

    private const string ThemeMarker = "Themes/Theme.";

    /// <summary>Name of the theme currently in effect — always <see cref="Dark"/> or <see cref="Light"/>.</summary>
    public static string Current { get; private set; } = Dark;

    /// <summary>Applies the named theme; unknown names fall back to Dark.</summary>
    public static void Apply(string? theme)
    {
        var name = theme == Light ? Light : Dark;
        Current = name;

        var app = Application.Current;
        if (app == null)
            return;

        var dictionaries = app.Resources.MergedDictionaries;

        // Idempotent: re-selecting the active theme must not rebuild the dictionary.
        // The palette is not necessarily the only merged dictionary (Themes/Icons.xaml
        // rides along), so match on content instead of on the dictionary count. The
        // title-bar pass below still runs, so a window opened since the last Apply
        // gets themed even on this early path.
        if (!dictionaries.Any(d => d.Source?.OriginalString?.Contains(ThemeMarker + name) == true))
        {
            for (var i = dictionaries.Count - 1; i >= 0; i--)
            {
                if (dictionaries[i].Source?.OriginalString?.Contains(ThemeMarker) == true)
                    dictionaries.RemoveAt(i);
            }

            dictionaries.Insert(0, new ResourceDictionary
            {
                Source = new Uri($"{ThemeMarker}{name}.xaml", UriKind.Relative)
            });
        }

        // The palette swap above only re-colours the client area; the native caption
        // belongs to DWM and has to be told separately, on every theme change.
        var dark = name == Dark;
        foreach (Window window in app.Windows)
            TitleBarTheme.Apply(window, dark);
    }

    /// <summary>
    /// Keeps <paramref name="window"/>'s native title bar in sync with the active theme,
    /// now and on every later <see cref="Apply"/>. Call from the constructor: the HWND
    /// does not exist yet there, so the real work happens on SourceInitialized (before
    /// the first frame is rendered), and Apply covers runtime switches afterwards.
    /// </summary>
    public static void TrackWindow(Window window)
    {
        TitleBarTheme.Apply(window, Current == Dark);
        window.SourceInitialized += (_, _) => TitleBarTheme.Apply(window, Current == Dark);
    }
}

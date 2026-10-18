#nullable enable
using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using Microsoft.Win32;
using N_m3u8DL_RE_GUI.Core.NativeBridge;

namespace N_m3u8DL_RE_GUI.Services;

/// <summary>
/// Publishes this executable as a Chrome native messaging host.
///
/// Runs on every normal startup rather than once at install time: there is no
/// installer, and rewriting is how a moved or re-extracted copy of the app
/// repairs its own registration. Everything is under HKCU, so no elevation is
/// involved and nothing outside this user's profile is touched.
/// </summary>
public static class NativeHostRegistrar
{
    public static void Register()
    {
        try
        {
            var exePath = Environment.ProcessPath;
            if (string.IsNullOrEmpty(exePath))
                return;

            var manifestPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "N_m3u8DL_RE_GUI",
                "native-host.json");

            Directory.CreateDirectory(Path.GetDirectoryName(manifestPath)!);

            var json = NativeHostManifest.BuildJson(exePath);
            if (!File.Exists(manifestPath) || File.ReadAllText(manifestPath) != json)
                File.WriteAllText(manifestPath, json, new UTF8Encoding(false));

            foreach (var keyPath in NativeHostManifest.RegistryKeyPaths)
            {
                try
                {
                    using var key = Registry.CurrentUser.CreateSubKey(keyPath);
                    key?.SetValue(null, manifestPath);
                }
                catch (Exception ex)
                {
                    // A browser that is not installed is the normal case here.
                    Trace.WriteLine($"Native host registration skipped for {keyPath}: {ex.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            // Registration is a convenience. Failing it must never stop the app
            // from starting — the extension simply falls back to Copy as cURL.
            Trace.WriteLine($"Native host registration failed: {ex.Message}");
        }
    }
}

#nullable enable
using System;
using System.Collections.Generic;
using System.Text.Json;

namespace N_m3u8DL_RE_GUI.Core.NativeBridge;

/// <summary>
/// The strings that make Chrome willing to launch this application as a
/// native messaging host. Pure text, so the whole contract is unit tested
/// without touching the registry or the disk.
/// </summary>
public static class NativeHostManifest
{
    public const string HostName = "com.nm3u8dlre.gui";

    /// <summary>
    /// Fixed by the "key" field in extension/manifest.json. An unpacked
    /// extension without that key gets a path-derived ID, which would differ
    /// on every machine and make this constant wrong for everyone but the
    /// author. See extension/test/extension-identity.test.js.
    /// </summary>
    public const string ExtensionId = "ecdmbdclbopgpbbhdelncjhbnapnblgd";
    public const string FirefoxExtensionId = "nre-stream-bridge@naravid19";

    /// <summary>
    /// Named mutex a running GUI holds for its whole lifetime, purely so a
    /// native-host process can ask "does a real GUI exist" via
    /// Mutex.TryOpenExisting. A process-name check cannot answer that: a
    /// transient native-host process carries the same image name as the real
    /// GUI for the brief time it runs, so two Download clicks close enough
    /// together could each see the other's host process, each conclude a GUI
    /// is already running, and neither would start one.
    /// </summary>
    public const string SingleInstanceMutexName = "N_m3u8DL_RE_GUI.SingleInstance";

    /// <summary>Chrome compares this exactly, trailing slash included.</summary>
    public static string AllowedOrigin => $"chrome-extension://{ExtensionId}/";

    public static IReadOnlyList<string> RegistryKeyPaths { get; } = new[]
    {
        $@"Software\Google\Chrome\NativeMessagingHosts\{HostName}",
        $@"Software\Microsoft\Edge\NativeMessagingHosts\{HostName}",
        $@"Software\BraveSoftware\Brave-Browser\NativeMessagingHosts\{HostName}"
    };

    public static string FirefoxRegistryKeyPath => $@"Software\Mozilla\NativeMessagingHosts\{HostName}";

    public static string BuildJson(string exePath) =>
        JsonSerializer.Serialize(new
        {
            name = HostName,
            description = "N_m3u8DL-RE GUI capture bridge",
            path = exePath,
            type = "stdio",
            allowed_origins = new[] { AllowedOrigin }
        }, new JsonSerializerOptions { WriteIndented = true });

    public static string BuildFirefoxJson(string exePath) =>
        JsonSerializer.Serialize(new
        {
            name = HostName,
            description = "N_m3u8DL-RE GUI capture bridge",
            path = exePath,
            type = "stdio",
            allowed_extensions = new[] { FirefoxExtensionId }
        }, new JsonSerializerOptions { WriteIndented = true });

    /// <summary>
    /// Chrome passes the calling extension's origin as the first argument.
    /// Firefox passes the manifest path as the first argument and the extension ID as the second.
    /// </summary>
    public static bool IsNativeHostLaunch(string[] args) =>
        (args.Length > 0 && args[0].StartsWith("chrome-extension://", StringComparison.Ordinal)) ||
        (args.Length >= 2 && string.Equals(args[1], FirefoxExtensionId, StringComparison.Ordinal));
}

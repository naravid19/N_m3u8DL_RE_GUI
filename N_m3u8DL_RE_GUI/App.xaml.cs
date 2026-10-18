#nullable enable
using System;
using System.Diagnostics;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using N_m3u8DL_RE_GUI.Core.NativeBridge;
using N_m3u8DL_RE_GUI.Services;
using N_m3u8DL_RE_GUI.ViewModels;

namespace N_m3u8DL_RE_GUI;

/// <summary>Application entry point and global failure handling.</summary>
public partial class App : System.Windows.Application
{
    /// <summary>Kept alive for the process lifetime; see the comment where it is created in OnStartup.</summary>
    private static Mutex? _singleInstanceMutex;

        protected override void OnStartup(StartupEventArgs e)
        {
            // Chrome launches a native messaging host with the calling
            // extension's origin as the first argument. In that mode there is
            // no user and no window: take the message, queue it, and exit immediately.
            if (NativeHostManifest.IsNativeHostLaunch(e.Args))
            {
                RunNativeHost();
                Shutdown();
                return;
            }

            DispatcherUnhandledException += OnDispatcherUnhandledException;
            AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;
            TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

            // Held for the process lifetime, never waited on or released: its
            // mere existence is what EnsureGuiRunning checks for. Created
            // before anything else in this branch so a host process racing a
            // GUI that is still starting up sees it as early as possible.
            // Guarded like NativeHostRegistrar.Register(): this is a
            // convenience for the browser handoff, and failing it must never
            // stop the app itself from opening.
            try
            {
                _singleInstanceMutex = new Mutex(initiallyOwned: false, NativeHostManifest.SingleInstanceMutexName);
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"Single-instance mutex unavailable: {ex.Message}");
            }

            ViewModelLocator.Initialize();
            NativeHostRegistrar.Register();

            try
            {
                var configService = new JsonConfigService();
                var config = configService.Load("config.txt");
                var initialLang = MainWindowConfigMapper.ResolveGuiLanguage(config);
                LanguageService.Default.SetLanguage(initialLang);
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"Failed to initialize startup language: {ex.Message}");
            }

            base.OnStartup(e);
        }

        /// <summary>
        /// Reads the one message Chrome sends, queues its payload, and makes
        /// sure a GUI exists to drain it. Answers on stdout so the extension
        /// can tell the difference between "handed off" and "no GUI installed".
        /// </summary>
        private static void RunNativeHost()
        {
            using var stdout = Console.OpenStandardOutput();
            try
            {
                using var stdin = Console.OpenStandardInput();

                var request = NativeMessageCodec.ReadMessage(stdin);
                if (request is null)
                    return;

                string? payload = null;
                string? type = null;
                using (var doc = JsonDocument.Parse(request))
                {
                    if (doc.RootElement.TryGetProperty("payload", out var value))
                        payload = value.GetString();
                    if (doc.RootElement.TryGetProperty("type", out var kind))
                        type = kind.GetString();
                }

                // A availability probe from the popup. Answer without queueing
                // anything and without starting the GUI.
                if (type == "ping")
                {
                    NativeMessageCodec.WriteMessage(stdout, "{\"ok\":true}");
                    return;
                }

                if (string.IsNullOrWhiteSpace(payload))
                {
                    NativeMessageCodec.WriteMessage(stdout, "{\"ok\":false,\"error\":\"empty payload\"}");
                    return;
                }

                new CaptureInbox().Write(payload);
                EnsureGuiRunning();

                NativeMessageCodec.WriteMessage(stdout, "{\"ok\":true}");
            }
            catch (Exception ex)
            {
                try
                {
                    var message = JsonSerializer.Serialize(new { ok = false, error = ex.Message });
                    NativeMessageCodec.WriteMessage(stdout, message);
                }
                catch (Exception inner)
                {
                    Trace.WriteLine($"Native host could not report a failure: {inner.Message}");
                }
            }
        }

        /// <summary>
        /// Starts the GUI unless one is already up.
        ///
        /// Checks for the named mutex a real GUI holds for its whole
        /// lifetime, not for another process sharing this executable's
        /// image name — a process-name check cannot tell a running GUI
        /// apart from another native-host process that is transiently
        /// alive under the same name, so two Download clicks close enough
        /// together could each see the other's host process, each
        /// conclude a GUI already exists, and neither would start one.
        /// </summary>
        private static void EnsureGuiRunning()
        {
            var exePath = Environment.ProcessPath;
            if (string.IsNullOrEmpty(exePath))
                return;

            if (Mutex.TryOpenExisting(NativeHostManifest.SingleInstanceMutexName, out var existing))
            {
                existing.Dispose();
                return;
            }

            using var process = Process.Start(new ProcessStartInfo(exePath) { UseShellExecute = true });
        }

        /// <summary>
        /// Catches anything escaping an async void event handler. Without this the
        /// process terminates silently and the user loses their unsaved settings.
        /// </summary>
        private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            Trace.WriteLine($"Unhandled UI exception: {e.Exception}");

            System.Windows.MessageBox.Show(
                $"An unexpected error occurred:\n\n{e.Exception.Message}\n\n" +
                "The application will keep running, but the last action did not complete.",
                "Unexpected Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);

            e.Handled = true;
        }

        private void OnDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            // Cannot be handled — the runtime is already tearing down. Log for a crash dump.
            Trace.WriteLine($"Fatal unhandled exception: {e.ExceptionObject}");
        }

        private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
        {
            Trace.WriteLine($"Unobserved task exception: {e.Exception}");
            e.SetObserved();
        }

        protected override void OnExit(ExitEventArgs e)
        {
            ViewModelLocator.Cleanup();
            base.OnExit(e);
        }
    }

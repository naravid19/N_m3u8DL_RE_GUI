using System;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using N_m3u8DL_RE_GUI.ViewModels;

namespace N_m3u8DL_RE_GUI
{
    /// <summary>Application entry point and global failure handling.</summary>
    public partial class App : System.Windows.Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            DispatcherUnhandledException += OnDispatcherUnhandledException;
            AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;
            TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

            ViewModelLocator.Initialize();

            base.OnStartup(e);
        }

        /// <summary>
        /// Catches anything escaping an async void event handler. Without this the
        /// process terminates silently and the user loses their unsaved settings.
        /// </summary>
        private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            Debug.WriteLine($"Unhandled UI exception: {e.Exception}");

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
            Debug.WriteLine($"Fatal unhandled exception: {e.ExceptionObject}");
        }

        private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
        {
            Debug.WriteLine($"Unobserved task exception: {e.Exception}");
            e.SetObserved();
        }

        protected override void OnExit(ExitEventArgs e)
        {
            ViewModelLocator.Cleanup();
            base.OnExit(e);
        }
    }
}

#nullable enable
using Microsoft.Extensions.DependencyInjection;
using N_m3u8DL_RE_GUI.Services;
using System;

namespace N_m3u8DL_RE_GUI.ViewModels;

/// <summary>
/// Owns the app's dependency-injection container; MainWindow resolves its services here.
/// </summary>
public class ViewModelLocator
{
    private static IServiceProvider? _serviceProvider;

    /// <summary>
    /// Initialize the service provider.
    /// </summary>
    public static void Initialize()
    {
        var services = new ServiceCollection();
        
        // Register Services
        services.AddSingleton<IDownloadService, DownloadService>();
        services.AddSingleton<IUtilityService, UtilityService>();
        services.AddSingleton<IDragDropService, DragDropService>();
        services.AddSingleton<IConfigService, JsonConfigService>();
        services.AddSingleton<IBatchScriptService, BatchScriptService>();
        // The static Default is what every current call site (MainWindow,
        // App.OnStartup) already uses. Registering a fresh LanguageService
        // here instead would give a future DI-injected consumer its own
        // instance -- always "en-US", its LanguageChanged never firing --
        // silently out of sync with what the user actually selected.
        services.AddSingleton<ILanguageService>(_ => LanguageService.Default);

        _serviceProvider = services.BuildServiceProvider();
    }

    /// <summary>
    /// Expose ServiceProvider for code-behind access to services.
    /// </summary>
    public static IServiceProvider ServiceProvider
    {
        get
        {
            if (_serviceProvider == null) Initialize();
            return _serviceProvider!;
        }
    }

    /// <summary>
    /// Cleanup resources.
    /// </summary>
    public static void Cleanup()
    {
        if (_serviceProvider is IDisposable disposable)
        {
            disposable.Dispose();
        }
    }
} 

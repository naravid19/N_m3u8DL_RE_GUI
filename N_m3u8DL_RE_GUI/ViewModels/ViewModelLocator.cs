#nullable enable
using Microsoft.Extensions.DependencyInjection;
using N_m3u8DL_RE_GUI.Services;
using System;

namespace N_m3u8DL_RE_GUI.ViewModels;

/// <summary>
/// ViewModel locator for dependency injection and service management.
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
        services.AddSingleton<IConfigService, ConfigService>();
        services.AddSingleton<IBatchScriptService, BatchScriptService>();
        
        // Register ViewModels
        services.AddTransient<MainViewModel>();
        
        _serviceProvider = services.BuildServiceProvider();
    }

    /// <summary>
    /// Get MainViewModel instance.
    /// </summary>
    public static MainViewModel MainViewModel
    {
        get
        {
            if (_serviceProvider == null)
            {
                Initialize();
            }
            return _serviceProvider!.GetRequiredService<MainViewModel>();
        }
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

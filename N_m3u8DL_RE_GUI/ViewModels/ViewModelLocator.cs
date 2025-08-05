using Microsoft.Extensions.DependencyInjection;
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
        
        // Register ViewModels
        services.AddTransient<MainViewModel>();
        
        // Register Services (future use)
        // services.AddTransient<IDownloadService, DownloadService>();
        // services.AddTransient<ILogService, LogService>();
        
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
#nullable enable
using Microsoft.Extensions.DependencyInjection;
using N_m3u8DL_RE_GUI.Services;
using N_m3u8DL_RE_GUI.ViewModels;
using Xunit;

namespace N_m3u8DL_RE_GUI.Tests.Unit.ViewModels;

public class ViewModelLocatorTests
{
    [Fact]
    public void ServiceProvider_ShouldResolveAllRegisteredServices()
    {
        var provider = ViewModelLocator.ServiceProvider;

        Assert.NotNull(provider);
        Assert.NotNull(provider.GetService<IDownloadService>());
        Assert.NotNull(provider.GetService<IUtilityService>());
        Assert.NotNull(provider.GetService<IDragDropService>());
        Assert.NotNull(provider.GetService<IConfigService>());
        Assert.NotNull(provider.GetService<IBatchScriptService>());
    }

    [Fact]
    public void MainViewModel_ShouldResolveNewTransientInstance()
    {
        var vm1 = ViewModelLocator.MainViewModel;
        var vm2 = ViewModelLocator.MainViewModel;

        Assert.NotNull(vm1);
        Assert.NotNull(vm2);
        Assert.NotSame(vm1, vm2); // Transient lifetime
    }

    [Fact]
    public void Cleanup_ShouldExecuteWithoutThrowing()
    {
        var exception = Record.Exception(() => ViewModelLocator.Cleanup());
        Assert.Null(exception);
    }
}

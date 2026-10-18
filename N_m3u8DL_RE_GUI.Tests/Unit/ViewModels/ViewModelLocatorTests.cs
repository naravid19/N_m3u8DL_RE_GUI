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
    public void ResolvedLanguageServiceIsTheSameInstanceAsTheStaticDefault()
    {
        // A second, un-synced LanguageService would have its own CurrentLanguage
        // (always "en-US") and its own LanguageChanged that never fires when the
        // user actually switches languages -- a future component injecting
        // ILanguageService would silently show stale text with no error and no
        // test failure. Proving reference equality is the only check that rules
        // that out; equal *values* on two different instances would not.
        var resolved = ViewModelLocator.ServiceProvider.GetService<ILanguageService>();

        Assert.Same(LanguageService.Default, resolved);
    }

    [Fact]
    public void Cleanup_ShouldExecuteWithoutThrowing()
    {
        var exception = Record.Exception(() => ViewModelLocator.Cleanup());
        Assert.Null(exception);
    }
}

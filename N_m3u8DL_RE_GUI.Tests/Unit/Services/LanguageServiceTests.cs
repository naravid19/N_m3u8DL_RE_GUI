#nullable enable
using System;
using System.Linq;
using N_m3u8DL_RE_GUI.Services;
using Xunit;

namespace N_m3u8DL_RE_GUI.Tests.Unit.Services;

/// <summary>
/// Unit tests for <see cref="LanguageService"/> verifying language enumeration,
/// switching, event propagation, string lookup, and fallback behavior.
/// </summary>
public class LanguageServiceTests
{
    [Fact]
    public void SupportedLanguages_ShouldContainEnUsZhCnAndZhTw()
    {
        // Arrange
        var service = new LanguageService();

        // Act
        var languages = service.SupportedLanguages;

        // Assert
        Assert.NotNull(languages);
        Assert.Equal(3, languages.Count);

        var en = languages.FirstOrDefault(l => l.Code == "en-US");
        Assert.NotNull(en);
        Assert.Equal("English", en.DisplayName);
        Assert.Equal("English", en.NativeName);

        var zhCn = languages.FirstOrDefault(l => l.Code == "zh-CN");
        Assert.NotNull(zhCn);
        Assert.Equal("Simplified Chinese", zhCn.DisplayName);
        Assert.Equal("简体中文", zhCn.NativeName);

        var zhTw = languages.FirstOrDefault(l => l.Code == "zh-TW");
        Assert.NotNull(zhTw);
        Assert.Equal("Traditional Chinese", zhTw.DisplayName);
        Assert.Equal("繁體中文", zhTw.NativeName);
    }

    [Fact]
    public void CurrentLanguage_Default_ShouldBeEnUs()
    {
        // Arrange
        var service = new LanguageService();

        // Assert
        Assert.Equal("en-US", service.CurrentLanguage);
    }

    [Theory]
    [InlineData("zh-CN")]
    [InlineData("zh-TW")]
    [InlineData("en-US")]
    public void SetLanguage_WithSupportedCulture_ShouldUpdateCurrentLanguageAndRaiseEvent(string targetCulture)
    {
        // Arrange
        var service = new LanguageService();
        var eventFired = false;
        string? eventCulture = null;

        service.LanguageChanged += (s, e) =>
        {
            eventFired = true;
            eventCulture = service.CurrentLanguage;
        };

        // Act
        service.SetLanguage(targetCulture);

        // Assert
        Assert.Equal(targetCulture, service.CurrentLanguage);
        Assert.True(eventFired);
        Assert.Equal(targetCulture, eventCulture);
    }

    [Theory]
    [InlineData("fr-FR")]
    [InlineData("ja-JP")]
    [InlineData("invalid")]
    [InlineData("")]
    public void SetLanguage_WithUnsupportedCulture_ShouldFallbackToEnUs(string invalidCulture)
    {
        // Arrange
        var service = new LanguageService();
        service.SetLanguage("zh-CN"); // switch away first

        // Act
        service.SetLanguage(invalidCulture);

        // Assert
        Assert.Equal("en-US", service.CurrentLanguage);
    }

    [Fact]
    public void GetString_WithExistingKey_ShouldReturnLocalizedValue()
    {
        // Arrange
        var service = new LanguageService();
        service.SetLanguage("en-US");

        // Act
        var title = service.GetString("Str_AppTitle");
        var download = service.GetString("Str_Download");

        // Assert
        Assert.Equal("N_m3u8DL-RE GUI", title);
        Assert.Equal("Download (GO)", download);
    }

    [Fact]
    public void GetString_WhenSwitchedToZhCn_ShouldReturnSimplifiedChineseValue()
    {
        // Arrange
        var service = new LanguageService();
        service.SetLanguage("zh-CN");

        // Act
        var download = service.GetString("Str_Download");
        var stop = service.GetString("Str_Stop");

        // Assert
        Assert.Equal("开始下载 (GO)", download);
        Assert.Equal("停止", stop);
    }

    [Fact]
    public void GetString_WhenSwitchedToZhTw_ShouldReturnTraditionalChineseValue()
    {
        // Arrange
        var service = new LanguageService();
        service.SetLanguage("zh-TW");

        // Act
        var download = service.GetString("Str_Download");
        var stop = service.GetString("Str_Stop");

        // Assert
        Assert.Equal("開始下載 (GO)", download);
        Assert.Equal("停止", stop);
    }

    [Fact]
    public void GetString_WithNonExistentKeyAndFallback_ShouldReturnFallback()
    {
        // Arrange
        var service = new LanguageService();

        // Act
        var result = service.GetString("Str_NonExistentKey", "Custom Fallback");

        // Assert
        Assert.Equal("Custom Fallback", result);
    }

    [Fact]
    public void GetString_WithNonExistentKeyAndNullFallback_ShouldReturnKey()
    {
        // Arrange
        var service = new LanguageService();

        // Act
        var result = service.GetString("Str_NonExistentKey", null);

        // Assert
        Assert.Equal("Str_NonExistentKey", result);
    }

    [Fact]
    public void Default_Singleton_ShouldNotBeNull()
    {
        Assert.NotNull(LanguageService.Default);
        Assert.Equal("en-US", LanguageService.Default.CurrentLanguage);
    }
}

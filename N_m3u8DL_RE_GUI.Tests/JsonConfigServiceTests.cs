using N_m3u8DL_RE_GUI.Services;
using Xunit;

namespace N_m3u8DL_RE_GUI.Tests;

/// <summary>
/// Tests for <see cref="JsonConfigService"/> verifying JSON save/load
/// round-trip and backward compatibility with legacy config.txt.
/// </summary>
public class JsonConfigServiceTests
{
    [Fact]
    public void Save_And_Load_RoundTrip_ShouldPreserveAllValues()
    {
        // Arrange
        var service = new JsonConfigService();
        var tempDir = Path.Combine(Path.GetTempPath(), $"jsonconfig_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        var configPath = Path.Combine(tempDir, "config.txt");

        try
        {
            var state = new AppConfigState();
            state.Set("SavePattern", "<SaveName>_<Resolution>");
            state.Set("LogFilePath", @"C:\Logs\test.log");
            state.Set("MuxAfterDone", "1");
            state.SetEncodedBase64("程序路径", @"C:\Tools\N_m3u8DL-RE.exe");

            // Act
            service.Save(configPath, state);
            var loaded = service.Load(configPath);

            // Assert
            Assert.Equal("<SaveName>_<Resolution>", loaded.Get("SavePattern"));
            Assert.Equal(@"C:\Logs\test.log", loaded.Get("LogFilePath"));
            Assert.Equal("1", loaded.Get("MuxAfterDone"));
            Assert.Equal(@"C:\Tools\N_m3u8DL-RE.exe", loaded.GetDecodedBase64("程序路径"));
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void Load_WithLegacyConfigTxt_ShouldAutoMigrateToJson()
    {
        // Arrange
        var tempDir = Path.Combine(Path.GetTempPath(), $"jsonconfig_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        var legacyPath = Path.Combine(tempDir, "config.txt");
        var jsonPath = Path.Combine(tempDir, "config.json");

        try
        {
            // Write a legacy config.txt
            File.WriteAllText(legacyPath, "SavePattern=<SaveName>;LogFilePath=C:\\Logs\\test.log;MuxAfterDone=1");

            var service = new JsonConfigService();

            // Act
            var loaded = service.Load(legacyPath);

            // Assert — values loaded correctly
            Assert.Equal("<SaveName>", loaded.Get("SavePattern"));
            Assert.Equal(@"C:\Logs\test.log", loaded.Get("LogFilePath"));
            Assert.Equal("1", loaded.Get("MuxAfterDone"));

            // Assert — JSON file was auto-created
            Assert.True(File.Exists(jsonPath), "config.json should have been auto-created");
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void Load_WithEmptyPath_ShouldReturnEmptyState()
    {
        var service = new JsonConfigService();
        var state = service.Load("");
        Assert.Empty(state.Entries);
    }

    [Fact]
    public void Load_WithNonExistentPath_ShouldReturnEmptyState()
    {
        var service = new JsonConfigService();
        var state = service.Load(@"C:\NonExistent\config.txt");
        Assert.Empty(state.Entries);
    }

    [Fact]
    public void Save_CreatesLegacyConfigTxtForBackwardCompat()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"jsonconfig_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        var legacyPath = Path.Combine(tempDir, "config.txt");

        try
        {
            var service = new JsonConfigService();
            var state = new AppConfigState();
            state.Set("TestKey", "TestValue");

            service.Save(legacyPath, state);

            // Both files should exist
            Assert.True(File.Exists(legacyPath), "Legacy config.txt should still be created");
            Assert.True(File.Exists(Path.Combine(tempDir, "config.json")), "config.json should be created");
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
    }
}

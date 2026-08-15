using System;
using System.IO;
using N_m3u8DL_RE_GUI.Services;
using Xunit;

namespace N_m3u8DL_RE_GUI.Tests.Unit.Services;

/// <summary>
/// Tests for <see cref="JsonConfigService"/> verifying JSON save/load
/// round-trip, secret encryption, and backward compatibility with legacy config.txt.
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
    public void Load_WithCorruptedJsonFile_ShouldRecoverGracefully()
    {
        var service = new JsonConfigService();
        var tempDir = Path.Combine(Path.GetTempPath(), $"jsonconfig_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        var jsonPath = Path.Combine(tempDir, "config.json");
        var configPath = Path.Combine(tempDir, "config.txt");

        try
        {
            File.WriteAllText(jsonPath, "{ corrupted_invalid_json_content !! ");

            var state = service.Load(configPath);
            Assert.Empty(state.Entries);
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
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

    [Fact]
    public void Save_ShouldNotStoreSecretsInPlaintext()
    {
        var service = new JsonConfigService();
        var tempDir = Path.Combine(Path.GetTempPath(), $"jsonconfig_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        var configPath = Path.Combine(tempDir, "config.txt");

        try
        {
            var state = new AppConfigState();
            state.Set("CustomHLSKey", "secret-hls-key-12345");
            state.Set("Headers", "Cookie: super-secret-session");

            service.Save(configPath, state);

            var jsonContent = File.ReadAllText(Path.Combine(tempDir, "config.json"));
            Assert.DoesNotContain("secret-hls-key-12345", jsonContent);
            Assert.DoesNotContain("super-secret-session", jsonContent);

            var loaded = service.Load(configPath);
            Assert.Equal("secret-hls-key-12345", loaded.Get("CustomHLSKey"));
            Assert.Equal("Cookie: super-secret-session", loaded.Get("Headers"));
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void Save_WithUnicodeSecrets_ShouldEncryptAndDecryptCorrectly()
    {
        var service = new JsonConfigService();
        var tempDir = Path.Combine(Path.GetTempPath(), $"jsonconfig_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        var configPath = Path.Combine(tempDir, "config.txt");

        try
        {
            var state = new AppConfigState();
            const string unicodeHeader = "Cookie: token=ความลับ123_密码_トークン";
            state.Set("Headers", unicodeHeader);

            service.Save(configPath, state);
            var loaded = service.Load(configPath);

            Assert.Equal(unicodeHeader, loaded.Get("Headers"));
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void Load_WithUndecryptableDpapiBlob_ShouldPreserveTheCiphertext()
    {
        var service = new JsonConfigService();
        var tempDir = Path.Combine(Path.GetTempPath(), $"jsonconfig_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        var jsonPath = Path.Combine(tempDir, "config.json");
        var legacyPath = Path.Combine(tempDir, "config.txt");

        try
        {
            File.WriteAllText(jsonPath, "{\n  \"CustomHLSKey\": \"dpapi:invalid-base64-data\"\n}");

            var loaded = service.Load(legacyPath);

            Assert.Equal("dpapi:invalid-base64-data", loaded.Get("CustomHLSKey"));
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void SaveAfterFailedDecrypt_ShouldNotOverwriteTheCiphertextWithEmpty()
    {
        var service = new JsonConfigService();
        var tempDir = Path.Combine(Path.GetTempPath(), $"jsonconfig_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        var jsonPath = Path.Combine(tempDir, "config.json");
        var legacyPath = Path.Combine(tempDir, "config.txt");

        try
        {
            File.WriteAllText(jsonPath, "{\n  \"CustomHLSKey\": \"dpapi:invalid-base64-data\"\n}");

            // Load-then-save is exactly what Window_Loaded + Window_Closing do.
            var loaded = service.Load(legacyPath);
            service.Save(legacyPath, loaded);

            Assert.Contains("dpapi:invalid-base64-data", File.ReadAllText(jsonPath));
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
    }
}

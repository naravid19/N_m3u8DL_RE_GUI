#nullable enable
using System;
using System.IO;
using N_m3u8DL_RE_GUI.Services;
using Xunit;

namespace N_m3u8DL_RE_GUI.Tests.Unit.Services;

/// <summary>
/// Pins down exactly WHICH config keys <see cref="JsonConfigService"/> treats as secrets.
///
/// This matters because the secret list is keyed by English names ("Headers", "Proxy",
/// "Key") while <c>MainWindowConfigMapper</c> persists the same fields under the legacy
/// Chinese names ("请求头", "代理") and duplicates CustomHLSIv under "IV". Any change to
/// either side must break one of these tests.
/// </summary>
public class JsonConfigServiceSecretCoverageTests
{
    [Theory]
    [InlineData("Headers")]
    [InlineData("请求头")]      // legacy name MainWindowConfigMapper actually writes
    [InlineData("Proxy")]
    [InlineData("代理")]        // legacy name MainWindowConfigMapper actually writes
    [InlineData("CustomHLSKey")]
    [InlineData("CustomHLSIv")]
    [InlineData("IV")]          // legacy duplicate of CustomHLSIv
    [InlineData("Key")]
    public void Save_WithARecognisedSecretKey_ShouldWriteADpapiBlobNotPlaintext(string key)
    {
        WithConfigDir((configPath, dir) =>
        {
            var state = new AppConfigState();
            state.Set(key, "s3cr3t-value-marker");

            new JsonConfigService().Save(configPath, state);

            var json = File.ReadAllText(Path.Combine(dir, "config.json"));
            Assert.DoesNotContain("s3cr3t-value-marker", json);
            Assert.Contains("dpapi:", json);
        });
    }

    [Theory]
    [InlineData("KeyTextFile")]   // a path, not a secret — plaintext is correct
    [InlineData("SavePattern")]
    public void Save_WithANonSecretKey_StoresTheValueInPlaintext(string key)
    {
        WithConfigDir((configPath, dir) =>
        {
            var state = new AppConfigState();
            state.Set(key, "s3cr3t-value-marker");

            new JsonConfigService().Save(configPath, state);

            var json = File.ReadAllText(Path.Combine(dir, "config.json"));
            Assert.Contains("s3cr3t-value-marker", json);
        });
    }

    [Fact]
    public void Save_WithCustomHlsIvDuplicatedUnderIv_ShouldEncryptBothKeys()
    {
        WithConfigDir((configPath, dir) =>
        {
            const string iv = "00112233445566778899aabbccddeeff";
            var state = new AppConfigState();
            state.Set("CustomHLSIv", iv);
            state.Set("IV", iv);

            new JsonConfigService().Save(configPath, state);

            var json = File.ReadAllText(Path.Combine(dir, "config.json"));
            Assert.DoesNotContain($"\"IV\": \"{iv}\"", json);
            Assert.Contains("\"IV\": \"dpapi:", json);
        });
    }

    [Fact]
    public void Save_ShouldStripRecognisedSecretsFromTheLegacyConfigTxt()
    {
        WithConfigDir((configPath, _) =>
        {
            var state = new AppConfigState();
            state.Set("Headers", "Cookie: legacy-secret");
            state.Set("NoLog", "1");

            new JsonConfigService().Save(configPath, state);

            var legacy = File.ReadAllText(configPath);
            Assert.DoesNotContain("legacy-secret", legacy);
            Assert.Contains("NoLog=1", legacy);
        });
    }

    [Fact]
    public void Save_ShouldStripLegacyNamedSecretsFromTheLegacyConfigTxt()
    {
        WithConfigDir((configPath, _) =>
        {
            var state = new AppConfigState();
            state.SetEncodedBase64("请求头", "Cookie: legacy-secret");

            new JsonConfigService().Save(configPath, state);

            Assert.DoesNotContain("请求头=", File.ReadAllText(configPath));
        });
    }

    [Fact]
    public void Save_WithEmptySecretValue_ShouldNotWriteADpapiBlob()
    {
        WithConfigDir((configPath, dir) =>
        {
            var state = new AppConfigState();
            state.Set("Headers", string.Empty);

            new JsonConfigService().Save(configPath, state);

            var json = File.ReadAllText(Path.Combine(dir, "config.json"));
            Assert.Contains("\"Headers\": \"\"", json);
            Assert.DoesNotContain("dpapi:", json);
        });
    }

    [Fact]
    public void Save_WithAnAlreadyProtectedValue_ShouldNotDoubleEncrypt()
    {
        WithConfigDir((configPath, dir) =>
        {
            var service = new JsonConfigService();
            var state = new AppConfigState();
            state.Set("Headers", "Cookie: once");

            service.Save(configPath, state);
            var firstJson = File.ReadAllText(Path.Combine(dir, "config.json"));

            // Re-saving the already-protected blob must be a no-op, not another layer.
            var protectedValue = ExtractJsonValue(firstJson, "Headers");
            var second = new AppConfigState();
            second.Set("Headers", protectedValue);
            service.Save(configPath, second);

            var secondJson = File.ReadAllText(Path.Combine(dir, "config.json"));
            Assert.Equal(protectedValue, ExtractJsonValue(secondJson, "Headers"));
            Assert.Equal("Cookie: once", service.Load(configPath).Get("Headers"));
        });
    }

    [Fact]
    public void Load_ShouldPreferConfigJsonOverLegacyConfigTxt()
    {
        WithConfigDir((configPath, dir) =>
        {
            File.WriteAllText(configPath, "LogLevel=legacy");
            File.WriteAllText(Path.Combine(dir, "config.json"), "{ \"LogLevel\": \"json\" }");

            Assert.Equal("json", new JsonConfigService().Load(configPath).Get("LogLevel"));
        });
    }

    [Fact]
    public void Load_ShouldAcceptCommentsAndTrailingCommas()
    {
        WithConfigDir((configPath, dir) =>
        {
            File.WriteAllText(
                Path.Combine(dir, "config.json"),
                "{\n  // hand-edited\n  \"LogLevel\": \"DEBUG\",\n}");

            Assert.Equal("DEBUG", new JsonConfigService().Load(configPath).Get("LogLevel"));
        });
    }

    [Fact]
    public void Load_WithNonStringJsonValues_ShouldReturnEmptyStateWithoutThrowing()
    {
        WithConfigDir((configPath, dir) =>
        {
            File.WriteAllText(Path.Combine(dir, "config.json"), "{ \"ThreadCount\": 16 }");

            var state = new JsonConfigService().Load(configPath);

            Assert.Empty(state.Entries);
        });
    }

    [Fact]
    public void Load_WithJsonArrayInsteadOfObject_ShouldReturnEmptyStateWithoutThrowing()
    {
        WithConfigDir((configPath, dir) =>
        {
            File.WriteAllText(Path.Combine(dir, "config.json"), "[1, 2, 3]");

            Assert.Empty(new JsonConfigService().Load(configPath).Entries);
        });
    }

    [Fact]
    public void Save_ShouldOverwritePreviousJsonRatherThanMerge()
    {
        WithConfigDir((configPath, dir) =>
        {
            var service = new JsonConfigService();

            var first = new AppConfigState();
            first.Set("A", "1");
            service.Save(configPath, first);

            var second = new AppConfigState();
            second.Set("B", "2");
            service.Save(configPath, second);

            var loaded = service.Load(configPath);
            Assert.Equal("2", loaded.Get("B"));
            Assert.Equal(string.Empty, loaded.Get("A"));
        });
    }

    [Fact]
    public void Save_WithEmptyOrNullPath_ShouldNotThrow()
    {
        var service = new JsonConfigService();
        var state = new AppConfigState();
        state.Set("A", "1");

        Assert.Null(Record.Exception(() => service.Save(string.Empty, state)));
        Assert.Null(Record.Exception(() => service.Save("   ", state)));
        Assert.Null(Record.Exception(() => service.Save(null!, state)));
    }

    [Fact]
    public void Load_WithBareRelativeFileName_ShouldNotThrow()
    {
        // Path.GetDirectoryName("name.txt") is "", which must still combine cleanly.
        // (A relative path resolves against the process CWD — the reason Window_Closing's
        // hard-coded "config.txt" is fragile.)
        var name = $"no_such_config_{Guid.NewGuid():N}.txt";

        Assert.Null(Record.Exception(() => new JsonConfigService().Load(name)));
    }

    // -------------------------------------------------------------------------

    private static string ExtractJsonValue(string json, string key)
    {
        using var document = System.Text.Json.JsonDocument.Parse(json);
        return document.RootElement.GetProperty(key).GetString() ?? string.Empty;
    }

    private static void WithConfigDir(Action<string, string> body)
    {
        var dir = Path.Combine(Path.GetTempPath(), $"jsoncfg_{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            body(Path.Combine(dir, "config.txt"), dir);
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { }
        }
    }
}

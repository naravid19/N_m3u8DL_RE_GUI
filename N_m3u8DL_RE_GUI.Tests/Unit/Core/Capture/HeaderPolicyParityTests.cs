#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using N_m3u8DL_RE_GUI.Core.Capture;
using Xunit;

namespace N_m3u8DL_RE_GUI.Tests.Unit.Core.Capture;

public class HeaderPolicyParityTests
{
    [Fact]
    public void ShouldForward_MatchesSharedFixtureCases()
    {
        var fixturePath = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "extension", "test", "fixtures", "header-policy-cases.json");
        if (!File.Exists(fixturePath))
        {
            // Try relative to workspace
            fixturePath = Path.GetFullPath(Path.Combine(Environment.CurrentDirectory, "extension", "test", "fixtures", "header-policy-cases.json"));
        }

        Assert.True(File.Exists(fixturePath), $"Fixture not found at {fixturePath}");

        var json = File.ReadAllText(fixturePath);
        var cases = JsonSerializer.Deserialize<Dictionary<string, bool>>(json);
        Assert.NotNull(cases);

        foreach (var kvp in cases!)
        {
            var result = HeaderPolicy.ShouldForward(kvp.Key);
            Assert.True(result == kvp.Value, $"HeaderPolicy.ShouldForward failed for '{kvp.Key}'. Expected {kvp.Value}, got {result}");
        }
    }
}

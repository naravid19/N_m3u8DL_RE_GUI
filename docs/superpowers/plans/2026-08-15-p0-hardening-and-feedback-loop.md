# P0 Hardening & In-Window Feedback Loop — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Close every P0 defect found in the 2026-08-14 code audit and UX critique — secret-storage data loss, cancellation-token corruption, unhandled crashes, batch-escaping breakage — and give the GUI a real in-window download feedback loop plus keyboard and screen-reader access.

**Architecture:** Pure logic moves out of the 1248-line `MainWindow.xaml.cs` into `N_m3u8DL_RE_GUI.Core` so it can be tested (`ConsoleOutputParser`, `CfCommandBuilder`). `DownloadService` switches from a detached shell console to a redirected child process and feeds the already-written-but-unbound `MainViewModel.Progress` / `LogOutput`. Accessibility is enforced by a test that parses `MainWindow.xaml` as XML, so the 78 missing labels cannot silently come back.

**Tech Stack:** .NET 9 / WPF, xunit 2.6.6, NSubstitute 6.0.0, CommunityToolkit.Mvvm 8.4.0

**Spec:**
- UX half: `.impeccable/critique/2026-08-14T18-24-43Z__n-m3u8dl-re-gui-mainwindow-xaml.md` (Design Health Score 16/40; the two P0 issues)
- Code half: no standalone doc — the audit findings are restated inline in each task below

---

## Global Constraints

- **Target frameworks are fixed:** `net9.0` for `N_m3u8DL_RE_GUI.Core`, `net9.0-windows` for the GUI and the test project. Do not change them.
- **No new NuGet packages.** Everything in this plan uses the BCL plus the three packages already referenced.
- **All user-visible strings are English.** This is a decided scope item (Task 10). Never add Thai or Chinese literals.
- **The test suite must stay green at every commit.** Baseline is **410 passing** (`dotnet test N_m3u8DL_RE_GUI.sln`). Every task ends with that command passing at a count ≥ the previous task's.
- **`Nullable` is `disable` in all three .csproj files.** Individual files opt in with `#nullable enable` at the top. Follow the file you are editing.
- **Config back-compat is mandatory.** Users have existing `config.txt` and `config.json` files with keys `程序路径`, `保存路径`, `代理`, `请求头`. Never rename a persisted key; only add.
- **Windows-only APIs** (`ProtectedData`, `System.Windows.Forms`, WPF) are expected and fine. Do not add cross-platform guards beyond the `RuntimeInformation.IsOSPlatform` checks that already exist.
- Commit after every task. Branch is `dev`; do not push unless asked.

---

## File Structure

**Created:**

| File | Responsibility |
|---|---|
| `N_m3u8DL_RE_GUI.Core/ConsoleOutputParser.cs` | Split a redirected stdout stream into logical lines (handles `\r` progress redraws), strip ANSI escapes, extract a percentage. Pure functions, no I/O. |
| `N_m3u8DL_RE_GUI.Core/CfCommandBuilder.cs` | Build the `m3u8_cf_bypass.py` command line and the `.bat` wrapper, with correct `%`-doubling. Pure functions, no I/O. |
| `N_m3u8DL_RE_GUI.Tests/Unit/Core/ConsoleOutputParserTests.cs` | Tests for the above. |
| `N_m3u8DL_RE_GUI.Tests/Unit/Core/CfCommandBuilderTests.cs` | Tests for the above. |
| `N_m3u8DL_RE_GUI.Tests/Unit/Xaml/XamlAccessibilityTests.cs` | Parses `MainWindow.xaml` as XML and asserts a11y invariants. Makes Task 8 TDD-able and prevents regression. |
| `N_m3u8DL_RE_GUI.Tests/Fixtures/XamlFixture.cs` | Locates and loads `MainWindow.xaml` for the test above. |

**Modified:**

| File | Change |
|---|---|
| `N_m3u8DL_RE_GUI/Services/JsonConfigService.cs` | Secret key set; decrypt-failure no longer destroys data. |
| `N_m3u8DL_RE_GUI/Services/MainWindowConfigMapper.cs` | Stop writing the plaintext `IV` duplicate. |
| `N_m3u8DL_RE_GUI/Services/DownloadService.cs` | Redirected process, real progress + log callbacks. |
| `N_m3u8DL_RE_GUI/Services/IDownloadService.cs` | Doc comment for the now-live `progressCallback`. |
| `N_m3u8DL_RE_GUI/MainWindow.xaml` | Zone D status strip + log panel; `InputBindings`; `FocusVisualStyle`; `AutomationProperties.Name`. |
| `N_m3u8DL_RE_GUI/MainWindow.xaml.cs` | Per-operation CTS; use `CfCommandBuilder`; wire callbacks; consume the `bool` result. |
| `N_m3u8DL_RE_GUI/App.xaml.cs` | Global exception handlers; drop culture forcing. |
| `N_m3u8DL_RE_GUI/ViewModels/MainViewModel.cs` | Remove Thai strings. |
| `README.md`, `CHANGELOG.md` | Truth corrections. |

---

## Task 1: Fix the DPAPI secret-key mismatch

The audit found that `JsonConfigService.SecretKeys` is keyed on English names (`Headers`, `Proxy`) while `MainWindowConfigMapper.Capture` persists those same fields under the legacy Chinese names (`请求头`, `代理`). The encryption branch is therefore never reached for them, and the values land in `config.json` in plaintext. `Key` is never captured at all.

We add the real key names rather than renaming the persisted keys, because renaming would orphan every existing user config.

**Files:**
- Modify: `N_m3u8DL_RE_GUI/Services/JsonConfigService.cs:30-37`
- Test: `N_m3u8DL_RE_GUI.Tests/Unit/Services/JsonConfigServiceSecretCoverageTests.cs:38-60`

**Interfaces:**
- Consumes: nothing.
- Produces: `JsonConfigService` protects the key set `{Headers, 请求头, Proxy, 代理, CustomHLSKey, CustomHLSIv, IV, Key}`.

- [ ] **Step 1: Flip the existing characterisation test to the desired behaviour**

In `JsonConfigServiceSecretCoverageTests.cs`, the theory `Save_WithAnUnrecognisedKeyName_StoresTheValueInPlaintext` currently asserts the bug. Delete its `请求头`, `代理` and `IV` rows so only `KeyTextFile` remains, and rename it:

```csharp
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
```

Then extend the passing theory `Save_WithARecognisedSecretKey_ShouldWriteADpapiBlobNotPlaintext` with the legacy names:

```csharp
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
```

- [ ] **Step 2: Run the tests to verify they fail**

```bash
dotnet test N_m3u8DL_RE_GUI.sln --filter "FullyQualifiedName~JsonConfigServiceSecretCoverageTests"
```

Expected: FAIL — three rows (`请求头`, `代理`, `IV`) report `Assert.DoesNotContain() Failure` because the marker is present in plaintext.

- [ ] **Step 3: Add the legacy names to the secret set**

In `JsonConfigService.cs`, replace the `SecretKeys` initialiser:

```csharp
    /// <summary>
    /// Config keys whose values are encrypted at rest with Windows DPAPI.
    /// Includes the legacy Chinese key names that MainWindowConfigMapper persists,
    /// and the legacy "IV" duplicate of CustomHLSIv. Renaming persisted keys would
    /// orphan existing user configs, so the set carries both spellings.
    /// </summary>
    private static readonly HashSet<string> SecretKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "Headers",
        "请求头",
        "Proxy",
        "代理",
        "CustomHLSKey",
        "CustomHLSIv",
        "IV",
        "Key"
    };
```

- [ ] **Step 4: Run the tests to verify they pass**

```bash
dotnet test N_m3u8DL_RE_GUI.sln --filter "FullyQualifiedName~JsonConfigServiceSecretCoverageTests"
```

Expected: PASS.

- [ ] **Step 5: Run the full suite**

```bash
dotnet test N_m3u8DL_RE_GUI.sln
```

Expected: PASS, count ≥ 410. `Save_ShouldNotStripUnrecognisedSecretNamesFromTheLegacyConfigTxt` will now FAIL because `请求头` is stripped from `config.txt` — that is the intended new behaviour. Update it:

```csharp
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
```

Re-run until green.

- [ ] **Step 6: Commit**

```bash
git add N_m3u8DL_RE_GUI/Services/JsonConfigService.cs N_m3u8DL_RE_GUI.Tests/Unit/Services/JsonConfigServiceSecretCoverageTests.cs
git commit -m "fix(config): encrypt secrets stored under legacy key names

SecretKeys was keyed on English names while MainWindowConfigMapper
persists 请求头/代理, so proxy and header secrets were written to
config.json in plaintext. Add both spellings plus the legacy IV
duplicate.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

## Task 2: Stop DPAPI decrypt failure from destroying secrets

`UnprotectSecret` returns `string.Empty` when `ProtectedData.Unprotect` throws — which happens whenever the user's DPAPI profile changes (new machine, restored backup, different Windows account). The empty value then flows into the UI and the next `Save` overwrites the ciphertext with nothing. The secret is gone permanently.

Returning the raw `dpapi:` string instead is safe: `ProtectSecret` already no-ops on values that start with `dpapi:`, so the ciphertext round-trips untouched and the user can recover it on the original machine.

**Files:**
- Modify: `N_m3u8DL_RE_GUI/Services/JsonConfigService.cs:182-202`
- Test: `N_m3u8DL_RE_GUI.Tests/Unit/Services/JsonConfigServiceTests.cs:204-226`

**Interfaces:**
- Consumes: `JsonConfigService.SecretKeys` from Task 1.
- Produces: `JsonConfigService.Load` returns the untouched `dpapi:<blob>` string for values it cannot decrypt.

- [ ] **Step 1: Write the failing test**

Replace `Load_WithMalformedDpapiPrefix_ShouldReturnEmptyValue` in `JsonConfigServiceTests.cs` with:

```csharp
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
```

- [ ] **Step 2: Run the tests to verify they fail**

```bash
dotnet test N_m3u8DL_RE_GUI.sln --filter "FullyQualifiedName~JsonConfigServiceTests"
```

Expected: FAIL — `Assert.Equal() Failure: Expected "dpapi:invalid-base64-data", Actual ""`, and the ciphertext is missing from the re-saved file.

- [ ] **Step 3: Preserve the value on failure**

In `JsonConfigService.cs`, change the two failure returns in `UnprotectSecret`:

```csharp
    private static string UnprotectSecret(string value)
    {
        if (string.IsNullOrEmpty(value) || !value.StartsWith("dpapi:", StringComparison.Ordinal))
            return value;

        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return value;

        try
        {
            string base64 = value.Substring(6);
            byte[] protectedBytes = Convert.FromBase64String(base64);
            byte[] plaintextBytes = ProtectedData.Unprotect(protectedBytes, null, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(plaintextBytes);
        }
        catch (Exception ex)
        {
            // Return the ciphertext untouched rather than an empty string. ProtectSecret
            // no-ops on values already prefixed "dpapi:", so the blob survives the next
            // save and stays recoverable on the machine that encrypted it. Returning
            // string.Empty here silently deleted the user's secret on the next save.
            // ponytail: the raw blob is visible in the textbox; a "could not decrypt"
            // placeholder needs UI state this service does not own.
            Debug.WriteLine($"DPAPI unprotect failed, preserving ciphertext: {ex.Message}");
            return value;
        }
    }
```

- [ ] **Step 4: Run the tests to verify they pass**

```bash
dotnet test N_m3u8DL_RE_GUI.sln --filter "FullyQualifiedName~JsonConfigServiceTests"
```

Expected: PASS.

- [ ] **Step 5: Run the full suite**

```bash
dotnet test N_m3u8DL_RE_GUI.sln
```

Expected: PASS, count ≥ 411.

- [ ] **Step 6: Commit**

```bash
git add N_m3u8DL_RE_GUI/Services/JsonConfigService.cs N_m3u8DL_RE_GUI.Tests/Unit/Services/JsonConfigServiceTests.cs
git commit -m "fix(config): preserve ciphertext when DPAPI decryption fails

Returning string.Empty meant the next save overwrote the user's
encrypted secret with nothing, losing it permanently after a machine
or profile change. Return the dpapi: blob unchanged instead.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

## Task 3: Stop persisting the plaintext `IV` duplicate

`MainWindowConfigMapper.Capture` writes `TextBox_IV.Text` to both `CustomHLSIv` and `IV`. Task 1 made both encrypted, so the leak is closed — but writing the same secret twice is still pointless work and doubles the blast radius. `ResolveCustomHlsIv` already falls back to `IV` on read, so dropping the write keeps old configs loading.

**Files:**
- Modify: `N_m3u8DL_RE_GUI/Services/MainWindowConfigMapper.cs:84`
- Test: `N_m3u8DL_RE_GUI.Tests/Unit/Services/MainWindowConfigMapperTests.cs`

**Interfaces:**
- Consumes: `MainWindowConfigMapper.ResolveCustomHlsIv(AppConfigState)` → `string` (already exists).
- Produces: no signature change.

- [ ] **Step 1: Write the failing test**

Append to `MainWindowConfigMapperTests.cs`:

```csharp
    [Fact]
    public void ResolveCustomHlsIv_ShouldStillReadConfigsWrittenBeforeTheIvDuplicateWasDropped()
    {
        // Old configs have only "IV"; new ones have only "CustomHLSIv". Both must load.
        var legacyOnly = new AppConfigState();
        legacyOnly.Set("IV", "00112233445566778899aabbccddeeff");
        Assert.Equal("00112233445566778899aabbccddeeff", MainWindowConfigMapper.ResolveCustomHlsIv(legacyOnly));

        var modernOnly = new AppConfigState();
        modernOnly.Set("CustomHLSIv", "ffeeddccbbaa99887766554433221100");
        Assert.Equal("ffeeddccbbaa99887766554433221100", MainWindowConfigMapper.ResolveCustomHlsIv(modernOnly));
    }
```

- [ ] **Step 2: Run it to confirm it passes already**

```bash
dotnet test N_m3u8DL_RE_GUI.sln --filter "FullyQualifiedName~MainWindowConfigMapperTests"
```

Expected: PASS. This test is a guard for the change, not a driver — it proves the read path survives before you touch the write path.

- [ ] **Step 3: Delete the duplicate write**

In `MainWindowConfigMapper.cs`, delete line 84 entirely:

```csharp
        state.Set("CustomHLSIv", window.TextBox_IV.Text);
        // "IV" is no longer written; ResolveCustomHlsIv still reads it for old configs.
```

- [ ] **Step 4: Run the full suite**

```bash
dotnet test N_m3u8DL_RE_GUI.sln
```

Expected: PASS, count ≥ 412.

- [ ] **Step 5: Commit**

```bash
git add N_m3u8DL_RE_GUI/Services/MainWindowConfigMapper.cs N_m3u8DL_RE_GUI.Tests/Unit/Services/MainWindowConfigMapperTests.cs
git commit -m "refactor(config): stop writing the duplicate IV key

ResolveCustomHlsIv still reads it, so existing configs keep working.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

## Task 4: Extract and fix the Cloudflare command builder

`StartCloudflareDownloadAsync` writes the Python command straight into a `.bat` file without doubling `%`. In a batch file `%20` is consumed as an argument reference, so every percent-encoded URL breaks. `BatchScriptService` gets this right (`.Replace("%", "%%")`); the CF path does not. Extracting the string work to Core makes it testable and kills the duplicate `GetValidFileName` in `MainWindow.xaml.cs:893` at the same time.

**Files:**
- Create: `N_m3u8DL_RE_GUI.Core/CfCommandBuilder.cs`
- Create: `N_m3u8DL_RE_GUI.Tests/Unit/Core/CfCommandBuilderTests.cs`
- Modify: `N_m3u8DL_RE_GUI/MainWindow.xaml.cs:830-898, 1044-1106`

**Interfaces:**
- Consumes: `N_m3u8DL_RE_GUI.Core.InputValidation` (unchanged).
- Produces:
  - `record CfCommandOptions(string PythonExe, string ScriptPath, string Url, string OutputName, string WorkDir, string SegDir, string Referer, string Cookie, string Impersonate, bool KeepSegments)`
  - `static string CfCommandBuilder.BuildCommand(CfCommandOptions options)`
  - `static string CfCommandBuilder.BuildBatchScript(string command)`
  - `static string CfCommandBuilder.DeriveReferer(string? explicitReferer, string? inputUrl)`

- [ ] **Step 1: Write the failing tests**

Create `N_m3u8DL_RE_GUI.Tests/Unit/Core/CfCommandBuilderTests.cs`:

```csharp
#nullable enable
using N_m3u8DL_RE_GUI.Core;
using Xunit;

namespace N_m3u8DL_RE_GUI.Tests.Unit.Core;

public class CfCommandBuilderTests
{
    private static CfCommandOptions Sample(string url = "https://example.com/a.m3u8") => new(
        PythonExe: "python",
        ScriptPath: @"C:\App\m3u8_cf_bypass.py",
        Url: url,
        OutputName: "video.mp4",
        WorkDir: @"C:\Save",
        SegDir: @"C:\App\cf_segments",
        Referer: "https://example.com/",
        Cookie: "",
        Impersonate: "chrome",
        KeepSegments: false);

    [Fact]
    public void BuildCommand_ShouldQuoteEveryPathArgument()
    {
        var cmd = CfCommandBuilder.BuildCommand(Sample());

        Assert.Contains("\"python\"", cmd);
        Assert.Contains("\"C:\\App\\m3u8_cf_bypass.py\"", cmd);
        Assert.Contains("-o \"video.mp4\"", cmd);
        Assert.Contains("--work-dir \"C:\\Save\"", cmd);
        Assert.Contains("--impersonate \"chrome\"", cmd);
    }

    [Fact]
    public void BuildCommand_ShouldOmitCookieWhenEmpty()
    {
        Assert.DoesNotContain("--cookie", CfCommandBuilder.BuildCommand(Sample()));
    }

    [Fact]
    public void BuildCommand_ShouldIncludeCookieWhenPresent()
    {
        var options = Sample() with { Cookie = "cf_clearance=abc" };

        Assert.Contains("--cookie \"cf_clearance=abc\"", CfCommandBuilder.BuildCommand(options));
    }

    [Fact]
    public void BuildCommand_ShouldAppendKeepSegsOnlyWhenRequested()
    {
        Assert.DoesNotContain("--keep-segs", CfCommandBuilder.BuildCommand(Sample()));
        Assert.Contains("--keep-segs", CfCommandBuilder.BuildCommand(Sample() with { KeepSegments = true }));
    }

    [Fact]
    public void BuildCommand_ShouldEscapeEmbeddedDoubleQuotes()
    {
        var options = Sample() with { OutputName = "my \"best\" clip.mp4" };

        Assert.Contains("-o \"my \\\"best\\\" clip.mp4\"", CfCommandBuilder.BuildCommand(options));
    }

    [Fact]
    public void BuildBatchScript_ShouldDoublePercentSigns()
    {
        // THE BUG: a percent-encoded URL is eaten by cmd.exe argument expansion.
        var cmd = CfCommandBuilder.BuildCommand(Sample("https://example.com/a%20b.m3u8"));

        var bat = CfCommandBuilder.BuildBatchScript(cmd);

        Assert.Contains("a%%20b.m3u8", bat);
        Assert.DoesNotContain("a%20b.m3u8", bat.Replace("%%", "\u0000"));
    }

    [Fact]
    public void BuildBatchScript_ShouldEmitUtf8HeaderAndPause()
    {
        var bat = CfCommandBuilder.BuildBatchScript("echo hi");

        Assert.StartsWith("@echo off", bat);
        Assert.Contains("chcp 65001 >nul", bat);
        Assert.Contains("set PYTHONUTF8=1", bat);
        Assert.Contains("pause", bat);
    }

    [Theory]
    [InlineData("https://custom.example/", "https://example.com/a.m3u8", "https://custom.example/")]
    [InlineData("", "https://example.com/path/a.m3u8", "https://example.com/")]
    [InlineData(null, "https://example.com:8443/a.m3u8", "https://example.com:8443/")]
    [InlineData("", "not a url", "")]
    [InlineData("", null, "")]
    public void DeriveReferer_ShouldPreferExplicitThenFallBackToTheUrlAuthority(
        string? explicitReferer, string? inputUrl, string expected)
    {
        Assert.Equal(expected, CfCommandBuilder.DeriveReferer(explicitReferer, inputUrl));
    }
}
```

- [ ] **Step 2: Run them to verify they fail**

```bash
dotnet test N_m3u8DL_RE_GUI.sln --filter "FullyQualifiedName~CfCommandBuilderTests"
```

Expected: FAIL to compile — `CfCommandBuilder` does not exist.

- [ ] **Step 3: Write the implementation**

Create `N_m3u8DL_RE_GUI.Core/CfCommandBuilder.cs`:

```csharp
#nullable enable
using System;
using System.Text;

namespace N_m3u8DL_RE_GUI.Core;

/// <summary>Inputs for one m3u8_cf_bypass.py invocation.</summary>
public sealed record CfCommandOptions(
    string PythonExe,
    string ScriptPath,
    string Url,
    string OutputName,
    string WorkDir,
    string SegDir,
    string Referer,
    string Cookie,
    string Impersonate,
    bool KeepSegments);

/// <summary>
/// Builds the Cloudflare-bypass command line and its .bat wrapper.
/// Pure string work, extracted from MainWindow so it can be tested.
/// </summary>
public static class CfCommandBuilder
{
    public static string BuildCommand(CfCommandOptions o)
    {
        var sb = new StringBuilder();
        sb.Append($"\"{Escape(o.PythonExe)}\"");
        sb.Append($" \"{Escape(o.ScriptPath)}\"");
        sb.Append($" \"{Escape(o.Url)}\"");
        sb.Append($" --referer \"{Escape(o.Referer)}\"");
        sb.Append($" -o \"{Escape(o.OutputName)}\"");
        sb.Append($" --work-dir \"{Escape(o.WorkDir)}\"");
        sb.Append($" --seg-dir \"{Escape(o.SegDir)}\"");
        sb.Append($" --impersonate \"{Escape(o.Impersonate)}\"");

        if (!string.IsNullOrEmpty(o.Cookie))
            sb.Append($" --cookie \"{Escape(o.Cookie)}\"");

        if (o.KeepSegments)
            sb.Append(" --keep-segs");

        return sb.ToString();
    }

    /// <summary>
    /// Wraps a command in a UTF-8 batch script. Percent signs are doubled because
    /// cmd.exe consumes %n as an argument reference — without this, every
    /// percent-encoded URL is corrupted before Python ever sees it.
    /// </summary>
    public static string BuildBatchScript(string command)
    {
        var sb = new StringBuilder();
        sb.AppendLine("@echo off");
        sb.AppendLine("title N_m3u8DL-RE (Cloudflare Bypass Mode)");
        sb.AppendLine("chcp 65001 >nul");
        sb.AppendLine("set PYTHONUTF8=1");
        sb.AppendLine(command.Replace("%", "%%"));
        sb.AppendLine("echo.");
        sb.AppendLine("pause");
        return sb.ToString();
    }

    /// <summary>
    /// Returns the explicit referer when supplied, otherwise the input URL's
    /// scheme+authority with a trailing slash, otherwise empty.
    /// </summary>
    public static string DeriveReferer(string? explicitReferer, string? inputUrl)
    {
        var trimmed = explicitReferer?.Trim();
        if (!string.IsNullOrEmpty(trimmed))
            return trimmed;

        if (string.IsNullOrWhiteSpace(inputUrl))
            return string.Empty;

        return Uri.TryCreate(inputUrl.Trim(), UriKind.Absolute, out var uri)
            ? uri.GetLeftPart(UriPartial.Authority) + "/"
            : string.Empty;
    }

    private static string Escape(string? value) =>
        string.IsNullOrEmpty(value) ? string.Empty : value.Replace("\"", "\\\"");
}
```

- [ ] **Step 4: Run the tests to verify they pass**

```bash
dotnet test N_m3u8DL_RE_GUI.sln --filter "FullyQualifiedName~CfCommandBuilderTests"
```

Expected: PASS, 8 tests.

- [ ] **Step 5: Rewire MainWindow to use it**

In `MainWindow.xaml.cs`, delete `EscapeBatchArg` (line 830), `BuildCfCommand` (line 841) and the private `GetValidFileName` (line 893). Replace `BuildCfCommand` with:

```csharp
        private CfCommandOptions BuildCfOptions(string pythonExe = "python")
        {
            string scriptPath = Path.Combine(AppContext.BaseDirectory, "m3u8_cf_bypass.py");
            if (!File.Exists(scriptPath))
                scriptPath = Path.Combine(Environment.CurrentDirectory, "m3u8_cf_bypass.py");

            var titleClean = _utilityService.GetValidFileName(TextBox_Title.Text);
            if (string.IsNullOrWhiteSpace(titleClean)) titleClean = "output";
            if (!titleClean.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase)) titleClean += ".mp4";

            return new CfCommandOptions(
                PythonExe: pythonExe,
                ScriptPath: scriptPath,
                Url: TextBox_URL.Text,
                OutputName: titleClean,
                WorkDir: string.IsNullOrWhiteSpace(TextBox_WorkDir.Text)
                    ? Environment.CurrentDirectory
                    : TextBox_WorkDir.Text,
                SegDir: Path.Combine(AppContext.BaseDirectory, "cf_segments"),
                Referer: CfCommandBuilder.DeriveReferer(TextBox_CFReferer?.Text, TextBox_URL.Text),
                Cookie: TextBox_CFCookie?.Text?.Trim() ?? string.Empty,
                Impersonate: (Combo_CFImpersonate?.SelectedItem is ComboBoxItem cfi && cfi.Tag is string tag && !string.IsNullOrEmpty(tag))
                    ? tag
                    : "chrome",
                KeepSegments: CheckBox_CFKeepSegs?.IsChecked == true);
        }
```

In `GetParameter()` (line 102) replace `BuildCfCommand()` with `CfCommandBuilder.BuildCommand(BuildCfOptions())`.

In `StartCloudflareDownloadAsync`, replace the `sb` block and `File.WriteAllText` (lines 1087-1101) with:

```csharp
            string cfCmd = CfCommandBuilder.BuildCommand(BuildCfOptions(pythonExe));
            TextBox_Parameter.Text = cfCmd;

            string bat = Path.Combine(Path.GetTempPath(), "cf_dl_" + DateTime.Now.ToString("yyyyMMddHHmmss") + ".bat");
            File.WriteAllText(bat, CfCommandBuilder.BuildBatchScript(cfCmd), new UTF8Encoding(false));
```

Add `using N_m3u8DL_RE_GUI.Core;` if the compiler asks (it is already present at line 24).

- [ ] **Step 6: Build and run the full suite**

```bash
dotnet build N_m3u8DL_RE_GUI.sln -c Debug
dotnet test N_m3u8DL_RE_GUI.sln
```

Expected: 0 errors; PASS, count ≥ 420.

- [ ] **Step 7: Commit**

```bash
git add N_m3u8DL_RE_GUI.Core/CfCommandBuilder.cs N_m3u8DL_RE_GUI.Tests/Unit/Core/CfCommandBuilderTests.cs N_m3u8DL_RE_GUI/MainWindow.xaml.cs
git commit -m "fix(cf): double percent signs in the generated bypass batch file

Percent-encoded URLs were corrupted by cmd.exe argument expansion
before Python saw them. Extract the command/batch construction to
Core.CfCommandBuilder so it is testable, and drop the duplicate
GetValidFileName that skipped reserved-device-name handling.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

## Task 5: Give every async operation its own CancellationTokenSource

`_titleLookupCts` and `_cfPrepCts` are shared fields mutated by four `async void` entry points. Concrete failure: double-click Title starts a lookup holding CTS-A; the user presses Enter; `Button_GO_Click`'s batch path disposes CTS-A and installs CTS-B; the in-flight lookup's token is now disposed (`ObjectDisposedException`); then the lookup's `finally` disposes CTS-B — the one the batch build is using — and nulls the field, so `Button_Stop` loses its handle.

The fix is to stop sharing: each operation owns a local CTS, and a single field holds only the *currently cancellable* one, swapped atomically.

**Files:**
- Modify: `N_m3u8DL_RE_GUI/MainWindow.xaml.cs:61-64, 422-454, 610-763, 765-771, 1044-1106`

**Interfaces:**
- Consumes: nothing new.
- Produces: `private CancellationTokenSource? _activeOperationCts` replaces both fields.

- [ ] **Step 1: Replace the two fields with one**

```csharp
        // One token source for whatever long-running operation is currently cancellable.
        // Each operation creates its own, publishes it here for Button_Stop, and clears
        // the field only if it is still the owner. Sharing a single field across the
        // async void handlers previously let one flow dispose another's live token.
        private System.Threading.CancellationTokenSource? _activeOperationCts;
```

Delete the `_cfPrepCts` and `_titleLookupCts` declarations.

- [ ] **Step 2: Add the ownership helpers**

```csharp
        /// <summary>
        /// Creates a token source for a new cancellable operation and publishes it so
        /// Button_Stop can reach it. Cancels any operation already in flight.
        /// </summary>
        private System.Threading.CancellationTokenSource BeginCancellableOperation()
        {
            var previous = _activeOperationCts;
            var cts = new System.Threading.CancellationTokenSource();
            _activeOperationCts = cts;

            if (previous != null)
            {
                try { previous.Cancel(); } catch (ObjectDisposedException) { }
                try { previous.Dispose(); } catch (ObjectDisposedException) { }
            }

            return cts;
        }

        /// <summary>Retires a token source, clearing the shared field only if we still own it.</summary>
        private void EndCancellableOperation(System.Threading.CancellationTokenSource cts)
        {
            if (ReferenceEquals(_activeOperationCts, cts))
                _activeOperationCts = null;

            try { cts.Dispose(); } catch (ObjectDisposedException) { }
        }
```

- [ ] **Step 3: Convert `PopulateTitleForInputAsync`**

```csharp
            if (InputValidation.IsHttpUrl(input))
            {
                var cts = BeginCancellableOperation();
                try
                {
                    TextBox_Title.Text = await _utilityService.GetTitleFromUrlAsync(input, cts.Token);
                }
                catch (OperationCanceledException)
                {
                }
                finally
                {
                    EndCancellableOperation(cts);
                }
                return;
            }
```

- [ ] **Step 4: Convert the batch path in `Button_GO_Click`**

Replace the `_titleLookupCts?.Dispose(); _titleLookupCts = new ...` prologue and its `finally` with:

```csharp
                var cts = BeginCancellableOperation();
                try
                {
                    var token = cts.Token;
                    result = await _batchScriptService.BuildScriptAsync(
                        inputPath: TextBox_URL.Text,
                        exePath: TextBox_EXE.Text,
                        resolveTitleAsync: url => _utilityService.GetTitleFromUrlAsync(url, token),
                        buildArgsForInput: BuildArgsRE,
                        onTitleResolved: title => TextBox_Title.Text = title,
                        cancellationToken: token);

                    _batchScriptService.SaveScript(result.FilePath, result.Content);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch (Exception ex)
                {
                    MessageBox.Show(ex.Message, "Batch build failed", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }
                finally
                {
                    EndCancellableOperation(cts);
                    Button_GO.Content = "▶ Download";   // was "GO" — see Task 9
                    this.IsEnabled = true;
                }
```

- [ ] **Step 5: Convert `StartCloudflareDownloadAsync`**

```csharp
            var cts = BeginCancellableOperation();
            string? pythonExe = null;
            try
            {
                pythonExe = await FindPythonWithCurlCffiAsync(cts.Token);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            finally
            {
                EndCancellableOperation(cts);
            }
```

- [ ] **Step 6: Make `Button_Stop_Click` unable to throw**

```csharp
        private void Button_Stop_Click(object sender, RoutedEventArgs e)
        {
            var cts = _activeOperationCts;
            if (cts != null)
            {
                try { cts.Cancel(); } catch (ObjectDisposedException) { }
            }

            _downloadService.StopDownload();
            Button_Stop.Visibility = Visibility.Collapsed;
        }
```

- [ ] **Step 7: Build and verify manually**

```bash
dotnet build N_m3u8DL_RE_GUI.sln -c Debug
dotnet test N_m3u8DL_RE_GUI.sln
```

Expected: 0 errors; PASS, count unchanged.

Manual check (WPF, no automated coverage available): launch the app, paste an `https://` URL, double-click the Save Name field to start a title lookup, and immediately press Enter in the URL field. Expected: no crash, no unhandled-exception dialog. Before this task, that sequence could throw `ObjectDisposedException`.

- [ ] **Step 8: Commit**

```bash
git add N_m3u8DL_RE_GUI/MainWindow.xaml.cs
git commit -m "fix(ui): give each async operation its own CancellationTokenSource

Two shared CTS fields were mutated by four async void handlers, so one
flow could dispose a token another flow was still using and then null
the field Button_Stop depends on.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

## Task 6: Add global exception handling and drop culture forcing

`App.xaml.cs` installs no `DispatcherUnhandledException` handler, so any exception escaping an `async void` handler kills the app with no message. The same file forces `CurrentUICulture` from a hard-coded map — which Task 10 makes pointless, since the UI ships English-only.

**Files:**
- Modify: `N_m3u8DL_RE_GUI/App.xaml.cs`

**Interfaces:**
- Consumes: `ViewModelLocator.Initialize()`, `ViewModelLocator.Cleanup()` (unchanged).
- Produces: no new public API.

- [ ] **Step 1: Replace the file contents**

```csharp
using System;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using N_m3u8DL_RE_GUI.ViewModels;

namespace N_m3u8DL_RE_GUI
{
    /// <summary>Application entry point and global failure handling.</summary>
    public partial class App : System.Windows.Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            DispatcherUnhandledException += OnDispatcherUnhandledException;
            AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;
            TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

            ViewModelLocator.Initialize();

            base.OnStartup(e);
        }

        /// <summary>
        /// Catches anything escaping an async void event handler. Without this the
        /// process terminates silently and the user loses their unsaved settings.
        /// </summary>
        private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            Debug.WriteLine($"Unhandled UI exception: {e.Exception}");

            MessageBox.Show(
                $"An unexpected error occurred:\n\n{e.Exception.Message}\n\n" +
                "The application will keep running, but the last action did not complete.",
                "Unexpected Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);

            e.Handled = true;
        }

        private void OnDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            // Cannot be handled — the runtime is already tearing down. Log for a crash dump.
            Debug.WriteLine($"Fatal unhandled exception: {e.ExceptionObject}");
        }

        private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
        {
            Debug.WriteLine($"Unobserved task exception: {e.Exception}");
            e.SetObserved();
        }

        protected override void OnExit(ExitEventArgs e)
        {
            ViewModelLocator.Cleanup();
            base.OnExit(e);
        }
    }
}
```

- [ ] **Step 2: Build and run the suite**

```bash
dotnet build N_m3u8DL_RE_GUI.sln -c Debug
dotnet test N_m3u8DL_RE_GUI.sln
```

Expected: 0 errors; PASS, count unchanged.

- [ ] **Step 3: Commit**

```bash
git add N_m3u8DL_RE_GUI/App.xaml.cs
git commit -m "fix(app): handle unhandled exceptions instead of dying silently

async void handlers threw straight into the dispatcher and killed the
process with no message. Also drop the culture forcing, which is dead
once the UI is English-only.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

## Task 7: Build the console output parser

`DownloadService` will redirect N_m3u8DL-RE's stdout. That stream is not line-oriented: progress redraws terminate with `\r`, not `\n`, and even with `--no-ansi-color` some escape sequences survive. `StreamReader.ReadLineAsync` treats a lone `\r` as a terminator on .NET, but it will not strip escapes, and we need a percentage out of each line. All of that is pure logic, so it belongs in Core with tests.

**Files:**
- Create: `N_m3u8DL_RE_GUI.Core/ConsoleOutputParser.cs`
- Create: `N_m3u8DL_RE_GUI.Tests/Unit/Core/ConsoleOutputParserTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces:
  - `static string ConsoleOutputParser.StripAnsi(string line)`
  - `static int? ConsoleOutputParser.TryExtractPercent(string line)`
  - `static string ConsoleOutputParser.Clean(string rawLine)` — `StripAnsi` + trim, returns `string.Empty` for lines with no content

- [ ] **Step 1: Write the failing tests**

Create `N_m3u8DL_RE_GUI.Tests/Unit/Core/ConsoleOutputParserTests.cs`:

```csharp
#nullable enable
using N_m3u8DL_RE_GUI.Core;
using Xunit;

namespace N_m3u8DL_RE_GUI.Tests.Unit.Core;

public class ConsoleOutputParserTests
{
    [Theory]
    [InlineData("plain text", "plain text")]
    [InlineData("\u001b[32mgreen\u001b[0m", "green")]
    [InlineData("\u001b[1;33mbold yellow\u001b[0m tail", "bold yellow tail")]
    [InlineData("\u001b[2K\u001b[1Gredrawn", "redrawn")]
    [InlineData("no escapes at all", "no escapes at all")]
    [InlineData("", "")]
    public void StripAnsi_ShouldRemoveEscapeSequencesOnly(string input, string expected)
    {
        Assert.Equal(expected, ConsoleOutputParser.StripAnsi(input));
    }

    [Theory]
    [InlineData("Downloading... 45%", 45)]
    [InlineData("Vid 1080p | 45.7% | 3.2MBps", 45)]
    [InlineData("100%", 100)]
    [InlineData("0%", 0)]
    [InlineData("first 10% then 80%", 80)]      // last match wins — it is the freshest
    [InlineData("no percent here", null)]
    [InlineData("", null)]
    [InlineData("999%", null)]                   // out of range, ignore
    [InlineData("file_100%_name.ts", 100)]
    public void TryExtractPercent_ShouldReturnTheLastValidPercentage(string line, int? expected)
    {
        Assert.Equal(expected, ConsoleOutputParser.TryExtractPercent(line));
    }

    [Fact]
    public void TryExtractPercent_ShouldIgnoreAnsiNoise()
    {
        Assert.Equal(72, ConsoleOutputParser.TryExtractPercent("\u001b[32m72%\u001b[0m done"));
    }

    [Theory]
    [InlineData("  \u001b[32mhello\u001b[0m  ", "hello")]
    [InlineData("\u001b[2K", "")]
    [InlineData("   ", "")]
    [InlineData("\r\n", "")]
    public void Clean_ShouldStripEscapesAndTrim(string input, string expected)
    {
        Assert.Equal(expected, ConsoleOutputParser.Clean(input));
    }

    [Fact]
    public void Clean_ShouldPreserveInternalSpacingAndUnicode()
    {
        Assert.Equal("ตอนที่ 1 中文 — dash", ConsoleOutputParser.Clean("  ตอนที่ 1 中文 — dash  "));
    }
}
```

- [ ] **Step 2: Run them to verify they fail**

```bash
dotnet test N_m3u8DL_RE_GUI.sln --filter "FullyQualifiedName~ConsoleOutputParserTests"
```

Expected: FAIL to compile — `ConsoleOutputParser` does not exist.

- [ ] **Step 3: Write the implementation**

Create `N_m3u8DL_RE_GUI.Core/ConsoleOutputParser.cs`:

```csharp
#nullable enable
using System.Text.RegularExpressions;

namespace N_m3u8DL_RE_GUI.Core;

/// <summary>
/// Turns raw redirected console output from N_m3u8DL-RE into text fit for the GUI log
/// and a progress percentage. Pure functions — no streams, no state.
/// </summary>
public static class ConsoleOutputParser
{
    // CSI sequences: ESC [ <params> <final byte>. Covers colour, erase-line and cursor moves.
    private static readonly Regex AnsiPattern = new(
        @"\u001b\[[0-9;?]*[A-Za-z]",
        RegexOptions.Compiled);

    // Last percentage on the line is the freshest one on a redrawn progress row.
    private static readonly Regex PercentPattern = new(
        @"(\d{1,3})(?:\.\d+)?%",
        RegexOptions.Compiled | RegexOptions.RightToLeft);

    public static string StripAnsi(string line) =>
        string.IsNullOrEmpty(line) ? string.Empty : AnsiPattern.Replace(line, string.Empty);

    /// <summary>Returns 0-100, or null when the line carries no usable percentage.</summary>
    public static int? TryExtractPercent(string line)
    {
        if (string.IsNullOrEmpty(line))
            return null;

        var match = PercentPattern.Match(StripAnsi(line));
        if (!match.Success)
            return null;

        return int.TryParse(match.Groups[1].Value, out var percent) && percent >= 0 && percent <= 100
            ? percent
            : null;
    }

    /// <summary>Strips escapes and surrounding whitespace; empty when nothing remains.</summary>
    public static string Clean(string rawLine) => StripAnsi(rawLine ?? string.Empty).Trim();
}
```

- [ ] **Step 4: Run the tests to verify they pass**

```bash
dotnet test N_m3u8DL_RE_GUI.sln --filter "FullyQualifiedName~ConsoleOutputParserTests"
```

Expected: PASS, 22 tests.

Note on `"999%"`: `RightToLeft` matching with `\d{1,3}` finds `999`, which fails the 0-100 range check and returns null. Confirm that row passes; if the regex instead matched `99`, tighten to `(?<!\d)(\d{1,3})(?:\.\d+)?%`.

- [ ] **Step 5: Run the full suite and commit**

```bash
dotnet test N_m3u8DL_RE_GUI.sln
git add N_m3u8DL_RE_GUI.Core/ConsoleOutputParser.cs N_m3u8DL_RE_GUI.Tests/Unit/Core/ConsoleOutputParserTests.cs
git commit -m "feat(core): add ConsoleOutputParser for redirected downloader output

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

## Task 8: Make DownloadService report progress and log lines

`StartDownloadAsync` accepts `IProgress<int>? progressCallback` and never uses it, and `UseShellExecute = true` makes redirection impossible — so the GUI can learn nothing about the run. Switching to a redirected child process is what makes Task 9's UI possible.

**Trade-off, decided:** N_m3u8DL-RE renders its own Spectre.Console progress UI in the detached window. Redirecting removes that window. The GUI replaces it with its own progress bar and log, which is the point of the change. `--no-ansi-color` is forced on the GUI-run path so captured output is parseable; `CheckBox_NoAnsiColor` becomes vestigial and is flagged for removal in a later pass.

**Files:**
- Modify: `N_m3u8DL_RE_GUI/Services/DownloadService.cs:44-201`
- Modify: `N_m3u8DL_RE_GUI/Services/IDownloadService.cs:15`
- Test: `N_m3u8DL_RE_GUI.Tests/Unit/Services/DownloadServiceTests.cs`

**Interfaces:**
- Consumes: `ConsoleOutputParser.Clean(string)`, `ConsoleOutputParser.TryExtractPercent(string)` from Task 7.
- Produces: `StartDownloadAsync` and `StartProcessAsync` keep their signatures; `progressCallback` and `logCallback` now receive real data. New private overload `StartTrackedProcessAsync(ProcessStartInfo, Action<string>?, IProgress<int>?, bool redirect, CancellationToken)`.

- [ ] **Step 1: Write the failing tests**

Append to `DownloadServiceTests.cs`:

```csharp
    [Fact]
    public async Task StartProcessAsync_ShouldForwardChildStdoutToTheLogCallback()
    {
        var service = new DownloadService();
        var lines = new List<string>();

        var ok = await service.StartProcessAsync(
            "cmd.exe",
            "/c echo hello-from-child",
            message => { lock (lines) lines.Add(message); });

        Assert.True(ok);
        Assert.Contains(lines, l => l.Contains("hello-from-child"));
    }

    [Fact]
    public async Task StartProcessAsync_ShouldReportPercentagesToTheProgressCallback()
    {
        var service = new DownloadService();
        var reported = new List<int>();
        var progress = new Progress<int>(p => { lock (reported) reported.Add(p); });

        await service.StartProcessAsync(
            "cmd.exe",
            "/c echo working 40% && echo working 90%",
            logCallback: null,
            progressCallback: progress);

        // Progress<T> marshals asynchronously; give the callbacks a moment to land.
        await Task.Delay(300);
        lock (reported)
        {
            Assert.Contains(40, reported);
            Assert.Contains(90, reported);
        }
    }

    [Fact]
    public async Task StartProcessAsync_ShouldReportNonZeroExitCodeInTheLog()
    {
        var service = new DownloadService();
        var lines = new List<string>();

        var ok = await service.StartProcessAsync(
            "cmd.exe",
            "/c exit 3",
            message => { lock (lines) lines.Add(message); });

        Assert.False(ok);
        Assert.Contains(lines, l => l.Contains("3"));
    }
```

This requires a `progressCallback` parameter on `StartProcessAsync`. Add it to the interface in the next step.

- [ ] **Step 2: Run them to verify they fail**

```bash
dotnet test N_m3u8DL_RE_GUI.sln --filter "FullyQualifiedName~DownloadServiceTests"
```

Expected: FAIL to compile — `StartProcessAsync` has no `progressCallback` parameter.

- [ ] **Step 3: Extend the interface**

In `IDownloadService.cs`, update the `progressCallback` doc comment and add the parameter to `StartProcessAsync`:

```csharp
    /// <param name="progressCallback">Receives 0-100 as parsed from the child process output.</param>
    Task<bool> StartDownloadAsync(
        DownloadOptions options,
        IProgress<int>? progressCallback = null,
        Action<string>? logCallback = null,
        CancellationToken cancellationToken = default);

    Task<bool> StartProcessAsync(
        string fileName,
        string arguments,
        Action<string>? logCallback = null,
        IProgress<int>? progressCallback = null,
        CancellationToken cancellationToken = default);
```

- [ ] **Step 4: Rewrite the process plumbing**

In `DownloadService.cs`, replace `StartDownloadAsync`, `StartProcessAsync` and `StartTrackedProcessAsync`:

```csharp
    public Task<bool> StartDownloadAsync(
        DownloadOptions options,
        IProgress<int>? progressCallback = null,
        Action<string>? logCallback = null,
        CancellationToken cancellationToken = default)
    {
        if (IsDownloading)
        {
            logCallback?.Invoke("Download is already in progress. Please wait for it to complete.");
            return Task.FromResult(false);
        }

        if (string.IsNullOrWhiteSpace(options.Input))
        {
            logCallback?.Invoke("Please enter a URL to download.");
            return Task.FromResult(false);
        }

        var exePath = string.IsNullOrWhiteSpace(options.ExePath) ? "N_m3u8DL-RE.exe" : options.ExePath;
        if (!System.IO.File.Exists(exePath))
        {
            logCallback?.Invoke($"File not found: {exePath}");
            logCallback?.Invoke("Please download N_m3u8DL-RE.exe from: https://github.com/nilaoda/N_m3u8DL-RE/releases");
            return Task.FromResult(false);
        }

        logCallback?.Invoke("Starting download...");
        var args = ArgsBuilder.Build(options);
        logCallback?.Invoke($"Command: {exePath} {args}");

        var startInfo = new ProcessStartInfo
        {
            FileName = exePath,
            Arguments = args,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = System.Text.Encoding.UTF8,
            StandardErrorEncoding = System.Text.Encoding.UTF8
        };

        return StartTrackedProcessAsync(startInfo, logCallback, progressCallback, redirect: true, cancellationToken);
    }

    public Task<bool> StartProcessAsync(
        string fileName,
        string arguments,
        Action<string>? logCallback = null,
        IProgress<int>? progressCallback = null,
        CancellationToken cancellationToken = default)
    {
        if (IsDownloading)
        {
            logCallback?.Invoke("A process is already in progress. Please wait for it to complete.");
            return Task.FromResult(false);
        }

        if (string.IsNullOrWhiteSpace(fileName))
        {
            logCallback?.Invoke("Process target file path is required.");
            return Task.FromResult(false);
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = System.Text.Encoding.UTF8,
            StandardErrorEncoding = System.Text.Encoding.UTF8
        };

        return StartTrackedProcessAsync(startInfo, logCallback, progressCallback, redirect: true, cancellationToken);
    }

    private async Task<bool> StartTrackedProcessAsync(
        ProcessStartInfo startInfo,
        Action<string>? logCallback,
        IProgress<int>? progressCallback,
        bool redirect,
        CancellationToken cancellationToken)
    {
        Process? process = null;
        CancellationTokenSource? cts = null;

        lock (_lockObject)
        {
            if (SafeIsRunning(_currentProcess))
            {
                logCallback?.Invoke("A process is already in progress. Please wait for it to complete.");
                return false;
            }

            cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _cancellationTokenSource = cts;

            process = new Process { StartInfo = startInfo };
            _currentProcess = process;
        }

        try
        {
            if (redirect)
            {
                process.OutputDataReceived += (_, e) => Forward(e.Data, logCallback, progressCallback);
                process.ErrorDataReceived += (_, e) => Forward(e.Data, logCallback, progressCallback);
            }

            if (!process.Start())
            {
                logCallback?.Invoke($"Failed to start process: {startInfo.FileName}");
                return false;
            }

            if (redirect)
            {
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
            }

            try
            {
                await process.WaitForExitAsync(cts.Token);
            }
            catch (OperationCanceledException)
            {
                logCallback?.Invoke("Process execution was cancelled.");
                return false;
            }

            var success = process.ExitCode == 0;
            logCallback?.Invoke(success
                ? "Process finished successfully!"
                : $"Process exited with code: {process.ExitCode}");

            if (success)
                progressCallback?.Report(100);

            return success;
        }
        catch (OperationCanceledException)
        {
            logCallback?.Invoke("Process execution was cancelled.");
            return false;
        }
        catch (Exception ex)
        {
            logCallback?.Invoke($"Process execution error: {ex.Message}");
            return false;
        }
        finally
        {
            lock (_lockObject)
            {
                if (_currentProcess == process) _currentProcess = null;
                if (_cancellationTokenSource == cts) _cancellationTokenSource = null;
            }

            if (process != null)
            {
                try
                {
                    if (!process.HasExited)
                        process.Kill(entireProcessTree: true);
                }
                catch { }
                try { process.Dispose(); } catch { }
            }

            try { cts?.Dispose(); } catch { }
        }
    }

    private static void Forward(string? raw, Action<string>? logCallback, IProgress<int>? progressCallback)
    {
        if (raw == null) return;   // null marks end of stream

        var percent = ConsoleOutputParser.TryExtractPercent(raw);
        if (percent.HasValue)
            progressCallback?.Report(percent.Value);

        var cleaned = ConsoleOutputParser.Clean(raw);
        if (cleaned.Length > 0)
            logCallback?.Invoke(cleaned);
    }
```

- [ ] **Step 5: Force `--no-ansi-color` on the GUI-run path**

In `MainWindow.xaml.cs`, in **both** `BuildArgsRE` and `BuildDownloadOptions`, change the `NoAnsiColor` assignment so the preview matches what actually runs:

```csharp
                // Forced on: the GUI parses redirected output, and escape sequences make
                // it unreadable. ponytail: CheckBox_NoAnsiColor is now vestigial — remove
                // it from the XAML in the IA pass.
                NoAnsiColor = true,
```

- [ ] **Step 6: Run the tests to verify they pass**

```bash
dotnet test N_m3u8DL_RE_GUI.sln --filter "FullyQualifiedName~DownloadServiceTests"
```

Expected: PASS. Existing callers in `MainWindow.xaml.cs` still compile because the new parameter is optional and positional order is preserved.

- [ ] **Step 7: Run the full suite and commit**

```bash
dotnet test N_m3u8DL_RE_GUI.sln
git add N_m3u8DL_RE_GUI/Services/DownloadService.cs N_m3u8DL_RE_GUI/Services/IDownloadService.cs N_m3u8DL_RE_GUI/MainWindow.xaml.cs N_m3u8DL_RE_GUI.Tests/Unit/Services/DownloadServiceTests.cs
git commit -m "feat(download): redirect child output and report progress

progressCallback was declared and never used, and UseShellExecute=true
made redirection impossible, so the GUI could learn nothing about a
running download. Redirect stdout/stderr, parse percentages, and force
--no-ansi-color so captured output is readable.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

## Task 9: Build the in-window feedback surface

Zone D gains a status strip above the existing command bar: a progress bar, a status line, an Open Folder button, and a toggleable log panel. The window's default height drops from 680 to 660 and is clamped to the work area, so the new strip does not push Zone D behind the taskbar.

**Design decision, made rather than asked:** the "Command:" bar stays. It is one of the app's genuine strengths (a live, always-correct mirror of what will run) and removing it to make room would trade a working feature for a new one. The new strip sits above it; the log panel is collapsed by default so idle height grows by ~34px, offset by the 20px height reduction.

**Files:**
- Modify: `N_m3u8DL_RE_GUI/MainWindow.xaml:9-11, 1040-1061`
- Modify: `N_m3u8DL_RE_GUI/MainWindow.xaml.cs:610-763`

**Interfaces:**
- Consumes: `IDownloadService.StartDownloadAsync(DownloadOptions, IProgress<int>?, Action<string>?, CancellationToken)` from Task 8.
- Produces: named controls `ProgressBar_Download`, `TextBlock_Status`, `Button_OpenFolder`, `ToggleButton_Log`, `TextBox_Log`; methods `SetStatus(string, bool)`, `AppendLog(string)`, `ResetProgress()`.

- [ ] **Step 1: Reduce the default height and clamp to the work area**

In `MainWindow.xaml` line 10, change `Height="680"` to `Height="660"`.

In `MainWindow.xaml.cs`, inside `Window_Loaded`'s `finally` block, before `GetParameter();`:

```csharp
                ClampToWorkArea();
```

And add the method:

```csharp
        /// <summary>
        /// Keeps the window inside the desktop work area. At 150% scaling on a 1080p
        /// display the default height would otherwise put Zone D behind the taskbar.
        /// </summary>
        private void ClampToWorkArea()
        {
            var work = SystemParameters.WorkArea;
            if (ActualHeight > work.Height)
                Height = work.Height;
            if (ActualWidth > work.Width)
                Width = work.Width;
            if (Top + Height > work.Bottom)
                Top = Math.Max(work.Top, work.Bottom - Height);
        }
```

- [ ] **Step 2: Replace Zone D in the XAML**

Replace the whole `<!-- ZONE D -->` `Border` block (lines 1040-1061) with:

```xml
        <!-- ================== ZONE D: STATUS + LOG + COMMAND BAR ================== -->
        <Border Grid.Row="2" Background="{StaticResource SurfaceBrush}"
                BorderBrush="{StaticResource BorderBrushCustom}" BorderThickness="1"
                Padding="8" Margin="0,10,0,0">
            <StackPanel>
                <!-- Status strip -->
                <Grid Margin="0,0,0,6">
                    <Grid.ColumnDefinitions>
                        <ColumnDefinition Width="140"/>
                        <ColumnDefinition Width="*"/>
                        <ColumnDefinition Width="Auto"/>
                        <ColumnDefinition Width="Auto"/>
                    </Grid.ColumnDefinitions>

                    <ProgressBar x:Name="ProgressBar_Download" Grid.Column="0"
                                 Height="8" Minimum="0" Maximum="100" Value="0"
                                 Background="{StaticResource CardBrush}"
                                 Foreground="{StaticResource AccentBrush}"
                                 BorderThickness="0" VerticalAlignment="Center"
                                 AutomationProperties.Name="Download Progress"/>

                    <TextBlock x:Name="TextBlock_Status" Grid.Column="1" Text="Ready"
                               Style="{StaticResource LabelStyle}" Margin="10,0,0,0"
                               TextTrimming="CharacterEllipsis"
                               AutomationProperties.Name="Download Status"
                               AutomationProperties.LiveSetting="Polite"/>

                    <Button x:Name="Button_OpenFolder" Grid.Column="2" Content="Open Folder"
                            Style="{StaticResource SecondaryButtonStyle}"
                            Click="Button_OpenFolder_Click" Padding="8,2" FontSize="11"
                            Visibility="Collapsed" Margin="0,0,6,0"
                            AutomationProperties.Name="Open Output Folder"
                            ToolTip="Open the folder the download was saved to"/>

                    <ToggleButton x:Name="ToggleButton_Log" Grid.Column="3" Content="Log"
                                  Padding="8,2" FontSize="11" Cursor="Hand"
                                  Background="Transparent" Foreground="{StaticResource TextPrimaryBrush}"
                                  BorderBrush="{StaticResource BorderBrushCustom}" BorderThickness="1"
                                  Checked="ToggleButton_Log_Changed" Unchecked="ToggleButton_Log_Changed"
                                  AutomationProperties.Name="Toggle Download Log"
                                  ToolTip="Show or hide the download log"/>
                </Grid>

                <!-- Log panel (collapsed by default) -->
                <TextBox x:Name="TextBox_Log" Visibility="Collapsed"
                         Height="150" Margin="0,0,0,6"
                         IsReadOnly="True" TextWrapping="NoWrap"
                         VerticalScrollBarVisibility="Auto" HorizontalScrollBarVisibility="Auto"
                         Background="{StaticResource CommandBarBrush}"
                         Foreground="{StaticResource TextPrimaryBrush}"
                         BorderBrush="{StaticResource BorderBrushCustom}" BorderThickness="1"
                         FontFamily="Consolas" FontSize="11"
                         AutomationProperties.Name="Download Log"/>

                <!-- Command bar (unchanged) -->
                <Grid>
                    <Grid.ColumnDefinitions>
                        <ColumnDefinition Width="Auto"/>
                        <ColumnDefinition Width="*"/>
                        <ColumnDefinition Width="Auto"/>
                    </Grid.ColumnDefinitions>
                    <TextBlock Text="Command:" Style="{StaticResource LabelStyle}" FontWeight="SemiBold" VerticalAlignment="Center"/>
                    <TextBox Grid.Column="1" x:Name="TextBox_Parameter" Style="{StaticResource TextBoxStyle}"
                             IsReadOnly="True" ToolTip="Generated command-line arguments"
                             AutomationProperties.Name="Generated Command Preview"
                             Background="{StaticResource CommandBarBrush}" FontFamily="Consolas" FontSize="11" Foreground="{StaticResource CommandTextBrush}"/>
                    <Button Grid.Column="2" x:Name="Button_CopyCommand" Content="📋 Copy"
                            Style="{StaticResource SecondaryButtonStyle}"
                            Click="Button_CopyCommand_Click" Margin="6,0,0,0" Padding="8,2" FontSize="11"
                            AutomationProperties.Name="Copy Command to Clipboard"
                            ToolTip="Copy generated command to clipboard"/>
                </Grid>
            </StackPanel>
        </Border>
```

- [ ] **Step 3: Add the code-behind helpers**

In `MainWindow.xaml.cs`, add fields and methods:

```csharp
        private readonly System.Text.StringBuilder _logBuffer = new();
        private string? _lastOutputDirectory;

        private void SetStatus(string text, bool isError = false)
        {
            TextBlock_Status.Text = text;
            TextBlock_Status.Foreground = isError ? ErrorBorderBrush : DefaultStatusBrush;
        }

        private static readonly Media.SolidColorBrush DefaultStatusBrush =
            CreateFrozenBrush(MediaColor.FromRgb(0x88, 0x88, 0xA8));

        private void AppendLog(string message)
        {
            _logBuffer.AppendLine(message);
            TextBox_Log.Text = _logBuffer.ToString();
            TextBox_Log.ScrollToEnd();
        }

        private void ResetRunState()
        {
            _logBuffer.Clear();
            TextBox_Log.Text = string.Empty;
            ProgressBar_Download.Value = 0;
            Button_OpenFolder.Visibility = Visibility.Collapsed;
        }

        private void ToggleButton_Log_Changed(object sender, RoutedEventArgs e)
        {
            TextBox_Log.Visibility = ToggleButton_Log.IsChecked == true
                ? Visibility.Visible
                : Visibility.Collapsed;
        }

        private void Button_OpenFolder_Click(object sender, RoutedEventArgs e)
        {
            if (!string.IsNullOrWhiteSpace(_lastOutputDirectory) && Directory.Exists(_lastOutputDirectory))
                StartShellTarget(_lastOutputDirectory);
        }
```

- [ ] **Step 4: Wire the download path**

In `Button_GO_Click`'s non-batch `else` branch, replace the `StartDownloadAsync` call:

```csharp
                        var argsForPreview = BuildArgsRE();
                        TextBox_Parameter.Text = argsForPreview;

                        var options = BuildDownloadOptions();
                        _lastOutputDirectory = string.IsNullOrWhiteSpace(options.SaveDir)
                            ? Environment.CurrentDirectory
                            : options.SaveDir;

                        ResetRunState();
                        SetStatus("Downloading…");

                        var progress = new Progress<int>(p => ProgressBar_Download.Value = p);
                        var log = new Action<string>(line => Dispatcher.InvokeAsync(() => AppendLog(line)));

                        var succeeded = await _downloadService.StartDownloadAsync(options, progress, log);

                        if (succeeded)
                        {
                            ProgressBar_Download.Value = 100;
                            SetStatus($"Saved to {_lastOutputDirectory}");
                            Button_OpenFolder.Visibility = Visibility.Visible;
                        }
                        else
                        {
                            SetStatus("Download failed — open the Log for details.", isError: true);
                            ToggleButton_Log.IsChecked = true;
                        }
```

In the **batch branch**, replace the `StartProcessAsync` block (the one wrapped in `Button_GO.IsEnabled = false` after the script is saved):

```csharp
                    Button_GO.IsEnabled = false;
                    Button_Stop.Visibility = Visibility.Visible;

                    _lastOutputDirectory = OptionValueNormalizer.NormalizeSaveDir(TextBox_WorkDir.Text)
                                           ?? Environment.CurrentDirectory;
                    ResetRunState();
                    SetStatus("Running batch…");

                    var batchProgress = new Progress<int>(p => ProgressBar_Download.Value = p);
                    var batchLog = new Action<string>(line => Dispatcher.InvokeAsync(() => AppendLog(line)));

                    try
                    {
                        var batchOk = await _downloadService.StartProcessAsync(
                            result.FilePath, string.Empty, batchLog, batchProgress);

                        if (batchOk)
                        {
                            ProgressBar_Download.Value = 100;
                            SetStatus($"Batch finished. Saved to {_lastOutputDirectory}");
                            Button_OpenFolder.Visibility = Visibility.Visible;
                        }
                        else
                        {
                            SetStatus("Batch failed — open the Log for details.", isError: true);
                            ToggleButton_Log.IsChecked = true;
                        }
                    }
                    catch (Exception ex)
                    {
                        SetStatus($"Batch error: {ex.Message}", isError: true);
                        ToggleButton_Log.IsChecked = true;
                    }
                    finally
                    {
                        Button_GO.IsEnabled = true;
                        Button_Stop.Visibility = Visibility.Collapsed;
                        try
                        {
                            if (File.Exists(result.FilePath))
                                File.Delete(result.FilePath);
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"Failed to delete temp batch file '{result.FilePath}': {ex.Message}");
                        }
                    }
```

In `StartCloudflareDownloadAsync`, replace the final `await _downloadService.StartProcessAsync(bat, "");` with:

```csharp
            _lastOutputDirectory = string.IsNullOrWhiteSpace(TextBox_WorkDir.Text)
                ? Environment.CurrentDirectory
                : TextBox_WorkDir.Text;
            ResetRunState();
            SetStatus("Running Cloudflare bypass…");

            var cfProgress = new Progress<int>(p => ProgressBar_Download.Value = p);
            var cfLog = new Action<string>(line => Dispatcher.InvokeAsync(() => AppendLog(line)));

            var cfOk = await _downloadService.StartProcessAsync(bat, string.Empty, cfLog, cfProgress);

            if (cfOk)
            {
                ProgressBar_Download.Value = 100;
                SetStatus($"Saved to {_lastOutputDirectory}");
                Button_OpenFolder.Visibility = Visibility.Visible;
            }
            else
            {
                SetStatus("Cloudflare bypass failed — open the Log for details.", isError: true);
                ToggleButton_Log.IsChecked = true;
            }
```

Note: the `.bat` written by `CfCommandBuilder.BuildBatchScript` ends with `pause`, which now blocks forever because there is no visible console to press a key in. Remove that line from `BuildBatchScript` and update `BuildBatchScript_ShouldEmitUtf8HeaderAndPause` in `CfCommandBuilderTests.cs` accordingly — rename it to `BuildBatchScript_ShouldEmitUtf8Header` and drop the `Assert.Contains("pause", bat)` line. The GUI log now carries the errors that `pause` existed to keep on screen.

- [ ] **Step 5: Build and verify manually**

```bash
dotnet build N_m3u8DL_RE_GUI.sln -c Debug
dotnet test N_m3u8DL_RE_GUI.sln
```

Expected: 0 errors; PASS, count unchanged.

Manual check: launch the app with a real `N_m3u8DL-RE.exe` configured and a working m3u8 URL. Expected: the progress bar advances, the Log toggle reveals streaming output, and on completion the status reads `Saved to <path>` with Open Folder visible. Then point it at a bad URL: status turns red, the log panel opens by itself, and the exit code appears in the log.

- [ ] **Step 6: Commit**

```bash
git add N_m3u8DL_RE_GUI/MainWindow.xaml N_m3u8DL_RE_GUI/MainWindow.xaml.cs
git commit -m "feat(ui): show download progress, log and result in-window

Success and failure previously produced identical GUI state because the
only feedback lived in a detached console. Add a status strip, a
collapsible log, and an Open Folder action, and clamp the window to the
work area so the new strip does not land behind the taskbar.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

## Task 10: Enforce accessibility with a XAML test, then fix the XAML

78 of 86 interactive controls have no accessible name, there are no keyboard accelerators, and no style defines a `FocusVisualStyle`. A test that parses `MainWindow.xaml` as XML turns all of that into TDD and stops it regressing.

**Files:**
- Create: `N_m3u8DL_RE_GUI.Tests/Fixtures/XamlFixture.cs`
- Create: `N_m3u8DL_RE_GUI.Tests/Unit/Xaml/XamlAccessibilityTests.cs`
- Modify: `N_m3u8DL_RE_GUI.Tests/N_m3u8DL_RE_GUI.Tests.csproj`
- Modify: `N_m3u8DL_RE_GUI/MainWindow.xaml`

**Interfaces:**
- Consumes: nothing.
- Produces: `static XDocument XamlFixture.MainWindow` and `static IEnumerable<XElement> XamlFixture.InteractiveControls()`.

- [ ] **Step 1: Copy the XAML into the test output**

In `N_m3u8DL_RE_GUI.Tests.csproj`, add before `</Project>`:

```xml
  <ItemGroup>
    <!-- XamlAccessibilityTests parses this file as XML; it must sit next to the test dll. -->
    <None Include="..\N_m3u8DL_RE_GUI\MainWindow.xaml" Link="MainWindow.xaml">
      <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
    </None>
  </ItemGroup>
```

- [ ] **Step 2: Write the fixture**

Create `N_m3u8DL_RE_GUI.Tests/Fixtures/XamlFixture.cs`:

```csharp
#nullable enable
using System.Xml.Linq;

namespace N_m3u8DL_RE_GUI.Tests.Fixtures;

/// <summary>Loads MainWindow.xaml as XML so structural UI invariants can be asserted.</summary>
public static class XamlFixture
{
    private static readonly XNamespace X = "http://schemas.microsoft.com/winfx/2006/xaml";
    private static readonly XNamespace Wpf = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

    private static readonly Lazy<XDocument> Document = new(() =>
        XDocument.Load(Path.Combine(AppContext.BaseDirectory, "MainWindow.xaml")));

    public static XDocument MainWindow => Document.Value;

    public static readonly string[] InteractiveElementNames =
        { "TextBox", "CheckBox", "ComboBox", "Button", "ToggleButton", "TabItem" };

    /// <summary>
    /// Controls the user can reach, excluding anything declared inside Window.Resources
    /// (styles and templates, which carry no user-facing identity of their own).
    /// </summary>
    public static IEnumerable<XElement> InteractiveControls()
    {
        var resources = MainWindow.Root!.Element(Wpf + "Window.Resources");

        return MainWindow.Descendants()
            .Where(e => InteractiveElementNames.Contains(e.Name.LocalName))
            .Where(e => resources == null || !e.Ancestors().Contains(resources));
    }

    public static string Identify(XElement element) =>
        (string?)element.Attribute(X + "Name")
        ?? (string?)element.Attribute("Content")
        ?? (string?)element.Attribute("Header")
        ?? $"<{element.Name.LocalName}> (unnamed)";

    public static bool HasAutomationName(XElement element) =>
        element.Attribute("AutomationProperties.Name") != null
        || element.Attribute("AutomationProperties.LabeledBy") != null;
}
```

- [ ] **Step 3: Write the failing tests**

Create `N_m3u8DL_RE_GUI.Tests/Unit/Xaml/XamlAccessibilityTests.cs`:

```csharp
#nullable enable
using System.Xml.Linq;
using N_m3u8DL_RE_GUI.Tests.Fixtures;
using Xunit;

namespace N_m3u8DL_RE_GUI.Tests.Unit.Xaml;

/// <summary>
/// Structural accessibility invariants for MainWindow.xaml. These are cheap, run in the
/// normal suite, and stop the 78 missing accessible names from creeping back.
/// </summary>
public class XamlAccessibilityTests
{
    [Fact]
    public void EveryInteractiveControl_ShouldHaveAnAccessibleName()
    {
        var missing = XamlFixture.InteractiveControls()
            .Where(e => !XamlFixture.HasAutomationName(e))
            .Select(XamlFixture.Identify)
            .ToList();

        Assert.True(missing.Count == 0,
            $"{missing.Count} control(s) have no AutomationProperties.Name:\n  " + string.Join("\n  ", missing));
    }

    [Fact]
    public void Window_ShouldDeclareKeyboardShortcutsForDownloadAndStop()
    {
        var xaml = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "MainWindow.xaml"));

        Assert.Contains("Window.InputBindings", xaml);
        Assert.Contains("Key=\"S\"", xaml);
        Assert.Contains("Modifiers=\"Alt\"", xaml);
        Assert.Contains("Key=\"Escape\"", xaml);
    }

    [Fact]
    public void EveryButtonAndCheckBoxStyle_ShouldDefineAFocusVisualStyle()
    {
        var xaml = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "MainWindow.xaml"));

        Assert.Contains("x:Key=\"AccessibleFocusVisual\"", xaml);
        foreach (var styleKey in new[] { "ButtonStyle", "SecondaryButtonStyle", "UpdatePillButtonStyle", "CheckBoxStyle" })
        {
            var index = xaml.IndexOf($"x:Key=\"{styleKey}\"", StringComparison.Ordinal);
            Assert.True(index >= 0, $"Style {styleKey} not found");

            var end = xaml.IndexOf("</Style>", index, StringComparison.Ordinal);
            var body = xaml[index..end];
            Assert.True(body.Contains("FocusVisualStyle"), $"{styleKey} does not set FocusVisualStyle");
        }
    }

    [Fact]
    public void NoTooltip_ShouldAdvertiseAShortcutThatIsNotBound()
    {
        var xaml = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "MainWindow.xaml"));

        // If a tooltip mentions Alt+S, the binding must exist.
        if (xaml.Contains("Alt+S"))
            Assert.Contains("Modifiers=\"Alt\"", xaml);
    }

    [Fact]
    public void GroupBoxHeaders_ShouldNotContainUnescapedUnderscores()
    {
        // RecognizesAccessKey="True" on the GroupBox header makes WPF eat a lone '_'.
        // "curl_cffi" rendered as "curlcffi" until the underscore was doubled.
        var offenders = XamlFixture.MainWindow.Descendants()
            .Where(e => e.Name.LocalName == "GroupBox")
            .Select(e => (string?)e.Attribute("Header"))
            .Where(h => h != null && h.Contains('_') && !h.Contains("__"))
            .ToList();

        Assert.True(offenders.Count == 0,
            "GroupBox headers with a single underscore (WPF will swallow it): " + string.Join(", ", offenders));
    }
}
```

- [ ] **Step 4: Run them to verify they fail**

```bash
dotnet test N_m3u8DL_RE_GUI.sln --filter "FullyQualifiedName~XamlAccessibilityTests"
```

Expected: FAIL — the name test lists the missing controls, the shortcut test finds no `Window.InputBindings`, the focus test finds no `AccessibleFocusVisual`, and the header test reports `⚡ Cloudflare Bypass (curl_cffi)`.

- [ ] **Step 5: Add the input bindings**

In `MainWindow.xaml`, immediately after `</Window.Resources>`:

```xml
    <Window.InputBindings>
        <KeyBinding Key="S" Modifiers="Alt" Command="{x:Static local:MainWindow.DownloadCommand}"/>
        <KeyBinding Key="Escape" Command="{x:Static local:MainWindow.StopCommand}"/>
    </Window.InputBindings>
```

In `MainWindow.xaml.cs`, add the routed commands and their handlers:

```csharp
        public static readonly RoutedUICommand DownloadCommand =
            new("Start Download", nameof(DownloadCommand), typeof(MainWindow));

        public static readonly RoutedUICommand StopCommand =
            new("Stop Download", nameof(StopCommand), typeof(MainWindow));
```

And in the constructor, after `InitializeComponent();`:

```csharp
            CommandBindings.Add(new CommandBinding(DownloadCommand, (s, e) => Button_GO_Click(s, e)));
            CommandBindings.Add(new CommandBinding(StopCommand, (s, e) => Button_Stop_Click(s, e)));
```

Add `using System.Windows.Input;` if not present (it is, at line 11).

- [ ] **Step 6: Add the shared focus visual**

In `Window.Resources`, before `LabelStyle`:

```xml
        <!-- Visible focus ring. WPF's default is a black dotted rectangle, invisible on
             this palette, so every custom style must opt into this one. -->
        <Style x:Key="AccessibleFocusVisual">
            <Setter Property="Control.Template">
                <Setter.Value>
                    <ControlTemplate>
                        <Rectangle Stroke="{StaticResource AccentBrush}" StrokeThickness="2"
                                   SnapsToDevicePixels="True" Margin="-2"/>
                    </ControlTemplate>
                </Setter.Value>
            </Setter>
        </Style>
```

Add this setter to `ButtonStyle`, `SecondaryButtonStyle`, `UpdatePillButtonStyle`, `CheckBoxStyle` and `TextBoxStyle`:

```xml
            <Setter Property="FocusVisualStyle" Value="{StaticResource AccessibleFocusVisual}"/>
```

Add a keyboard-focus trigger to `LeftTabItemStyle`'s `ControlTemplate.Triggers`:

```xml
                            <Trigger Property="IsKeyboardFocusWithin" Value="True">
                                <Setter TargetName="TabBorder" Property="Background" Value="#1C1C22"/>
                                <Setter TargetName="TabBorder" Property="BorderThickness" Value="3,0,0,0"/>
                            </Trigger>
```

- [ ] **Step 7: Fix the swallowed underscore**

Change line 629's header to double the underscore:

```xml
                        <GroupBox Header="⚡ Cloudflare Bypass (curl__cffi)" Style="{StaticResource GroupBoxStyle}" Foreground="{StaticResource CfAmberBrush}">
```

- [ ] **Step 8: Add the missing accessible names**

Run the failing test to get the exact list, then add `AutomationProperties.Name="<human label>"` to every control it names. Use the visible label text, not the `x:Name` — e.g. `TextBox_SelectVideo` gets `AutomationProperties.Name="Select Video Regex"`, `CheckBox_Del` gets `AutomationProperties.Name="Delete Temporary Files After Done"`, and the six `TabItem`s get `AutomationProperties.Name="Download"`, `"Network"`, `"Security"`, `"Media"`, `"Live"`, `"Advanced"` so screen readers stop reading emoji names.

Re-run after each tab's worth of edits:

```bash
dotnet test N_m3u8DL_RE_GUI.sln --filter "FullyQualifiedName~EveryInteractiveControl"
```

- [ ] **Step 9: Remove the false Alt+S promise or keep it honest**

`Button_GO`'s tooltip already says `"Start download (Alt+S)"`. Step 5 made that true, so leave it. Verify `NoTooltip_ShouldAdvertiseAShortcutThatIsNotBound` passes.

- [ ] **Step 10: Fix the validation border so style triggers survive**

`ApplyValidationState` writes `BorderBrush` as a local value, which outranks `TextBoxStyle`'s `IsFocused` trigger forever. Replace it with a tag-driven trigger.

In `MainWindow.xaml.cs`:

```csharp
        private void ApplyValidationState(TextBox? textBox, bool isValid)
        {
            if (textBox == null)
                return;
            // Set a Tag, not BorderBrush. A local BorderBrush value outranks the style's
            // IsFocused/IsMouseOver triggers in WPF property precedence and silently
            // removed the focus indicator from the primary URL field.
            textBox.Tag = isValid ? null : "invalid";
        }
```

In `TextBoxStyle`, add as the **first** trigger so focus and hover still win:

```xml
                <Trigger Property="Tag" Value="invalid">
                    <Setter Property="BorderBrush" Value="#E74C3C"/>
                </Trigger>
```

Field cleanup, precisely: `DefaultBorderBrush` becomes unused — delete it. **Keep `ErrorBorderBrush`**; Task 9's `SetStatus` uses it for the failure state.

- [ ] **Step 11: Run everything**

```bash
dotnet build N_m3u8DL_RE_GUI.sln -c Debug
dotnet test N_m3u8DL_RE_GUI.sln
```

Expected: 0 errors; PASS, count ≥ 445.

Manual check: launch, press Tab repeatedly. Every control shows a blue 2px ring. Press Alt+S — the download starts. Press Escape — it stops.

- [ ] **Step 12: Commit**

```bash
git add N_m3u8DL_RE_GUI/MainWindow.xaml N_m3u8DL_RE_GUI/MainWindow.xaml.cs N_m3u8DL_RE_GUI.Tests/Fixtures/XamlFixture.cs N_m3u8DL_RE_GUI.Tests/Unit/Xaml/XamlAccessibilityTests.cs N_m3u8DL_RE_GUI.Tests/N_m3u8DL_RE_GUI.Tests.csproj
git commit -m "feat(a11y): accessible names, keyboard shortcuts and focus visuals

Adds Alt+S/Escape bindings, a visible focus ring on every custom style,
AutomationProperties.Name on all interactive controls, and a XAML-parsing
test suite that fails if any of it regresses. Also fixes the validation
border writing a local value that permanently defeated the focus trigger,
and the GroupBox header underscore WPF was swallowing (curl_cffi).

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

## Task 11: Commit to English-only and correct the docs

Decided scope: the UI ships English. Remove the Thai dialogs, collapse the resource variants, and stop the README claiming things that are not true.

**Files:**
- Modify: `N_m3u8DL_RE_GUI/ViewModels/MainViewModel.cs:71, 83, 85, 118, 124, 126, 143, 187`
- Modify: `N_m3u8DL_RE_GUI/MainWindow.xaml.cs:89, 651, 656, 661, 669, 1175`
- Delete: `N_m3u8DL_RE_GUI/Properties/Resources.en-US.Designer.cs`, `Resources.zh-TW.Designer.cs`, and the matching `.resx` files
- Modify: `README.md`, `CHANGELOG.md`

**Interfaces:**
- Consumes: nothing.
- Produces: no `Properties.Resources` references remain in the GUI project.

- [ ] **Step 1: Replace the Thai strings in MainViewModel**

```csharp
        // line 71
            MessageBox.Show("Please enter a URL to download.", "Missing Input",
                MessageBoxButton.OK, MessageBoxImage.Warning);

        // line 83 / 85
                _logBuilder.AppendLine("Starting download...");
            LogOutput = "Starting download...\n";

        // line 118
                MessageBox.Show("Download failed.", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);

        // line 124 / 126
            lock (_logLock) { _logBuilder.AppendLine($"Error: {ex.Message}"); }
            UpdateLogOutput(_logBuilder.ToString());
            MessageBox.Show($"Error: {ex.Message}", "Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
```

- [ ] **Step 2: Inline the six resource strings**

Replace each `Properties.Resources.StringN` in `MainWindow.xaml.cs` with a literal, and give each `MessageBox.Show` a title and an icon:

| Call site | Replacement |
|---|---|
| `:89` `String1` | `"Select download folder"` |
| `:651` `String2` | `MessageBox.Show("N_m3u8DL-RE.exe was not found. Set its path on the Download tab, or right-click the Executable field and choose Get Downloader.", "Downloader Not Found", MessageBoxButton.OK, MessageBoxImage.Warning);` |
| `:656` `String3` | `MessageBox.Show("Enter a URL or file path first.", "Missing Input", MessageBoxButton.OK, MessageBoxImage.Warning);` |
| `:669` `String4` | `"Working…"` |
| `:1175` `String6` | `MessageBox.Show("That file is not a valid 16-byte key file.", "Invalid Key File", MessageBoxButton.OK, MessageBoxImage.Warning);` |
| `:661` `String7` | `MessageBox.Show("Proxy must start with http:// or socks5://.", "Invalid Proxy", MessageBoxButton.OK, MessageBoxImage.Warning);` |

- [ ] **Step 3: Delete the resource variants**

```bash
git rm N_m3u8DL_RE_GUI/Properties/Resources.en-US.Designer.cs \
       N_m3u8DL_RE_GUI/Properties/Resources.zh-TW.Designer.cs
git rm N_m3u8DL_RE_GUI/Properties/Resources.en-US.resx \
       N_m3u8DL_RE_GUI/Properties/Resources.zh-TW.resx
```

Keep `Resources.Designer.cs` / `Resources.resx` only if something still references them; if `grep -rn "Properties.Resources" N_m3u8DL_RE_GUI --include=*.cs` returns nothing, remove those too along with the `xmlns:props` declaration at `MainWindow.xaml:7`.

- [ ] **Step 4: Correct the README**

Remove "Multi-language UI (EN/CN/TW)" from the feature list. Fix each drift the audit found:
- `config.json` → describe both: `config.json` (encrypted secrets) alongside legacy `config.txt`
- "Click GO" → "Click **▶ Download**"
- Cloudflare Bypass "Security Tab (🔒)" → "**Network** tab (🌐)"
- Delete the "expand the Cloudflare Bypass section" step — there is no expander
- "Check the 'Bypass CF' option" → "Check **Enable Cloudflare Bypass**"
- Remove "Download progress visualization" from the roadmap — Task 9 shipped it
- Remove "collapsible sections" from the roadmap or mark it unstarted

- [ ] **Step 5: Correct the CHANGELOG**

The 2.1.5 entry claims `Enabled <Nullable>enable</Nullable> in the core library`. `N_m3u8DL_RE_GUI.Core.csproj` still says `disable`. Either make it true or delete the line — deleting is correct here, since flipping it is not in this plan's scope.

Add a `## [Unreleased]` section describing Tasks 1-11.

- [ ] **Step 6: Verify nothing references the deleted resources**

```bash
grep -rn "Properties.Resources" N_m3u8DL_RE_GUI --include=*.cs --include=*.xaml
dotnet build N_m3u8DL_RE_GUI.sln -c Debug
dotnet test N_m3u8DL_RE_GUI.sln
```

Expected: no matches; 0 errors; PASS.

- [ ] **Step 7: Commit**

```bash
git add -A
git commit -m "chore(i18n): commit to an English-only UI and correct the docs

The XAML was already 100% hard-coded English while MainViewModel shipped
Thai dialogs and the neutral resource fallback was Simplified Chinese.
Drop the unused resource variants, replace the Thai strings, and remove
the README claims that never matched the build.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

## Deferred — not in this plan

These were found in the same audit and are deliberately out of scope. Each is a candidate for its own plan.

**P1 — correctness/perf:** `sb.ToString().Contains()` O(N²) in `UtilityService.GetHtmlTitleStreamingAsync:81` · `StreamReader` ignoring HTTP charset (`:68`) · `Encoding.Default` is UTF-8 on .NET Core so the ANSI branch in `TextEncodingDetector:49` recovers nothing · UTF-8 detection failing when a sequence straddles byte 8192 (`:102`) · `new[] { '\\', '"' }` allocating per call in `ArgsBuilder:233` · `--custom-range` / `--mux-after-done` bypassing the escaper (`:78`, `:168`) · `;` corrupting raw values in `ConfigService:52` · `CON.txt.bak` not caught by `UtilityService:177`

**P1 — UX:** `CheckBox_AudioOnly` silently overwriting Media-tab fields · CF mode silently discarding five tabs · `Combo_UILanguage` mislabelled

**P1 — measured contrast:** ten WCAG AA failures listed in the critique snapshot, including the update pill at 2.10:1 and the Download button degrading to 3.65:1 on hover

**P2:** `BuildArgsRE` / `BuildDownloadOptions` being ~100 lines of duplicated field mapping · `StartExecutableWithArguments` dead code · empty `UtilityService.Dispose()` · `DownloadOptions.AudioOnly` checking `"all"` while the GUI writes `".*"` · `GitHubUpdateCheckService`'s hard-coded static `HttpClient` blocking tests · `CleanStaleTempBatchFiles` missing `batch_*.bat` · `Window_Closing` saving to a relative path · IA restructure (task-named groups, progressive disclosure) · `CheckBox_NoAnsiColor` now vestigial after Task 8

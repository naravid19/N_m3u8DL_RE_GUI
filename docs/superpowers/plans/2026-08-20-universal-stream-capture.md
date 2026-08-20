# Universal Stream Capture Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let a user who is watching a stream in their browser get that stream into this GUI with its required headers, without hand-copying anything.

**Architecture:** Three input paths, all landing on one shared `CapturedRequest` record and one shared header-emitting seam. Path 1 parses a `curl` command from the clipboard (browser devtools already generates these). Path 2 scans a saved `.har` file when the user does not know which request is the stream. Path 3 is a browser extension that finds the stream itself and puts a `curl` command on the clipboard — which means it reuses Path 1's parser and needs **zero new C#**.

**Tech Stack:** .NET 9, WPF, `System.Text.Json` (already in the framework — no new package), xunit + NSubstitute. Phase 3 is Chrome MV3 / vanilla JS, no build step.

**Spec:** This document. Supersedes `c:\Users\narav\.gemini\antigravity\brain\a824a840-.../implementation_plan.md`, whose defects are catalogued in Part 0.

## Global Constraints

- Config back-compat is mandatory. Existing `config.txt` and `config.json` must keep loading. Never rename a persisted key.
- All user-visible strings are English.
- **No new NuGet packages.** `System.Text.Json` ships in the framework.
- Target frameworks are fixed: `net9.0` for Core, `net9.0-windows` for GUI and tests.
- No admin/elevation may ever be required. The app ships as a portable zip.
- Secrets (Cookie, Authorization) must never reach a process command line, a log file, or plaintext on disk.
- Every new Core type is pure and testable with no WPF reference.

## Scope boundary

Every path here operates on **requests the user's own browser already completed successfully**. Nothing in this plan solves a bot challenge, forges a TLS fingerprint, or decrypts DRM. The tool reads a network log the browser produced and hands the URL plus headers to a downloader. If a site blocked the browser, it blocks this too — that is the intended behaviour, not a gap to close.

---

## Part 0: Defects in the inherited plan

Each was verified against the code, not assumed.

### D1 — `Registry.ClassesRoot` requires admin *(blocks every normal user)*

The inherited plan writes the URL scheme with `Registry.ClassesRoot.CreateSubKey(...)`. `HKEY_CLASSES_ROOT` is a merged view; **writes go to `HKLM\Software\Classes`**, which throws `UnauthorizedAccessException` for a non-elevated process. This app ships as a portable zip with no installer and no elevation. The registration would fail for essentially everyone.

Correct key is `HKCU\Software\Classes\<scheme>` — per-user, no admin, same resolution behaviour.

### D2 — Secrets on the command line *(security regression)*

`nm3u8dlre://download?...&cookie=SESSIONID%3Dabc` is delivered by the OS as a **process command-line argument**. That is readable by Task Manager's "Command line" column, `wmic process get commandline`, any process with `PROCESS_QUERY_LIMITED_INFORMATION`, and most EDR/Sysmon pipelines — which commonly forward it to a SIEM.

This project already DPAPI-encrypts the exact same Cookie value at rest in `config.json`. Passing it in cleartext on a command line contradicts that threat model. Secondary problem: browsers truncate custom-scheme URLs around ~2000 chars, and real cookie headers routinely exceed that, so it would also silently corrupt long values.

### D3 — Adding `.har` to `DropInputRules` would break the drop *(functional bug)*

`DropInputRules.UrlInputExtensions` means "this path is a valid **stream input** to hand to N_m3u8DL-RE". `MainWindow.xaml.cs:593` acts on it by assigning the path into `TextBox_URL`. Adding `.har` there makes the app pass `fairyanime.net.har` to the downloader as if it were a playlist.

A HAR is a **source to extract an input from**, not an input. It needs a separate predicate and a branch that runs *before* `IsSupportedUrlInputPath`.

### D4 — MV3 service workers are ephemeral *(guaranteed bug in Phase 3)*

The inherited `background.js` keeps detected streams in module-scope variables. An MV3 service worker is terminated after ~30 s idle and restarted on the next event, wiping module state. Detected streams must live in `chrome.storage.session`.

Related: MV3 removed **blocking** `webRequest`, but observational `onSendHeaders`/`onHeadersReceived` still work. Reading `Cookie`/`Set-Cookie` additionally requires `extraHeaders` in the extra-info spec **and** host permissions.

### D5 — HAR parsing strategy is unspecified and the file is hostile

The sample HAR is 4.3 MB; real captures reach 100 MB+. It also contains HTTP/2 pseudo-headers (`:authority`, `:method`, `:path`, `:scheme` — confirmed present in the sample) which are **not valid to re-send** and must be filtered. HARs also carry passwords and auth tokens in POST bodies, so parsed content must never be logged.

The plan says "parse .har → video URL" with no strategy for size, for pseudo-headers, or for the real algorithmic problem: **a live HLS capture contains hundreds of segment requests and one manifest.** Naively matching `video/*` returns the segments.

### D6 — Phase ordering ships dead code

The plan builds the URL scheme (Phase 2) before the extension (Phase 3) that is its only consumer. That is a phase whose entire output is unreachable — a YAGNI violation, and untestable end-to-end until Phase 3 lands.

### D7 — The two claimed "real bugs" are not bugs

Verified both. Reporting honestly rather than inheriting the claim:

- `BatchScriptService.cs:76` — `AppendLine("::Created by N_m3u8DL_RE_GUI\r\n")` does emit a doubled newline, and **is inconsistent** with the identical line at `:116` which has no `\r\n`. But batch ignores blank lines; this is cosmetic, not functional. Worth fixing for consistency (Task 1.0), not worth a "bug fix" label.
- `ConsoleOutputParser.cs:41` — `StripAnsi(rawLine ?? string.Empty)` applies `??` to a non-nullable `string` parameter. The defensive operator is unreachable, not wrong. The fix is to widen the parameter to `string?` to match the intent. A nit.

### D8 — Found while verifying: the plans directory is not in git

`.gitignore:290` is a bare `/docs`, which ignores the directory itself. Git does not descend into an ignored directory, so the `!/docs/superpowers/` negation at `:283` **can never take effect**. Confirmed: `git check-ignore -v docs/superpowers/plans/test.md` → `.gitignore:290:/docs`, and `git ls-files docs` is empty.

Every plan document from prior sessions exists only on disk. Fixed in Task 0.

---

## Revised architecture

The load-bearing decision: **the clipboard is the integration bus.**

Once a `curl` command can be pasted, the browser extension does not need a URL scheme, a registry key, a native-messaging host, a localhost port, or a firewall exception. It writes a `curl` command to the clipboard and the user clicks the button that already exists. The extension becomes purely additive JavaScript with **zero coupling to the C# app**.

That removes D1 and D2 entirely rather than fixing them, and it drops Phase 3 from "extension + registry + protocol handler + IPC" to "extension".

```
Phase 1  Paste-as-cURL            ~250 lines C#   ← ships alone, immediate value
Phase 2  HAR drop + picker        ~350 lines C#   ← for "I don't know which request"
Phase 3  Browser extension        ~200 lines JS   ← ZERO new C#, rides Phase 1
Phase 4  True 1-click IPC         deferred        ← only if users ask; see Deferred
```

Cost of the clipboard hop versus a protocol handler: one extra click. Benefit: no admin, no registry, no secrets on a command line, no port conflict, no MV3 native-host manifest, no Chrome Web Store dependency, and Phase 3 can be developed and shipped by someone who never touches the C# solution.

### Why cURL before HAR

Both were compared on real steps, not on feel:

| | Paste-as-cURL | HAR drop |
|---|---|---|
| User steps | F12 → filter `m3u8` → right-click → Copy as cURL → Paste | F12 → play → right-click → Save all as HAR → find file → drag → pick from list |
| Header fidelity | Exact, every header the browser sent | Exact, but pseudo-headers must be stripped |
| Code | ~250 lines, one tokenizer | ~350 lines + a picker window + size/secret handling |
| Fails when | User can't tell which request is the stream | — |

cURL is fewer steps *and* less code. HAR's only advantage is handling the case where the user cannot identify the request — which is real, but is the minority case and the harder code. Ship the cheap majority path first.

---

## File Structure

**Create**

| File | Responsibility |
|---|---|
| `N_m3u8DL_RE_GUI.Core/Capture/CapturedRequest.cs` | The shared seam: `CapturedRequest`, `CapturedHeader`, `CapturedStreamKind`. Every path produces this. |
| `N_m3u8DL_RE_GUI.Core/Capture/HeaderPolicy.cs` | One place deciding which headers survive into `-H`. Used by both parsers. |
| `N_m3u8DL_RE_GUI.Core/Capture/CurlCommandParser.cs` | Tokenizes a `curl` command (bash/cmd/Firefox dialects) → `CapturedRequest`. |
| `N_m3u8DL_RE_GUI.Core/Capture/HarStreamExtractor.cs` | Reads a `.har` → ranked `CapturedRequest` list. |
| `N_m3u8DL_RE_GUI/Views/StreamPickerWindow.xaml(.cs)` | Small modal listing candidates when a HAR yields more than one. |
| `N_m3u8DL_RE_GUI.Tests/Unit/Core/Capture/CurlCommandParserTests.cs` | |
| `N_m3u8DL_RE_GUI.Tests/Unit/Core/Capture/HarStreamExtractorTests.cs` | |
| `N_m3u8DL_RE_GUI.Tests/Unit/Core/Capture/HeaderPolicyTests.cs` | |
| `N_m3u8DL_RE_GUI.Tests/Fixtures/Har/*.har` | Synthetic minimal HARs. Never a real capture — real ones carry real cookies. |
| `extension/` | Phase 3. Manifest, service worker, popup. |

**Modify**

| File | Change |
|---|---|
| `.gitignore:290` | Delete the bare `/docs` line (D8). |
| `N_m3u8DL_RE_GUI.Core/ArgsBuilder.cs:38` | Split headers on `\|` **and** newline. |
| `N_m3u8DL_RE_GUI.Core/DropInputRules.cs` | Add `IsHarPath`. **Do not** touch `UrlInputExtensions` (D3). |
| `N_m3u8DL_RE_GUI/MainWindow.xaml` | "Paste from browser" button; make `TextBox_Headers` multi-line. |
| `N_m3u8DL_RE_GUI/MainWindow.xaml.cs:591` | HAR branch *before* the existing URL-input branch; paste handler. |
| `N_m3u8DL_RE_GUI/Services/BatchScriptService.cs:76` | Cosmetic newline consistency (D7). |
| `N_m3u8DL_RE_GUI.Core/ConsoleOutputParser.cs:41` | Widen parameter to `string?` (D7). |

---

## Phase 0 — Repository hygiene

### Task 0: Make the plans directory trackable

**Files:** Modify `.gitignore:288-291`

- [ ] **Step 1: Confirm the defect**

```bash
git check-ignore -v docs/superpowers/plans/2026-08-20-universal-stream-capture.md
```

Expected: `.gitignore:290:/docs` — proving line 290 is the rule that wins.

- [ ] **Step 2: Delete the bare `/docs` line**

Remove line 290 (`/docs`) entirely. Lines 282-283 (`/docs/*` then `!/docs/superpowers/`) already express the intent correctly on their own.

- [ ] **Step 3: Verify the negation now works**

```bash
git check-ignore -v docs/superpowers/plans/2026-08-20-universal-stream-capture.md
```

Expected: **no output, exit code 1** (not ignored). Also confirm `docs/dev-notes/` is still ignored:

```bash
git check-ignore -v docs/dev-notes/INDEX.md
```

Expected: `.gitignore:282:/docs/*`

- [ ] **Step 4: Commit, including the previously-untracked plans**

```bash
git add .gitignore docs/superpowers/
git commit -m "fix(git): stop ignoring docs/superpowers so plans are tracked

A bare /docs rule shadowed the !/docs/superpowers/ negation. Git does not
descend into an ignored directory, so the exception could never apply and
every plan document lived only on disk."
```

---

## Phase 1 — Paste as cURL

### Task 1.0: Housekeeping fixes from D7

**Files:**
- Modify: `N_m3u8DL_RE_GUI/Services/BatchScriptService.cs:76`
- Modify: `N_m3u8DL_RE_GUI.Core/ConsoleOutputParser.cs:41`

**Interfaces:**
- Produces: no API change. `ConsoleOutputParser.Clean` accepts `string?` after this task.

- [ ] **Step 1: Write a test pinning the two batch headers as identical**

Add to `N_m3u8DL_RE_GUI.Tests/Unit/Services/BatchScriptServiceTests.cs`:

```csharp
[Fact]
public async Task BothBatchPaths_ShouldEmitTheSamePreamble()
{
    var directory = Path.Combine(Path.GetTempPath(), $"batch_{Guid.NewGuid():N}");
    Directory.CreateDirectory(directory);
    var textFile = Path.Combine(directory, "list.txt");
    await File.WriteAllTextAsync(textFile, "https://example.com/a.m3u8");

    try
    {
        var service = new BatchScriptService();
        var fromDirectory = await service.BuildScriptAsync(
            directory, @"C:\re.exe", _ => Task.FromResult("t"), _ => "--args");
        var fromTextFile = await service.BuildScriptAsync(
            textFile, @"C:\re.exe", _ => Task.FromResult("t"), _ => "--args");

        static string Preamble(string script) =>
            string.Join("\n", script.Split('\n').Take(4).Select(l => l.TrimEnd('\r')));

        Assert.Equal(Preamble(fromDirectory.Content), Preamble(fromTextFile.Content));
    }
    finally
    {
        Directory.Delete(directory, recursive: true);
    }
}
```

> If `BatchScriptBuildResult`'s content member is not named `Content`, use the actual name — check `BatchScriptBuildResult`'s definition before running.

- [ ] **Step 2: Run it and watch it fail**

```bash
dotnet test --filter "FullyQualifiedName~BothBatchPaths_ShouldEmitTheSamePreamble"
```

Expected: FAIL — the directory path has a blank 4th line, the text-file path does not.

- [ ] **Step 3: Make both preambles identical**

In `BatchScriptService.cs:76`, change:

```csharp
builder.AppendLine("::Created by N_m3u8DL_RE_GUI\r\n");
```

to:

```csharp
builder.AppendLine("::Created by N_m3u8DL_RE_GUI");
```

- [ ] **Step 4: Widen the `Clean` parameter**

In `ConsoleOutputParser.cs:41`, change `string rawLine` to `string? rawLine`. The `?? string.Empty` is now meaningful rather than unreachable.

```csharp
public static string Clean(string? rawLine) => StripAnsi(rawLine ?? string.Empty).Trim();
```

- [ ] **Step 5: Run the full suite**

```bash
dotnet test
```

Expected: all previously-passing tests still pass, plus the new one.

- [ ] **Step 6: Commit**

```bash
git add N_m3u8DL_RE_GUI/Services/BatchScriptService.cs N_m3u8DL_RE_GUI.Core/ConsoleOutputParser.cs N_m3u8DL_RE_GUI.Tests/Unit/Services/BatchScriptServiceTests.cs
git commit -m "fix: align batch preamble across both build paths; widen Clean to string?"
```

---

### Task 1.1: The shared capture seam

**Files:**
- Create: `N_m3u8DL_RE_GUI.Core/Capture/CapturedRequest.cs`
- Test: covered indirectly by Tasks 1.2–1.3; no standalone test (it is a record with one derived member, tested through its consumers)

**Interfaces:**
- Produces: `CapturedHeader(string Name, string Value)`, `CapturedStreamKind`, `CapturedRequest(string Url, IReadOnlyList<CapturedHeader> Headers, CapturedStreamKind Kind)` with `string ToHeaderLines()`. Tasks 1.2, 1.3, 2.1, 2.3 all consume this.

- [ ] **Step 1: Create the file**

```csharp
#nullable enable
using System.Collections.Generic;
using System.Linq;

namespace N_m3u8DL_RE_GUI.Core.Capture;

/// <summary>One HTTP header worth re-sending. Name keeps its original casing.</summary>
public sealed record CapturedHeader(string Name, string Value);

/// <summary>What kind of stream a captured URL appears to be. Drives ranking.</summary>
public enum CapturedStreamKind
{
    /// <summary>Not recognisably a stream — a segment, a script, an image.</summary>
    Unknown,
    Hls,
    Dash,
    /// <summary>A progressive media file: .mp4, .webm, or a video/* response.</summary>
    Media
}

/// <summary>
/// A single request lifted out of a browser capture, reduced to what a downloader
/// needs. Produced by every capture path (cURL paste, HAR drop) so the GUI has one
/// shape to consume.
/// </summary>
public sealed record CapturedRequest(
    string Url,
    IReadOnlyList<CapturedHeader> Headers,
    CapturedStreamKind Kind)
{
    /// <summary>
    /// Newline-separated "Name: Value" lines, the format TextBox_Headers holds.
    /// Newlines are illegal inside an HTTP header value, so this round-trips
    /// losslessly — unlike the legacy pipe separator.
    /// </summary>
    public string ToHeaderLines() =>
        string.Join("\n", Headers.Select(h => $"{h.Name}: {h.Value}"));
}
```

- [ ] **Step 2: Build**

```bash
dotnet build N_m3u8DL_RE_GUI.Core
```

Expected: 0 errors, 0 warnings.

- [ ] **Step 3: Commit**

```bash
git add N_m3u8DL_RE_GUI.Core/Capture/CapturedRequest.cs
git commit -m "feat(capture): add shared CapturedRequest seam"
```

---

### Task 1.2: Header policy

**Files:**
- Create: `N_m3u8DL_RE_GUI.Core/Capture/HeaderPolicy.cs`
- Test: `N_m3u8DL_RE_GUI.Tests/Unit/Core/Capture/HeaderPolicyTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces: `HeaderPolicy.ShouldForward(string name) → bool`. Tasks 1.3 and 2.1 both call it.

**Why this exists:** a browser sends 15–20 headers, most of which are noise or actively harmful to re-send. `accept-encoding: gzip` can make the downloader receive a compressed body it did not ask for. HTTP/2 pseudo-headers (`:authority` and friends, present in the sample HAR) are not real headers and are illegal to set. Filtering in one place keeps both parsers honest.

- [ ] **Step 1: Write the failing tests**

```csharp
#nullable enable
using N_m3u8DL_RE_GUI.Core.Capture;
using Xunit;

namespace N_m3u8DL_RE_GUI.Tests.Unit.Core.Capture;

public class HeaderPolicyTests
{
    [Theory]
    [InlineData("Referer")]
    [InlineData("referer")]
    [InlineData("Origin")]
    [InlineData("User-Agent")]
    [InlineData("Cookie")]
    [InlineData("Authorization")]
    [InlineData("X-Custom-Token")]
    public void ShouldForward_KeepsHeadersThatAffectStreamAccess(string name)
    {
        Assert.True(HeaderPolicy.ShouldForward(name));
    }

    [Theory]
    [InlineData(":authority")]
    [InlineData(":method")]
    [InlineData(":path")]
    [InlineData(":scheme")]
    public void ShouldForward_DropsHttp2PseudoHeaders(string name)
    {
        // These appear verbatim in HAR captures and are illegal to set on a request.
        Assert.False(HeaderPolicy.ShouldForward(name));
    }

    [Theory]
    [InlineData("accept-encoding")]
    [InlineData("Accept-Encoding")]
    [InlineData("content-length")]
    [InlineData("host")]
    [InlineData("connection")]
    [InlineData("priority")]
    [InlineData("dnt")]
    [InlineData("upgrade-insecure-requests")]
    public void ShouldForward_DropsTransportAndNoiseHeaders(string name)
    {
        Assert.False(HeaderPolicy.ShouldForward(name));
    }

    [Theory]
    [InlineData("sec-fetch-dest")]
    [InlineData("sec-fetch-mode")]
    [InlineData("sec-ch-ua")]
    [InlineData("Sec-CH-UA-Platform")]
    public void ShouldForward_DropsTheEntireSecPrefix(string name)
    {
        Assert.False(HeaderPolicy.ShouldForward(name));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ShouldForward_RejectsEmptyNames(string? name)
    {
        Assert.False(HeaderPolicy.ShouldForward(name));
    }
}
```

- [ ] **Step 2: Run and watch it fail**

```bash
dotnet test --filter "FullyQualifiedName~HeaderPolicyTests"
```

Expected: FAIL — `HeaderPolicy` does not exist.

- [ ] **Step 3: Implement**

```csharp
#nullable enable
using System;
using System.Collections.Generic;

namespace N_m3u8DL_RE_GUI.Core.Capture;

/// <summary>
/// Decides which captured headers are worth re-sending. A browser sends far more
/// than a downloader needs, and some of them break it.
/// </summary>
public static class HeaderPolicy
{
    private static readonly HashSet<string> Dropped = new(StringComparer.OrdinalIgnoreCase)
    {
        // Transport-level: the HTTP client owns these.
        "accept-encoding", "content-length", "host", "connection",
        "te", "trailer", "transfer-encoding", "expect", "keep-alive",
        // Navigation hints with no bearing on stream access.
        "priority", "dnt", "upgrade-insecure-requests", "cache-control", "pragma",
    };

    public static bool ShouldForward(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return false;

        var trimmed = name.Trim();

        // HTTP/2 pseudo-headers appear in HAR captures. They are not settable headers.
        if (trimmed.StartsWith(':'))
            return false;

        // sec-fetch-*, sec-ch-ua* — browser fingerprint metadata, pure noise here.
        if (trimmed.StartsWith("sec-", StringComparison.OrdinalIgnoreCase))
            return false;

        return !Dropped.Contains(trimmed);
    }
}
```

- [ ] **Step 4: Run and watch it pass**

```bash
dotnet test --filter "FullyQualifiedName~HeaderPolicyTests"
```

Expected: PASS, 24 tests.

- [ ] **Step 5: Commit**

```bash
git add N_m3u8DL_RE_GUI.Core/Capture/HeaderPolicy.cs N_m3u8DL_RE_GUI.Tests/Unit/Core/Capture/HeaderPolicyTests.cs
git commit -m "feat(capture): add HeaderPolicy filtering pseudo-headers and transport noise"
```

---

### Task 1.3: cURL command parser

**Files:**
- Create: `N_m3u8DL_RE_GUI.Core/Capture/CurlCommandParser.cs`
- Test: `N_m3u8DL_RE_GUI.Tests/Unit/Core/Capture/CurlCommandParserTests.cs`

**Interfaces:**
- Consumes: `CapturedRequest`, `CapturedHeader` (Task 1.1); `HeaderPolicy.ShouldForward` (Task 1.2).
- Produces: `CurlCommandParser.LooksLikeCurl(string?) → bool` and `CurlCommandParser.Parse(string?) → CapturedRequest?`. Task 1.5 (GUI) consumes both.

**Dialects to handle.** All three are generated by shipping browsers:

```
bash     curl 'https://x/a.m3u8' \
           -H 'Referer: https://y/' \
           --compressed

cmd      curl "https://x/a.m3u8" ^
           -H "sec-ch-ua: ^\"Chromium^\";v=^\"151^\"" ^
           --compressed

firefox  curl "https://x/a.m3u8" --compressed -X GET -H "User-Agent: ..."
```

Line continuation is `\` (bash) or `^` (cmd). Bash escapes an embedded single quote as `'\''`. cmd escapes an embedded double quote as `^\"` — cmd consumes the `^`, then curl's own parser turns `\"` into `"`.

- [ ] **Step 1: Write the failing tests**

```csharp
#nullable enable
using System.Linq;
using N_m3u8DL_RE_GUI.Core.Capture;
using Xunit;

namespace N_m3u8DL_RE_GUI.Tests.Unit.Core.Capture;

public class CurlCommandParserTests
{
    [Theory]
    [InlineData("curl 'https://example.com/a.m3u8'")]
    [InlineData("  curl https://example.com/a.m3u8")]
    [InlineData("CURL 'https://example.com/a.m3u8'")]
    public void LooksLikeCurl_AcceptsCommandsRegardlessOfCaseAndLeadingSpace(string text)
    {
        Assert.True(CurlCommandParser.LooksLikeCurl(text));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("https://example.com/a.m3u8")]
    [InlineData("curling is a sport")]
    public void LooksLikeCurl_RejectsAnythingElse(string? text)
    {
        Assert.False(CurlCommandParser.LooksLikeCurl(text));
    }

    [Fact]
    public void Parse_BashDialect_ExtractsUrlAndHeaders()
    {
        const string command = """
            curl 'https://cdn.example.com/hls/master.m3u8' \
              -H 'Referer: https://player.example.com/' \
              -H 'User-Agent: Mozilla/5.0 (Windows NT 10.0; Win64; x64)' \
              --compressed
            """;

        var result = CurlCommandParser.Parse(command);

        Assert.NotNull(result);
        Assert.Equal("https://cdn.example.com/hls/master.m3u8", result!.Url);
        Assert.Equal(CapturedStreamKind.Hls, result.Kind);
        Assert.Equal(2, result.Headers.Count);
        Assert.Contains(result.Headers, h => h.Name == "Referer" && h.Value == "https://player.example.com/");
    }

    [Fact]
    public void Parse_CmdDialect_UnwrapsCaretAndBackslashEscapes()
    {
        const string command = "curl \"https://cdn.example.com/a.m3u8\" ^\r\n  -H \"X-Token: ^\\\"abc^\\\"\"";

        var result = CurlCommandParser.Parse(command);

        Assert.NotNull(result);
        Assert.Equal("https://cdn.example.com/a.m3u8", result!.Url);
        Assert.Contains(result.Headers, h => h.Name == "X-Token" && h.Value == "\"abc\"");
    }

    [Fact]
    public void Parse_BashEscapedSingleQuote_IsReassembledIntoOneToken()
    {
        // 'a'\''b' is the bash idiom for the literal a'b
        const string command = @"curl 'https://example.com/a.m3u8' -H 'X-N: a'\''b'";

        var result = CurlCommandParser.Parse(command);

        Assert.Contains(result!.Headers, h => h.Name == "X-N" && h.Value == "a'b");
    }

    [Fact]
    public void Parse_AppliesHeaderPolicy()
    {
        const string command = """
            curl 'https://example.com/a.m3u8' \
              -H 'sec-fetch-dest: empty' \
              -H 'accept-encoding: gzip, deflate, br' \
              -H 'Referer: https://example.com/'
            """;

        var result = CurlCommandParser.Parse(command);

        Assert.Single(result!.Headers);
        Assert.Equal("Referer", result.Headers[0].Name);
    }

    [Fact]
    public void Parse_LongFormHeaderFlag_IsSupported()
    {
        var result = CurlCommandParser.Parse(
            "curl 'https://example.com/a.m3u8' --header 'Referer: https://example.com/'");

        Assert.Single(result!.Headers);
    }

    [Fact]
    public void Parse_CookieFlag_BecomesACookieHeader()
    {
        var result = CurlCommandParser.Parse(
            "curl 'https://example.com/a.m3u8' -b 'session=abc; theme=dark'");

        Assert.Contains(result!.Headers, h => h.Name == "Cookie" && h.Value == "session=abc; theme=dark");
    }

    [Fact]
    public void Parse_ExplicitCookieHeaderIsNotDuplicatedByCookieFlag()
    {
        var result = CurlCommandParser.Parse(
            "curl 'https://example.com/a.m3u8' -H 'Cookie: a=1' -b 'b=2'");

        Assert.Single(result!.Headers, h => h.Name == "Cookie");
    }

    [Theory]
    [InlineData("curl 'https://example.com/manifest.mpd'", CapturedStreamKind.Dash)]
    [InlineData("curl 'https://example.com/master.m3u8?token=x'", CapturedStreamKind.Hls)]
    [InlineData("curl 'https://example.com/video.mp4'", CapturedStreamKind.Media)]
    [InlineData("curl 'https://example.com/page'", CapturedStreamKind.Unknown)]
    public void Parse_ClassifiesByUrlPathIgnoringQuery(string command, CapturedStreamKind expected)
    {
        Assert.Equal(expected, CurlCommandParser.Parse(command)!.Kind);
    }

    [Fact]
    public void Parse_SkipsFlagValuesWhenLookingForTheUrl()
    {
        // -X GET must not make "GET" a URL candidate, and the URL comes after it.
        var result = CurlCommandParser.Parse(
            "curl -X GET --compressed 'https://example.com/a.m3u8'");

        Assert.Equal("https://example.com/a.m3u8", result!.Url);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("curl --compressed")]
    [InlineData("curl 'ftp://example.com/a.m3u8'")]
    [InlineData("not a curl command at all")]
    public void Parse_ReturnsNullWhenThereIsNoUsableHttpUrl(string? command)
    {
        Assert.Null(CurlCommandParser.Parse(command));
    }

    [Fact]
    public void Parse_MalformedHeaderWithoutColon_IsIgnoredNotCrashed()
    {
        var result = CurlCommandParser.Parse(
            "curl 'https://example.com/a.m3u8' -H 'GarbageWithNoColon'");

        Assert.NotNull(result);
        Assert.Empty(result!.Headers);
    }

    [Fact]
    public void Parse_TrailingHeaderFlagWithNoValue_IsIgnoredNotCrashed()
    {
        var result = CurlCommandParser.Parse("curl 'https://example.com/a.m3u8' -H");

        Assert.NotNull(result);
        Assert.Empty(result!.Headers);
    }
}
```

- [ ] **Step 2: Run and watch it fail**

```bash
dotnet test --filter "FullyQualifiedName~CurlCommandParserTests"
```

Expected: FAIL — `CurlCommandParser` does not exist.

- [ ] **Step 3: Implement**

```csharp
#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace N_m3u8DL_RE_GUI.Core.Capture;

/// <summary>
/// Parses a "Copy as cURL" command from browser devtools into a CapturedRequest.
///
/// Handles the three dialects shipping browsers emit: bash (single quotes,
/// backslash continuation), cmd (double quotes, caret continuation and caret
/// escapes), and Firefox (double quotes, no continuation).
///
/// ponytail: heuristic tokenizer aimed at generated commands, not a POSIX shell
/// parser. It does not evaluate variables, subshells, or redirection — a
/// hand-written command using those will parse oddly. Upgrade path if that ever
/// matters: a real shell-word splitter.
/// </summary>
public static class CurlCommandParser
{
    /// <summary>Flags that consume the following token, so it is never the URL.</summary>
    private static readonly HashSet<string> ValueTakingFlags = new(StringComparer.Ordinal)
    {
        "-H", "--header", "-b", "--cookie", "-X", "--request",
        "-d", "--data", "--data-raw", "--data-binary", "--data-urlencode",
        "-A", "--user-agent", "-e", "--referer", "-u", "--user",
        "--url", "-o", "--output", "--connect-timeout", "--max-time", "-m",
    };

    public static bool LooksLikeCurl(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return false;

        var trimmed = text.TrimStart();
        if (!trimmed.StartsWith("curl", StringComparison.OrdinalIgnoreCase))
            return false;

        // "curling is a sport" must not match; require a delimiter after the verb.
        return trimmed.Length == 4 || char.IsWhiteSpace(trimmed[4]);
    }

    public static CapturedRequest? Parse(string? text)
    {
        if (!LooksLikeCurl(text))
            return null;

        var tokens = Tokenize(text!);
        string? url = null;
        var headers = new List<CapturedHeader>();
        string? cookieFlagValue = null;

        for (var i = 0; i < tokens.Count; i++)
        {
            var token = tokens[i];

            if (token.Equals("curl", StringComparison.OrdinalIgnoreCase) && url is null && i == 0)
                continue;

            if (token is "-H" or "--header")
            {
                if (i + 1 < tokens.Count)
                    AddHeader(headers, tokens[++i]);
                continue;
            }

            if (token is "-b" or "--cookie")
            {
                if (i + 1 < tokens.Count)
                    cookieFlagValue = tokens[++i];
                continue;
            }

            if (token is "-A" or "--user-agent")
            {
                if (i + 1 < tokens.Count)
                    AddHeader(headers, $"User-Agent: {tokens[++i]}");
                continue;
            }

            if (token is "-e" or "--referer")
            {
                if (i + 1 < tokens.Count)
                    AddHeader(headers, $"Referer: {tokens[++i]}");
                continue;
            }

            if (token is "--url")
            {
                if (i + 1 < tokens.Count && IsHttpUrl(tokens[i + 1]))
                    url = tokens[++i];
                continue;
            }

            if (ValueTakingFlags.Contains(token))
            {
                i++; // swallow the value so it is never mistaken for the URL
                continue;
            }

            if (token.StartsWith('-'))
                continue;

            url ??= IsHttpUrl(token) ? token : null;
        }

        if (url is null)
            return null;

        // -b only applies when no explicit Cookie header was given.
        if (cookieFlagValue is not null &&
            !headers.Any(h => h.Name.Equals("Cookie", StringComparison.OrdinalIgnoreCase)))
        {
            AddHeader(headers, $"Cookie: {cookieFlagValue}");
        }

        return new CapturedRequest(url, headers, ClassifyUrl(url));
    }

    private static void AddHeader(List<CapturedHeader> headers, string raw)
    {
        var separator = raw.IndexOf(':');
        if (separator <= 0)
            return; // no colon, or a leading colon (pseudo-header) — nothing usable

        var name = raw[..separator].Trim();
        var value = raw[(separator + 1)..].Trim();

        if (!HeaderPolicy.ShouldForward(name) || value.Length == 0)
            return;

        headers.Add(new CapturedHeader(name, value));
    }

    private static bool IsHttpUrl(string token) =>
        Uri.TryCreate(token, UriKind.Absolute, out var uri) &&
        (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);

    /// <summary>Classifies by URL path only. Query strings routinely carry tokens
    /// ending in ".mp4" and would produce false positives.</summary>
    internal static CapturedStreamKind ClassifyUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return CapturedStreamKind.Unknown;

        var path = uri.AbsolutePath;

        if (path.EndsWith(".m3u8", StringComparison.OrdinalIgnoreCase) ||
            path.EndsWith(".m3u", StringComparison.OrdinalIgnoreCase))
            return CapturedStreamKind.Hls;

        if (path.EndsWith(".mpd", StringComparison.OrdinalIgnoreCase))
            return CapturedStreamKind.Dash;

        if (path.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase) ||
            path.EndsWith(".webm", StringComparison.OrdinalIgnoreCase) ||
            path.EndsWith(".mkv", StringComparison.OrdinalIgnoreCase))
            return CapturedStreamKind.Media;

        return CapturedStreamKind.Unknown;
    }

    /// <summary>
    /// Splits a generated shell command into arguments. Adjacent quoted runs join
    /// into one token, which is what makes the bash 'a'\''b' idiom work.
    /// </summary>
    internal static List<string> Tokenize(string input)
    {
        var tokens = new List<string>();
        var current = new StringBuilder();
        var hasToken = false;
        var i = 0;

        while (i < input.Length)
        {
            var c = input[i];

            // Line continuation: backslash (bash) or caret (cmd) before a newline.
            if ((c == '\\' || c == '^') && i + 1 < input.Length &&
                (input[i + 1] == '\n' || input[i + 1] == '\r'))
            {
                i++;
                while (i < input.Length && (input[i] == '\r' || input[i] == '\n'))
                    i++;
                continue;
            }

            if (char.IsWhiteSpace(c))
            {
                if (hasToken)
                {
                    tokens.Add(current.ToString());
                    current.Clear();
                    hasToken = false;
                }
                i++;
                continue;
            }

            if (c == '\'')
            {
                hasToken = true;
                i++;
                while (i < input.Length && input[i] != '\'')
                    current.Append(input[i++]);
                i++; // closing quote
                continue;
            }

            if (c == '"')
            {
                hasToken = true;
                i++;
                while (i < input.Length && input[i] != '"')
                {
                    // cmd's escape char: drop it and re-examine what follows.
                    if (input[i] == '^' && i + 1 < input.Length)
                    {
                        i++;
                        continue;
                    }
                    if (input[i] == '\\' && i + 1 < input.Length)
                    {
                        current.Append(input[i + 1]);
                        i += 2;
                        continue;
                    }
                    current.Append(input[i++]);
                }
                i++; // closing quote
                continue;
            }

            if ((c == '\\' || c == '^') && i + 1 < input.Length)
            {
                current.Append(input[i + 1]);
                i += 2;
                hasToken = true;
                continue;
            }

            current.Append(c);
            hasToken = true;
            i++;
        }

        if (hasToken)
            tokens.Add(current.ToString());

        return tokens;
    }
}
```

- [ ] **Step 4: Run and watch it pass**

```bash
dotnet test --filter "FullyQualifiedName~CurlCommandParserTests"
```

Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add N_m3u8DL_RE_GUI.Core/Capture/CurlCommandParser.cs N_m3u8DL_RE_GUI.Tests/Unit/Core/Capture/CurlCommandParserTests.cs
git commit -m "feat(capture): parse Copy-as-cURL commands from browser devtools"
```

---

### Task 1.4: Make the header field lossless

**Files:**
- Modify: `N_m3u8DL_RE_GUI.Core/ArgsBuilder.cs:36-45`
- Modify: `N_m3u8DL_RE_GUI/MainWindow.xaml` (`TextBox_Headers`)
- Test: `N_m3u8DL_RE_GUI.Tests/Unit/Core/ArgsBuilderTests.cs`

**Interfaces:**
- Produces: `DownloadOptions.Headers` now accepts `|`-separated (legacy) **or** newline-separated (new) values. Task 1.5 relies on newline separation.

**Why:** `ArgsBuilder` splits headers on `|`. A cookie value containing `|` silently splits into two broken headers — the same class of data-loss bug already fixed for `config.txt` via `LegacyConfigCodec`. Auto-filling real captured cookies will start exercising this hard. Newline cannot appear in an HTTP header value, so accepting it as a separator is lossless. Keeping `|` preserves every existing config.

- [ ] **Step 1: Write the failing tests**

```csharp
[Fact]
public void Build_HeadersSeparatedByNewline_ProducesOneFlagEach()
{
    var options = new DownloadOptions
    {
        Input = "https://example.com/a.m3u8",
        Headers = "Referer: https://example.com/\nUser-Agent: Mozilla/5.0"
    };

    var args = ArgsBuilder.Build(options);

    Assert.Contains("-H\"Referer: https://example.com/\"", args);
    Assert.Contains("-H\"User-Agent: Mozilla/5.0\"", args);
}

[Fact]
public void Build_HeadersSeparatedByPipe_StillWorkForExistingConfigs()
{
    var options = new DownloadOptions
    {
        Input = "https://example.com/a.m3u8",
        Headers = "Referer: https://example.com/|User-Agent: Mozilla/5.0"
    };

    var args = ArgsBuilder.Build(options);

    Assert.Contains("-H\"Referer: https://example.com/\"", args);
    Assert.Contains("-H\"User-Agent: Mozilla/5.0\"", args);
}

[Fact]
public void Build_CrLfSeparatedHeaders_DoNotProduceEmptyFlags()
{
    var options = new DownloadOptions
    {
        Input = "https://example.com/a.m3u8",
        Headers = "Referer: https://example.com/\r\nUser-Agent: Mozilla/5.0"
    };

    var args = ArgsBuilder.Build(options);

    Assert.DoesNotContain("-H\"\"", args);
    Assert.Equal(2, args.Split("-H\"").Length - 1);
}
```

> Check the exact spacing `AppendQuoted` produces before asserting — if it emits `-H "value"` with a space, adjust the expected strings to match.

- [ ] **Step 2: Run and watch the newline cases fail**

```bash
dotnet test --filter "FullyQualifiedName~ArgsBuilderTests"
```

Expected: the two newline tests FAIL; the pipe test PASSES.

- [ ] **Step 3: Widen the separator**

In `ArgsBuilder.cs:38`, change:

```csharp
var headers = options.Headers.Split('|');
```

to:

```csharp
// '|' is the legacy separator kept for existing configs. Newline is the lossless
// one — it cannot occur inside an HTTP header value, unlike '|'.
var headers = options.Headers.Split(
    new[] { '|', '\n', '\r' },
    StringSplitOptions.RemoveEmptyEntries);
```

- [ ] **Step 4: Make the textbox multi-line**

In `MainWindow.xaml`, on `TextBox_Headers`, add:

```xml
AcceptsReturn="True"
TextWrapping="Wrap"
VerticalScrollBarVisibility="Auto"
MinHeight="72"
```

Keep every existing attribute, including `AutomationProperties.Name`.

- [ ] **Step 5: Run the full suite**

```bash
dotnet test
```

Expected: all pass, including the existing XAML accessibility and contrast tests.

- [ ] **Step 6: Commit**

```bash
git add N_m3u8DL_RE_GUI.Core/ArgsBuilder.cs N_m3u8DL_RE_GUI/MainWindow.xaml N_m3u8DL_RE_GUI.Tests/Unit/Core/ArgsBuilderTests.cs
git commit -m "feat(args): accept newline-separated headers losslessly

'|' cannot survive a header value that contains one. Newline can never appear
inside an HTTP header value, so it round-trips. '|' stays supported so existing
configs keep loading."
```

---

### Task 1.5: Wire the paste into the GUI

**Files:**
- Modify: `N_m3u8DL_RE_GUI/MainWindow.xaml` (button next to the URL field)
- Modify: `N_m3u8DL_RE_GUI/MainWindow.xaml.cs`
- Test: `N_m3u8DL_RE_GUI.Tests/Unit/UI/XamlAccessibilityTests.cs` (extend the existing per-element sweep)

**Interfaces:**
- Consumes: `CurlCommandParser.LooksLikeCurl`, `CurlCommandParser.Parse`, `CapturedRequest.ToHeaderLines` (Tasks 1.1, 1.3).

**UX decision.** The button is primary because it is discoverable; a user who has never heard of this feature must be able to find it. Paste-detection on `TextBox_URL` is five extra lines riding the same parser and catches the user who pastes out of habit. Both, because the second is nearly free — and neither is a modal, because a modal for a routine action is friction.

Feedback goes to the existing Zone D status strip, not a dialog: `Imported from cURL — 1 URL, 4 headers`.

- [ ] **Step 1: Add the button to the XAML**

Place beside the URL field, matching the existing `SecondaryButton` style:

```xml
<Button x:Name="Button_PasteCurl"
        Style="{StaticResource SecondaryButtonStyle}"
        Content="📋 Paste from browser"
        Click="Button_PasteCurl_Click"
        AutomationProperties.Name="Paste stream URL and headers from a copied cURL command"
        ToolTip="In your browser press F12 → Network → right-click the stream request → Copy → Copy as cURL, then click here."
        Margin="8,0,0,0"/>
```

- [ ] **Step 2: Write the failing accessibility assertion**

The existing `EveryInteractiveControl_ShouldHaveAnAccessibleName` sweep covers this automatically once the button exists — run it and confirm it passes with the name above. Then add an explicit guard so the wiring cannot silently regress:

```csharp
[Fact]
public void PasteCurlButton_ShouldBeWiredToItsHandler()
{
    var xaml = File.ReadAllText(MainWindowXamlPath);

    Assert.Contains("x:Name=\"Button_PasteCurl\"", xaml);
    Assert.Contains("Click=\"Button_PasteCurl_Click\"", xaml);
}
```

> `MainWindowXamlPath` already exists in this test class — reuse it rather than recomputing the path.

- [ ] **Step 3: Run and watch it fail**

```bash
dotnet test --filter "FullyQualifiedName~XamlAccessibilityTests"
```

Expected: FAIL until the handler and button both exist.

- [ ] **Step 4: Implement the handler**

In `MainWindow.xaml.cs`, near the other click handlers:

```csharp
private void Button_PasteCurl_Click(object sender, RoutedEventArgs e)
{
    string clipboardText;
    try
    {
        clipboardText = System.Windows.Clipboard.GetText();
    }
    catch (Exception ex)
    {
        // The clipboard is a shared OS resource; another process can hold it locked.
        SetStatus($"Could not read the clipboard: {ex.Message}");
        return;
    }

    if (!TryApplyCapturedRequest(CurlCommandParser.Parse(clipboardText)))
    {
        SetStatus("Clipboard does not contain a cURL command with an http(s) URL. " +
                  "In your browser: F12 → Network → right-click the request → Copy as cURL.");
    }
}

/// <summary>
/// Fills the URL and header fields from a capture. Returns false when there was
/// nothing usable, so callers can report it their own way.
/// </summary>
private bool TryApplyCapturedRequest(CapturedRequest? captured)
{
    if (captured is null)
        return false;

    TextBox_URL.Text = captured.Url;

    if (captured.Headers.Count > 0)
        TextBox_Headers.Text = captured.ToHeaderLines();

    var kind = captured.Kind == CapturedStreamKind.Unknown
        ? "stream"
        : captured.Kind.ToString().ToUpperInvariant();

    SetStatus($"Imported {kind} — 1 URL, {captured.Headers.Count} header(s).");
    return true;
}
```

> `SetStatus` is the existing Zone D status writer. If the method has a different name in this file, use the real one — do not add a second status path.

- [ ] **Step 5: Add paste-detection on the URL box**

```csharp
private void TextBox_URL_Pasting(object sender, DataObjectPastingEventArgs e)
{
    if (!e.DataObject.GetDataPresent(DataFormats.UnicodeText))
        return;

    var pasted = e.DataObject.GetData(DataFormats.UnicodeText) as string;
    if (!CurlCommandParser.LooksLikeCurl(pasted))
        return;

    if (TryApplyCapturedRequest(CurlCommandParser.Parse(pasted)))
        e.CancelCommand(); // we already placed the URL; stop the raw command landing in the box
}
```

Register it in the constructor, after `InitializeComponent()`:

```csharp
DataObject.AddPastingHandler(TextBox_URL, TextBox_URL_Pasting);
```

- [ ] **Step 6: Run everything**

```bash
dotnet build && dotnet test
```

Expected: 0 warnings, 0 errors, all tests pass.

- [ ] **Step 7: Manual verification**

1. Open any site with an HLS player in Chrome or Edge.
2. F12 → Network → filter `m3u8` → right-click the manifest → Copy → Copy as cURL (bash).
3. In the GUI click **📋 Paste from browser**.
4. Confirm: the URL field holds the manifest URL, the header field holds `Referer`/`User-Agent`/`Cookie` on separate lines, no `sec-*` or `accept-encoding` lines are present, and the status strip reports the count.
5. Repeat with **Copy as cURL (cmd)** and confirm the caret escapes came out clean.

- [ ] **Step 8: Commit**

```bash
git add N_m3u8DL_RE_GUI/MainWindow.xaml N_m3u8DL_RE_GUI/MainWindow.xaml.cs N_m3u8DL_RE_GUI.Tests/Unit/UI/XamlAccessibilityTests.cs
git commit -m "feat(ui): paste a browser cURL command to fill URL and headers"
```

**Phase 1 is independently shippable here.** Everything below adds reach, not correctness.

---

## Phase 2 — HAR drop

For the case Phase 1 cannot serve: the user cannot tell which request is the stream.

### Task 2.1: HAR extractor

**Files:**
- Create: `N_m3u8DL_RE_GUI.Core/Capture/HarStreamExtractor.cs`
- Create: `N_m3u8DL_RE_GUI.Tests/Fixtures/Har/hls-with-segments.har`
- Create: `N_m3u8DL_RE_GUI.Tests/Fixtures/Har/progressive-mp4-ranges.har`
- Create: `N_m3u8DL_RE_GUI.Tests/Fixtures/Har/no-streams.har`
- Test: `N_m3u8DL_RE_GUI.Tests/Unit/Core/Capture/HarStreamExtractorTests.cs`

**Interfaces:**
- Consumes: `CapturedRequest`, `CapturedStreamKind`, `HeaderPolicy` (Tasks 1.1, 1.2); `CurlCommandParser.ClassifyUrl` (Task 1.3, `internal` — the test project already has `InternalsVisibleTo`; if it does not, make `ClassifyUrl` public instead).
- Produces: `HarStreamExtractor.ExtractFromFile(string path) → IReadOnlyList<CapturedRequest>` and `HarStreamExtractor.Extract(Stream) → IReadOnlyList<CapturedRequest>`, ranked best-first. Tasks 2.2 and 2.3 consume it.

**The real algorithmic problem.** A live HLS capture contains one manifest and hundreds of segments. Matching `video/*` returns the segments. Rules, in order:

1. Classify by **URL path** (query stripped — query strings carry tokens that end in `.mp4`).
2. Fall back to **response `mimeType`** (`*mpegurl*` → HLS, `*dash+xml*` → DASH).
3. **Explicitly exclude segment extensions** even when the mime says `video/*`: `.ts .m4s .aac .mp3 .vtt .cmfv .cmfa .cmft .init .key`.
4. Progressive media only counts at status **200 or 206**.
5. **Deduplicate by exact URL**, keeping the first occurrence's headers — this collapses the range requests a `<video>` element fires.
6. Rank: HLS and DASH first in first-seen order, then Media in first-seen order. A master playlist is normally requested before its variants, so first-seen is the right default.

Do not try to be cleverer than that. Show the list and let the user pick; a good default beats a heuristic that is confidently wrong.

**Size and secrecy.** Cap at 256 MB with a clear error. Never read `response.content.text` — that is where HAR keeps response bodies, including anything that was in a login response. Never log any parsed field.

- [ ] **Step 1: Create the fixtures**

`hls-with-segments.har` — a master, a variant, three `.ts` segments, one image, and one HTTP/2 pseudo-header set on the master:

```json
{
  "log": {
    "version": "1.2",
    "creator": { "name": "test", "version": "1" },
    "entries": [
      {
        "request": {
          "method": "GET",
          "url": "https://cdn.example.com/hls/master.m3u8",
          "headers": [
            { "name": ":authority", "value": "cdn.example.com" },
            { "name": ":method", "value": "GET" },
            { "name": "Referer", "value": "https://player.example.com/" },
            { "name": "accept-encoding", "value": "gzip, deflate, br" },
            { "name": "sec-fetch-dest", "value": "empty" },
            { "name": "User-Agent", "value": "Mozilla/5.0" }
          ]
        },
        "response": { "status": 200, "content": { "mimeType": "application/vnd.apple.mpegurl", "size": 512 } }
      },
      {
        "request": { "method": "GET", "url": "https://cdn.example.com/hls/1080p/index.m3u8", "headers": [] },
        "response": { "status": 200, "content": { "mimeType": "application/vnd.apple.mpegurl", "size": 2048 } }
      },
      {
        "request": { "method": "GET", "url": "https://cdn.example.com/hls/1080p/seg_00001.ts", "headers": [] },
        "response": { "status": 200, "content": { "mimeType": "video/mp2t", "size": 1048576 } }
      },
      {
        "request": { "method": "GET", "url": "https://cdn.example.com/hls/1080p/seg_00002.ts", "headers": [] },
        "response": { "status": 200, "content": { "mimeType": "video/mp2t", "size": 1048576 } }
      },
      {
        "request": { "method": "GET", "url": "https://cdn.example.com/hls/1080p/seg_00003.ts", "headers": [] },
        "response": { "status": 200, "content": { "mimeType": "video/mp2t", "size": 1048576 } }
      },
      {
        "request": { "method": "GET", "url": "https://cdn.example.com/poster.jpg", "headers": [] },
        "response": { "status": 200, "content": { "mimeType": "image/jpeg", "size": 40960 } }
      }
    ]
  }
}
```

`progressive-mp4-ranges.har` — the same MP4 URL three times at 206, plus one unrelated 404:

```json
{
  "log": {
    "version": "1.2",
    "creator": { "name": "test", "version": "1" },
    "entries": [
      {
        "request": { "method": "GET", "url": "https://cdn.example.com/video/movie.mp4", "headers": [ { "name": "Referer", "value": "https://site.example.com/" } ] },
        "response": { "status": 206, "content": { "mimeType": "video/mp4", "size": 1048576 } }
      },
      {
        "request": { "method": "GET", "url": "https://cdn.example.com/video/movie.mp4", "headers": [] },
        "response": { "status": 206, "content": { "mimeType": "video/mp4", "size": 1048576 } }
      },
      {
        "request": { "method": "GET", "url": "https://cdn.example.com/video/movie.mp4", "headers": [] },
        "response": { "status": 206, "content": { "mimeType": "video/mp4", "size": 1048576 } }
      },
      {
        "request": { "method": "GET", "url": "https://cdn.example.com/missing.mp4", "headers": [] },
        "response": { "status": 404, "content": { "mimeType": "application/json", "size": 28 } }
      }
    ]
  }
}
```

`no-streams.har` — scripts, stylesheets and images only, mirroring what the fairyanime sample actually contained:

```json
{
  "log": {
    "version": "1.2",
    "creator": { "name": "test", "version": "1" },
    "entries": [
      {
        "request": { "method": "GET", "url": "https://cdn.example.com/player.js", "headers": [] },
        "response": { "status": 200, "content": { "mimeType": "application/javascript", "size": 90000 } }
      },
      {
        "request": { "method": "GET", "url": "https://cdn.example.com/style.css", "headers": [] },
        "response": { "status": 200, "content": { "mimeType": "text/css", "size": 4096 } }
      }
    ]
  }
}
```

Mark all three as copied to output in `N_m3u8DL_RE_GUI.Tests.csproj`:

```xml
<ItemGroup>
  <None Update="Fixtures\Har\*.har" CopyToOutputDirectory="PreserveNewest" />
</ItemGroup>
```

- [ ] **Step 2: Write the failing tests**

```csharp
#nullable enable
using System;
using System.IO;
using System.Linq;
using System.Text;
using N_m3u8DL_RE_GUI.Core.Capture;
using Xunit;

namespace N_m3u8DL_RE_GUI.Tests.Unit.Core.Capture;

public class HarStreamExtractorTests
{
    private static string Fixture(string name) =>
        Path.Combine(AppContext.BaseDirectory, "Fixtures", "Har", name);

    [Fact]
    public void Extract_HlsCapture_ReturnsManifestsAndNeverSegments()
    {
        var results = HarStreamExtractor.ExtractFromFile(Fixture("hls-with-segments.har"));

        Assert.Equal(2, results.Count);
        Assert.All(results, r => Assert.Equal(CapturedStreamKind.Hls, r.Kind));
        Assert.DoesNotContain(results, r => r.Url.Contains(".ts", StringComparison.Ordinal));
    }

    [Fact]
    public void Extract_HlsCapture_RanksTheMasterFirst()
    {
        // The master is requested before its variants, so first-seen order is correct.
        var results = HarStreamExtractor.ExtractFromFile(Fixture("hls-with-segments.har"));

        Assert.Equal("https://cdn.example.com/hls/master.m3u8", results[0].Url);
    }

    [Fact]
    public void Extract_AppliesHeaderPolicyToCapturedHeaders()
    {
        var master = HarStreamExtractor.ExtractFromFile(Fixture("hls-with-segments.har"))[0];

        Assert.Contains(master.Headers, h => h.Name == "Referer");
        Assert.Contains(master.Headers, h => h.Name == "User-Agent");
        Assert.DoesNotContain(master.Headers, h => h.Name.StartsWith(':'));
        Assert.DoesNotContain(master.Headers, h => h.Name.StartsWith("sec-", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(master.Headers, h => h.Name.Equals("accept-encoding", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Extract_RangeRequestsForOneFile_CollapseToASingleEntry()
    {
        var results = HarStreamExtractor.ExtractFromFile(Fixture("progressive-mp4-ranges.har"));

        Assert.Single(results);
        Assert.Equal("https://cdn.example.com/video/movie.mp4", results[0].Url);
        Assert.Equal(CapturedStreamKind.Media, results[0].Kind);
    }

    [Fact]
    public void Extract_DedupeKeepsTheFirstOccurrencesHeaders()
    {
        var results = HarStreamExtractor.ExtractFromFile(Fixture("progressive-mp4-ranges.har"));

        Assert.Contains(results[0].Headers, h => h.Name == "Referer");
    }

    [Fact]
    public void Extract_FailedResponses_AreNotOffered()
    {
        var results = HarStreamExtractor.ExtractFromFile(Fixture("progressive-mp4-ranges.har"));

        Assert.DoesNotContain(results, r => r.Url.Contains("missing.mp4", StringComparison.Ordinal));
    }

    [Fact]
    public void Extract_CaptureWithNoMedia_ReturnsEmptyRatherThanThrowing()
    {
        Assert.Empty(HarStreamExtractor.ExtractFromFile(Fixture("no-streams.har")));
    }

    [Fact]
    public void Extract_MalformedJson_ThrowsInvalidDataWithAReadableMessage()
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("{ not json"));

        var exception = Assert.Throws<InvalidDataException>(() => HarStreamExtractor.Extract(stream));
        Assert.Contains("HAR", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Extract_JsonThatIsNotAHar_ThrowsInvalidData()
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("""{"hello":"world"}"""));

        Assert.Throws<InvalidDataException>(() => HarStreamExtractor.Extract(stream));
    }

    [Fact]
    public void Extract_EntryMissingResponse_IsSkippedNotFatal()
    {
        const string har = """
            { "log": { "entries": [
              { "request": { "url": "https://cdn.example.com/a.m3u8", "headers": [] } },
              { "request": { "url": "https://cdn.example.com/b.m3u8", "headers": [] },
                "response": { "status": 200, "content": { "mimeType": "application/vnd.apple.mpegurl" } } }
            ] } }
            """;
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(har));

        var results = HarStreamExtractor.Extract(stream);

        Assert.Single(results);
        Assert.Equal("https://cdn.example.com/b.m3u8", results[0].Url);
    }

    [Fact]
    public void Extract_ClassifiesByMimeTypeWhenTheUrlHasNoExtension()
    {
        const string har = """
            { "log": { "entries": [
              { "request": { "url": "https://cdn.example.com/manifest?id=42", "headers": [] },
                "response": { "status": 200, "content": { "mimeType": "application/dash+xml" } } }
            ] } }
            """;
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(har));

        var results = HarStreamExtractor.Extract(stream);

        Assert.Single(results);
        Assert.Equal(CapturedStreamKind.Dash, results[0].Kind);
    }

    [Fact]
    public void ExtractFromFile_OverTheSizeCap_ThrowsBeforeParsing()
    {
        var path = Path.Combine(Path.GetTempPath(), $"huge_{Guid.NewGuid():N}.har");
        try
        {
            // Create a sparse file past the cap without writing 256 MB.
            using (var fs = new FileStream(path, FileMode.CreateNew))
                fs.SetLength(HarStreamExtractor.MaxFileBytes + 1);

            Assert.Throws<InvalidDataException>(() => HarStreamExtractor.ExtractFromFile(path));
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }
}
```

- [ ] **Step 3: Run and watch it fail**

```bash
dotnet test --filter "FullyQualifiedName~HarStreamExtractorTests"
```

Expected: FAIL — `HarStreamExtractor` does not exist.

- [ ] **Step 4: Implement**

```csharp
#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace N_m3u8DL_RE_GUI.Core.Capture;

/// <summary>
/// Pulls downloadable stream candidates out of a browser HAR capture.
///
/// A HAR holds everything the browser did, including credentials in request
/// bodies. This type reads only request URLs, request headers, response status
/// and response mimeType — never response bodies — and never logs what it reads.
/// </summary>
public static class HarStreamExtractor
{
    /// <summary>Refuse anything larger. A HAR this big is a mistake, and parsing it
    /// would balloon well past its own size in memory.</summary>
    public const long MaxFileBytes = 256L * 1024 * 1024;

    /// <summary>Media segments. Excluded even when the response says video/*, because
    /// a live capture contains hundreds of them and exactly one manifest.</summary>
    private static readonly HashSet<string> SegmentExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".ts", ".m4s", ".aac", ".mp3", ".vtt", ".cmfv", ".cmfa", ".cmft", ".init", ".key"
    };

    public static IReadOnlyList<CapturedRequest> ExtractFromFile(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("Path is required.", nameof(path));

        var info = new FileInfo(path);
        if (!info.Exists)
            throw new FileNotFoundException("HAR file not found.", path);

        if (info.Length > MaxFileBytes)
        {
            throw new InvalidDataException(
                $"This HAR is {info.Length / (1024 * 1024)} MB, over the " +
                $"{MaxFileBytes / (1024 * 1024)} MB limit. Re-capture with the network " +
                "log cleared just before you press play.");
        }

        using var stream = File.OpenRead(path);
        return Extract(stream);
    }

    public static IReadOnlyList<CapturedRequest> Extract(Stream harStream)
    {
        JsonDocument document;
        try
        {
            // ponytail: JsonDocument buffers the whole file. Fine up to the cap above;
            // upgrade path is a Utf8JsonReader walk if the cap ever needs raising.
            document = JsonDocument.Parse(harStream);
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException(
                "This file is not valid JSON, so it cannot be a HAR capture.", ex);
        }

        using (document)
        {
            if (!document.RootElement.TryGetProperty("log", out var log) ||
                !log.TryGetProperty("entries", out var entries) ||
                entries.ValueKind != JsonValueKind.Array)
            {
                throw new InvalidDataException(
                    "This JSON file has no log.entries array, so it is not a HAR capture.");
            }

            var seen = new HashSet<string>(StringComparer.Ordinal);
            var found = new List<CapturedRequest>();

            foreach (var entry in entries.EnumerateArray())
            {
                var captured = ReadEntry(entry);
                if (captured is null)
                    continue;

                // Collapse the range requests a <video> element fires for one file.
                if (!seen.Add(captured.Url))
                    continue;

                found.Add(captured);
            }

            // Manifests before progressive files; original request order within each
            // group, because a master playlist is fetched before its variants.
            return found
                .Select((request, index) => (request, index))
                .OrderBy(x => x.request.Kind == CapturedStreamKind.Media ? 1 : 0)
                .ThenBy(x => x.index)
                .Select(x => x.request)
                .ToList();
        }
    }

    private static CapturedRequest? ReadEntry(JsonElement entry)
    {
        if (!entry.TryGetProperty("request", out var request) ||
            !request.TryGetProperty("url", out var urlElement) ||
            urlElement.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        var url = urlElement.GetString();
        if (string.IsNullOrWhiteSpace(url))
            return null;

        var status = 0;
        string? mimeType = null;

        if (entry.TryGetProperty("response", out var response))
        {
            if (response.TryGetProperty("status", out var statusElement) &&
                statusElement.ValueKind == JsonValueKind.Number)
            {
                status = statusElement.GetInt32();
            }

            if (response.TryGetProperty("content", out var content) &&
                content.TryGetProperty("mimeType", out var mimeElement) &&
                mimeElement.ValueKind == JsonValueKind.String)
            {
                mimeType = mimeElement.GetString();
            }
        }

        var kind = Classify(url, mimeType, status);
        if (kind == CapturedStreamKind.Unknown)
            return null;

        return new CapturedRequest(url, ReadHeaders(request), kind);
    }

    private static List<CapturedHeader> ReadHeaders(JsonElement request)
    {
        var headers = new List<CapturedHeader>();

        if (!request.TryGetProperty("headers", out var headerArray) ||
            headerArray.ValueKind != JsonValueKind.Array)
        {
            return headers;
        }

        foreach (var header in headerArray.EnumerateArray())
        {
            if (!header.TryGetProperty("name", out var nameElement) ||
                !header.TryGetProperty("value", out var valueElement) ||
                nameElement.ValueKind != JsonValueKind.String ||
                valueElement.ValueKind != JsonValueKind.String)
            {
                continue;
            }

            var name = nameElement.GetString();
            var value = valueElement.GetString();

            if (!HeaderPolicy.ShouldForward(name) || string.IsNullOrWhiteSpace(value))
                continue;

            headers.Add(new CapturedHeader(name!.Trim(), value!.Trim()));
        }

        return headers;
    }

    internal static CapturedStreamKind Classify(string url, string? mimeType, int status)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            return CapturedStreamKind.Unknown;
        }

        var extension = Path.GetExtension(uri.AbsolutePath);

        // Segments first: a .ts served as video/mp2t must never outrank the manifest.
        if (SegmentExtensions.Contains(extension))
            return CapturedStreamKind.Unknown;

        var byUrl = CurlCommandParser.ClassifyUrl(url);
        if (byUrl is CapturedStreamKind.Hls or CapturedStreamKind.Dash)
            return byUrl;

        var mime = mimeType ?? string.Empty;
        if (mime.Contains("mpegurl", StringComparison.OrdinalIgnoreCase))
            return CapturedStreamKind.Hls;
        if (mime.Contains("dash+xml", StringComparison.OrdinalIgnoreCase))
            return CapturedStreamKind.Dash;

        // Progressive media only counts when the server actually served it.
        if (status is 200 or 206)
        {
            if (byUrl == CapturedStreamKind.Media)
                return CapturedStreamKind.Media;
            if (mime.StartsWith("video/", StringComparison.OrdinalIgnoreCase))
                return CapturedStreamKind.Media;
        }

        return CapturedStreamKind.Unknown;
    }
}
```

- [ ] **Step 5: Run and watch it pass**

```bash
dotnet test --filter "FullyQualifiedName~HarStreamExtractorTests"
```

Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add N_m3u8DL_RE_GUI.Core/Capture/HarStreamExtractor.cs N_m3u8DL_RE_GUI.Tests/Unit/Core/Capture/HarStreamExtractorTests.cs N_m3u8DL_RE_GUI.Tests/Fixtures/Har N_m3u8DL_RE_GUI.Tests/N_m3u8DL_RE_GUI.Tests.csproj
git commit -m "feat(capture): extract ranked stream candidates from a HAR capture"
```

---

### Task 2.2: Recognise a HAR path

**Files:**
- Modify: `N_m3u8DL_RE_GUI.Core/DropInputRules.cs`
- Test: `N_m3u8DL_RE_GUI.Tests/Unit/Core/DropInputRulesTests.cs`

**Interfaces:**
- Produces: `DropInputRules.IsHarPath(string? path) → bool`. Task 2.3 branches on it.

**Critical (D3):** do **not** add `.har` to `UrlInputExtensions` or `AutoTitleExtensions`. Those sets mean "hand this path to the downloader as an input". A HAR is a source to extract from.

- [ ] **Step 1: Write the failing tests**

```csharp
[Fact]
public void IsHarPath_AcceptsAnExistingHarFile()
{
    var path = Path.Combine(Path.GetTempPath(), $"c_{Guid.NewGuid():N}.har");
    File.WriteAllText(path, "{}");
    try
    {
        Assert.True(DropInputRules.IsHarPath(path));
    }
    finally { File.Delete(path); }
}

[Fact]
public void IsHarPath_IsCaseInsensitive()
{
    var path = Path.Combine(Path.GetTempPath(), $"c_{Guid.NewGuid():N}.HAR");
    File.WriteAllText(path, "{}");
    try
    {
        Assert.True(DropInputRules.IsHarPath(path));
    }
    finally { File.Delete(path); }
}

[Theory]
[InlineData(null)]
[InlineData("")]
[InlineData(@"C:\does\not\exist.har")]
public void IsHarPath_RejectsMissingOrEmptyPaths(string? path)
{
    Assert.False(DropInputRules.IsHarPath(path));
}

[Fact]
public void HarFile_MustNotBeTreatedAsAStreamInput()
{
    // Regression guard: a .har handed straight to the downloader is a bug.
    var path = Path.Combine(Path.GetTempPath(), $"c_{Guid.NewGuid():N}.har");
    File.WriteAllText(path, "{}");
    try
    {
        Assert.False(DropInputRules.IsSupportedUrlInputPath(path));
        Assert.False(DropInputRules.ShouldAutoFillTitleFromFileName(path));
    }
    finally { File.Delete(path); }
}
```

- [ ] **Step 2: Run and watch it fail**

```bash
dotnet test --filter "FullyQualifiedName~DropInputRulesTests"
```

Expected: the `IsHarPath` tests FAIL (method missing); `HarFile_MustNotBeTreatedAsAStreamInput` should already PASS — if it fails, someone added `.har` to the wrong set.

- [ ] **Step 3: Implement**

Add to `DropInputRules`:

```csharp
/// <summary>
/// True for a browser HAR capture. Deliberately separate from
/// <see cref="IsSupportedUrlInputPath"/>: a HAR is a source to extract a stream
/// URL from, never a stream input to hand to the downloader.
/// </summary>
public static bool IsHarPath(string? path)
{
    if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        return false;

    return Path.GetExtension(path).Equals(".har", StringComparison.OrdinalIgnoreCase);
}
```

- [ ] **Step 4: Run and watch it pass**

```bash
dotnet test --filter "FullyQualifiedName~DropInputRulesTests"
```

- [ ] **Step 5: Commit**

```bash
git add N_m3u8DL_RE_GUI.Core/DropInputRules.cs N_m3u8DL_RE_GUI.Tests/Unit/Core/DropInputRulesTests.cs
git commit -m "feat(drop): recognise .har as a capture source, not a stream input"
```

---

### Task 2.3: Picker window and drop wiring

**Files:**
- Create: `N_m3u8DL_RE_GUI/Views/StreamPickerWindow.xaml`
- Create: `N_m3u8DL_RE_GUI/Views/StreamPickerWindow.xaml.cs`
- Modify: `N_m3u8DL_RE_GUI/MainWindow.xaml.cs:591` (`TextBox_URL_PreviewDrop`)

**Interfaces:**
- Consumes: `HarStreamExtractor.ExtractFromFile`, `DropInputRules.IsHarPath`, `TryApplyCapturedRequest` (Task 1.5).

**Behaviour:** zero candidates → status message explaining how to re-capture. Exactly one → apply it silently, no dialog. Two or more → picker, first row pre-selected.

- [ ] **Step 1: Create the picker XAML**

```xml
<Window x:Class="N_m3u8DL_RE_GUI.Views.StreamPickerWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        Title="Choose a stream"
        Height="360" Width="720"
        WindowStartupLocation="CenterOwner"
        ShowInTaskbar="False"
        Background="{StaticResource CardBrush}">
    <Grid Margin="16">
        <Grid.RowDefinitions>
            <RowDefinition Height="Auto"/>
            <RowDefinition Height="*"/>
            <RowDefinition Height="Auto"/>
        </Grid.RowDefinitions>

        <TextBlock Grid.Row="0"
                   Text="This capture contains more than one stream. Pick the one to download — the first is usually the master playlist."
                   TextWrapping="Wrap"
                   Foreground="{StaticResource TextBrush}"
                   Margin="0,0,0,12"/>

        <ListBox Grid.Row="1"
                 x:Name="List_Candidates"
                 AutomationProperties.Name="Stream candidates found in the capture"
                 MouseDoubleClick="List_Candidates_MouseDoubleClick"
                 HorizontalContentAlignment="Stretch">
            <ListBox.ItemTemplate>
                <DataTemplate>
                    <StackPanel Margin="4">
                        <TextBlock Text="{Binding Kind}" FontWeight="Bold"
                                   Foreground="{StaticResource AccentTextBrush}"/>
                        <TextBlock Text="{Binding Url}" TextTrimming="CharacterEllipsis"
                                   Foreground="{StaticResource TextBrush}"/>
                    </StackPanel>
                </DataTemplate>
            </ListBox.ItemTemplate>
        </ListBox>

        <StackPanel Grid.Row="2" Orientation="Horizontal"
                    HorizontalAlignment="Right" Margin="0,12,0,0">
            <Button x:Name="Button_Cancel" Content="Cancel" IsCancel="True"
                    AutomationProperties.Name="Cancel stream selection"
                    Style="{StaticResource SecondaryButtonStyle}" MinWidth="90"/>
            <Button x:Name="Button_Use" Content="Use this stream" IsDefault="True"
                    AutomationProperties.Name="Use the selected stream"
                    Click="Button_Use_Click"
                    Style="{StaticResource PrimaryButtonStyle}" MinWidth="130" Margin="8,0,0,0"/>
        </StackPanel>
    </Grid>
</Window>
```

> Confirm the real style keys (`CardBrush`, `TextBrush`, `AccentTextBrush`, `PrimaryButtonStyle`, `SecondaryButtonStyle`) against `MainWindow.xaml` and use whatever is actually defined — these must resolve or the window throws at load.

- [ ] **Step 2: Create the code-behind**

```csharp
#nullable enable
using System.Collections.Generic;
using System.Windows;
using N_m3u8DL_RE_GUI.Core.Capture;

namespace N_m3u8DL_RE_GUI.Views;

/// <summary>Modal list shown when a capture yields more than one stream candidate.</summary>
public partial class StreamPickerWindow : Window
{
    public CapturedRequest? Selected { get; private set; }

    public StreamPickerWindow(IReadOnlyList<CapturedRequest> candidates)
    {
        InitializeComponent();
        List_Candidates.ItemsSource = candidates;
        List_Candidates.SelectedIndex = 0;
    }

    private void Button_Use_Click(object sender, RoutedEventArgs e) => Accept();

    private void List_Candidates_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e) => Accept();

    private void Accept()
    {
        Selected = List_Candidates.SelectedItem as CapturedRequest;
        if (Selected is null)
            return;

        DialogResult = true;
        Close();
    }
}
```

- [ ] **Step 3: Add the drop branch — before the existing one**

In `MainWindow.xaml.cs`, in `TextBox_URL_PreviewDrop`, insert the HAR check ahead of the `IsSupportedUrlInputPath` branch:

```csharp
private void TextBox_URL_PreviewDrop(object sender, System.Windows.DragEventArgs e)
{
    if (!TryGetFirstDroppedPath(e, out var path))
        return;

    // Must come first: a .har is a source to extract from, never a stream input.
    if (DropInputRules.IsHarPath(path))
    {
        ImportFromHar(path);
        e.Handled = true;
        return;
    }

    if (DropInputRules.IsSupportedUrlInputPath(path))
    {
        // ... existing body unchanged ...
    }
}

private void ImportFromHar(string path)
{
    IReadOnlyList<CapturedRequest> candidates;
    try
    {
        candidates = HarStreamExtractor.ExtractFromFile(path);
    }
    catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException)
    {
        SetStatus(ex.Message);
        return;
    }

    if (candidates.Count == 0)
    {
        SetStatus("No stream was found in that capture. Clear the network log, press play, " +
                  "let it run a few seconds, then save the HAR again.");
        return;
    }

    if (candidates.Count == 1)
    {
        TryApplyCapturedRequest(candidates[0]);
        return;
    }

    var picker = new Views.StreamPickerWindow(candidates) { Owner = this };
    if (picker.ShowDialog() == true)
        TryApplyCapturedRequest(picker.Selected);
}
```

Also update `TextBox_URL_PreviewDragOver` so a `.har` shows the copy cursor — it already accepts any file drop via `HasFileDropData`, so verify no change is needed rather than assuming one is.

- [ ] **Step 4: Build and run everything**

```bash
dotnet build && dotnet test
```

Expected: 0 warnings, 0 errors, all tests pass including the XAML accessibility sweep over the new window.

- [ ] **Step 5: Manual verification**

1. Capture a HAR from a site with an HLS player: F12 → Network → clear → play → save all as HAR.
2. Drag the `.har` onto the URL box.
3. Single manifest → fields fill with no dialog. Multiple → picker appears with the master pre-selected.
4. Drag `no-streams.har` → the "no stream was found" guidance appears and nothing is overwritten.

- [ ] **Step 6: Commit**

```bash
git add N_m3u8DL_RE_GUI/Views N_m3u8DL_RE_GUI/MainWindow.xaml.cs
git commit -m "feat(ui): drop a .har capture to pick a stream and its headers"
```

---

## Phase 3 — Browser extension

**Zero new C#.** The extension puts a `curl` command on the clipboard; the user clicks the Phase 1 button. Everything below is standalone JavaScript that can be built, tested and shipped without opening the solution.

### Task 3.1: Manifest and service worker

**Files:** `extension/manifest.json`, `extension/background.js`

**MV3 constraints that must be respected (D4):**
- Observational `webRequest` still works in MV3; only *blocking* was removed. Do not add `webRequestBlocking`.
- The service worker is killed after ~30 s idle. **All detected state lives in `chrome.storage.session`**, never in module variables.
- Reading `Cookie` requires `extraHeaders` in the extra-info spec plus host permissions. `<all_urls>` is unavoidable for a tool that must watch arbitrary CDNs, and it produces the "read all your data on all websites" install warning. Say so plainly in the README rather than hiding it.

```json
{
  "manifest_version": 3,
  "name": "N_m3u8DL-RE Companion",
  "version": "1.0.0",
  "description": "Finds video streams on the page you are watching and copies them as a cURL command for N_m3u8DL-RE GUI.",
  "permissions": ["webRequest", "storage", "tabs"],
  "host_permissions": ["<all_urls>"],
  "background": { "service_worker": "background.js" },
  "action": { "default_popup": "popup/popup.html" },
  "icons": { "16": "icons/16.png", "48": "icons/48.png", "128": "icons/128.png" }
}
```

Detection logic must mirror `HarStreamExtractor.Classify` exactly — same segment exclusion list, same status gate, same mime fallbacks. If the two drift, the same capture produces different answers depending on the path the user took.

Per-tab state shape stored under key `streams:<tabId>`:

```javascript
{ url, kind, referer, userAgent, cookie, at }
```

Clear a tab's entry on `chrome.tabs.onUpdated` when the URL changes, and on `chrome.tabs.onRemoved`. Set `chrome.action.setBadgeText` to the candidate count for that tab.

### Task 3.2: Popup

**Files:** `extension/popup/popup.html`, `popup.js`, `popup.css`

Lists candidates for the active tab, manifests first. Each row: kind badge, elided URL, and a **Copy as cURL** button that builds the bash dialect and calls `navigator.clipboard.writeText(...)` — which works in a popup because the click supplies transient user activation.

Emit exactly the header set Phase 1 keeps, so nothing is filtered twice:

```javascript
function toCurl(stream) {
  const q = (s) => `'${String(s).replace(/'/g, `'\\''`)}'`;
  const parts = [`curl ${q(stream.url)}`];
  if (stream.referer)   parts.push(`-H ${q('Referer: ' + stream.referer)}`);
  if (stream.userAgent) parts.push(`-H ${q('User-Agent: ' + stream.userAgent)}`);
  if (stream.cookie)    parts.push(`-H ${q('Cookie: ' + stream.cookie)}`);
  return parts.join(' \\\n  ');
}
```

Note the single-quote escape uses the `'\''` idiom, which Task 1.3 already has a test for — that test is what keeps the two halves compatible.

### Task 3.3: README and packaging

**Files:** `extension/README.md`, plus a section in the root `README.md`

Install steps: `chrome://extensions` → Developer mode → Load unpacked → select `extension/`. State the permission warning and why it is needed. State plainly that the extension only reads requests the browser already made, and that a site the browser cannot reach is equally unreachable here.

Ship the folder inside the GitHub release zip alongside the exe.

---

## Deferred

**True 1-click (extension → app with no clipboard hop).** Only worth building if users actually ask. When they do, the mechanism is **Chrome Native Messaging**, not a URL scheme: the payload travels over stdin/stdout with no size limit and never touches a command line, which is what makes D2 go away rather than get worked around. It costs a native-host manifest under `HKCU\Software\Classes` (no admin), a small host executable, and a fixed extension ID. That is a lot of moving parts to remove one click, which is exactly why it is deferred rather than planned.

**Raising the HAR size cap.** Swap `JsonDocument` for a `Utf8JsonReader` walk. Only if a real capture exceeds 256 MB.

---

## Verification Criteria

**Phase 1**
- `dotnet build` → 0 warnings, 0 errors
- `dotnet test` → every pre-existing test still passes, plus the new Capture tests
- Copy as cURL (bash) from Chrome → paste → URL and headers fill; no `sec-*` or `accept-encoding` present
- Copy as cURL (cmd) from Chrome → paste → caret escapes resolved, no stray `^` in any value
- A header value containing `|` survives into a single `-H` flag

**Phase 2**
- Drop `hls-with-segments.har` → two HLS candidates, master first, zero `.ts` entries
- Drop `progressive-mp4-ranges.har` → exactly one candidate
- Drop `no-streams.har` → guidance message, URL box unchanged
- Drop a 300 MB file named `.har` → size message, no hang
- A `.har` never reaches `TextBox_URL` as a literal path

**Phase 3**
- Badge shows a count while a stream plays
- Copy as cURL → paste in GUI → fields fill
- Service worker restart (30 s idle, then reopen popup) → the list survives

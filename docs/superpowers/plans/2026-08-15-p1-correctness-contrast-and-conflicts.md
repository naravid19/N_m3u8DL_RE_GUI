# P1 Correctness, Contrast & Option Conflicts Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Close the P1 backlog left by the P0 plan — text that decodes wrongly for non-UTF-8 sources, a title fetch that is O(N²), nine measured WCAG failures, and three settings that silently override or ignore each other.

**Architecture:** Three parts that ship independently. Part A fixes decoding and escaping inside `N_m3u8DL_RE_GUI.Core` and `Services`, all unit-testable with no UI. Part B replaces four colour tokens in `MainWindow.xaml` with values verified against a WCAG contrast calculator. Part C makes conflicting options visible in the UI instead of silently discarding user input. A reviewer can accept any one part and reject the others.

**Tech Stack:** .NET 9 / WPF, xunit 2.6.6, NSubstitute 6.0.0, CommunityToolkit.Mvvm 8.4.0, plus one new first-party package (see Global Constraints)

**Spec:**
- `.impeccable/critique/2026-08-14T18-24-43Z__n-m3u8dl-re-gui-mainwindow-xaml.md` — the measured contrast table and the option-conflict findings
- `docs/superpowers/plans/2026-08-15-p0-hardening-and-feedback-loop.md` — the "Deferred — not in this plan" section is this plan's input list

---

## Global Constraints

- **Baseline is 458 passing tests** (`dotnet test N_m3u8DL_RE_GUI.sln`) and a **clean build with 0 warnings** (`dotnet build N_m3u8DL_RE_GUI.sln -c Debug --no-incremental`). Every task ends with both still true and the test count no lower.
- **One new package is allowed and only one:** `System.Text.Encoding.CodePages` (Microsoft, first-party) on `N_m3u8DL_RE_GUI.Core`. It is the only way to decode GBK/Big5/Shift-JIS on .NET Core, and Tasks 1 and 2 both need it. Do not add any other package.
- **Target frameworks are fixed:** `net9.0` for Core, `net9.0-windows` for the GUI and tests.
- **`Nullable` is `disable` in all three .csproj files.** Files opt in with `#nullable enable` at the top; follow the file you are editing.
- **All user-visible strings are English.** The resource files were deleted in commit 2f42467; do not reintroduce them.
- **Config back-compat is mandatory.** Existing `config.txt` and `config.json` files must keep loading. Never rename a persisted key.
- **`XamlAccessibilityTests` must keep passing.** It enforces accessible names on all 96 controls, focus visuals on all five interactive styles, and that shortcuts route to `MainWindow`'s own routed commands.
- Commit after every task. Branch is `dev`; do not push unless asked.

---

## File Structure

**Created:**

| File | Responsibility |
|---|---|
| `N_m3u8DL_RE_GUI.Core/HtmlTitleExtractor.cs` | Streaming-safe `</title>` detection and title cleaning, pulled out of `UtilityService` so both are testable without a socket. |
| `N_m3u8DL_RE_GUI.Core/LegacyConfigCodec.cs` | Escape/unescape for the `key=value;` legacy format, so the separator stops eating data. |
| `N_m3u8DL_RE_GUI.Tests/Unit/Core/HtmlTitleExtractorTests.cs` | Tests for the above. |
| `N_m3u8DL_RE_GUI.Tests/Unit/Core/LegacyConfigCodecTests.cs` | Tests for the above. |
| `N_m3u8DL_RE_GUI.Tests/Unit/UI/XamlContrastTests.cs` | Parses the palette out of `MainWindow.xaml` and asserts WCAG ratios, so a future colour edit cannot silently regress. |

**Modified:**

| File | Change |
|---|---|
| `N_m3u8DL_RE_GUI.Core/N_m3u8DL_RE_GUI.Core.csproj` | Add `System.Text.Encoding.CodePages`. |
| `N_m3u8DL_RE_GUI.Core/TextEncodingDetector.cs` | Real ANSI fallback; tolerate a sequence straddling the sample boundary. |
| `N_m3u8DL_RE_GUI.Core/ArgsBuilder.cs` | Cache the escape-char array; escape quotes in `MuxBinPath`. |
| `N_m3u8DL_RE_GUI.Core/DownloadOptions.cs` | `AudioOnly` getter accepts the drop pattern the GUI actually writes. |
| `N_m3u8DL_RE_GUI/Services/UtilityService.cs` | Honour the HTTP charset; use `HtmlTitleExtractor`; fix the reserved-device-name check. |
| `N_m3u8DL_RE_GUI/Services/ConfigService.cs` | Use `LegacyConfigCodec`. |
| `N_m3u8DL_RE_GUI/MainWindow.xaml` | Four colour tokens; disabled-state bindings; renamed language label. |
| `N_m3u8DL_RE_GUI/MainWindow.xaml.cs` | Option-conflict wiring. |

**Explicitly out of scope — needs its own plan and a design pass first:** the information-architecture restructure (task-named groups, progressive disclosure, hiding the 82 secondary controls), `BuildArgsRE`/`BuildDownloadOptions` deduplication, `GitHubUpdateCheckService` testability, dead-code removal. Those are listed at the end.

---

## PART A — Correctness

## Task 1: Decode titles using the charset the server declared

`GetHtmlTitleStreamingAsync` wraps the response body in `new StreamReader(stream)`, which assumes UTF-8. A GBK or Big5 page — the exact audience this tool serves — yields a title full of replacement characters. The same method also calls `sb.ToString().Contains("</title>")` on every 8 KB chunk, which is O(N²): for a 256 KB page that is 32 allocations totalling roughly 4 MB, in a method whose stated purpose was to *reduce* allocations.

**Files:**
- Create: `N_m3u8DL_RE_GUI.Core/HtmlTitleExtractor.cs`
- Create: `N_m3u8DL_RE_GUI.Tests/Unit/Core/HtmlTitleExtractorTests.cs`
- Modify: `N_m3u8DL_RE_GUI.Core/N_m3u8DL_RE_GUI.Core.csproj`
- Modify: `N_m3u8DL_RE_GUI/Services/UtilityService.cs:59-104, 136-153`

**Interfaces:**
- Consumes: nothing.
- Produces:
  - `static bool HtmlTitleExtractor.ContainsClosingTitleTag(string chunk, ref string carry)` — call per chunk; `carry` holds the 7-character overlap between chunks.
  - `static string HtmlTitleExtractor.Extract(string html)` — returns the cleaned title, or `string.Empty`.
  - `static string HtmlTitleExtractor.Clean(string title)` — strips known site suffixes and characters illegal in Windows filenames.
  - `static Encoding HtmlTitleExtractor.ResolveEncoding(string? charSet)` — maps an HTTP `charset` token to an `Encoding`, defaulting to UTF-8.

- [ ] **Step 1: Add the code-pages package**

In `N_m3u8DL_RE_GUI.Core/N_m3u8DL_RE_GUI.Core.csproj`, add before `</Project>`:

```xml
  <ItemGroup>
    <!-- .NET Core dropped the non-Unicode code pages from the BCL. GBK/Big5/Shift-JIS
         are required both for HTTP charset handling and for legacy batch .txt files. -->
    <PackageReference Include="System.Text.Encoding.CodePages" Version="9.0.0" />
  </ItemGroup>
```

Run `dotnet restore N_m3u8DL_RE_GUI.sln` and confirm it succeeds.

- [ ] **Step 2: Write the failing tests**

Create `N_m3u8DL_RE_GUI.Tests/Unit/Core/HtmlTitleExtractorTests.cs`:

```csharp
#nullable enable
using System.Text;
using N_m3u8DL_RE_GUI.Core;
using Xunit;

namespace N_m3u8DL_RE_GUI.Tests.Unit.Core;

public class HtmlTitleExtractorTests
{
    [Theory]
    [InlineData("<html><head><title>Episode 01</title></head>", "Episode 01")]
    [InlineData("<title data-x=\"1\" lang=\"th\">รายการที่ 5</title>", "รายการที่ 5")]
    [InlineData("<title>\n   Spaced   \n</title>", "Spaced")]
    [InlineData("<TITLE>Upper Case Tag</TITLE>", "Upper Case Tag")]
    [InlineData("<html><body>no title</body></html>", "")]
    [InlineData("<title></title>", "")]
    [InlineData("", "")]
    public void Extract_ShouldReturnTheCleanedTitleOrEmpty(string html, string expected)
    {
        Assert.Equal(expected, HtmlTitleExtractor.Extract(html));
    }

    [Theory]
    [InlineData("A:B/C?D*E|F\"G", "ABCDEFG")]
    [InlineData("My Video_哔哩哔哩", "My Video")]
    [InlineData("My Video - WeTV", "My Video")]
    [InlineData("My Video_腾讯视频", "My Video")]
    [InlineData("My Video_爱奇艺", "My Video")]
    [InlineData("My Video_优酷", "My Video")]
    [InlineData("   padded   ", "padded")]
    [InlineData("", "")]
    public void Clean_ShouldStripIllegalCharactersAndKnownSiteSuffixes(string raw, string expected)
    {
        Assert.Equal(expected, HtmlTitleExtractor.Clean(raw));
    }

    [Fact]
    public void ContainsClosingTitleTag_ShouldFindATagSplitAcrossTwoChunks()
    {
        // THE BUG this replaces: the old code called sb.ToString().Contains() on the whole
        // accumulated buffer every chunk, which is O(N^2). A carry of 7 chars is enough to
        // catch "</title>" no matter where the chunk boundary lands.
        var carry = string.Empty;

        Assert.False(HtmlTitleExtractor.ContainsClosingTitleTag("<title>Some Name</ti", ref carry));
        Assert.True(HtmlTitleExtractor.ContainsClosingTitleTag("tle></head>", ref carry));
    }

    [Fact]
    public void ContainsClosingTitleTag_ShouldFindATagFullyInsideOneChunk()
    {
        var carry = string.Empty;

        Assert.True(HtmlTitleExtractor.ContainsClosingTitleTag("<title>X</title>", ref carry));
    }

    [Fact]
    public void ContainsClosingTitleTag_ShouldNotFalselyMatchAcrossUnrelatedChunks()
    {
        var carry = string.Empty;

        Assert.False(HtmlTitleExtractor.ContainsClosingTitleTag("aaaaaaaaaaaaaaaa", ref carry));
        Assert.False(HtmlTitleExtractor.ContainsClosingTitleTag("bbbbbbbbbbbbbbbb", ref carry));
    }

    [Fact]
    public void ContainsClosingTitleTag_ShouldBeCaseInsensitive()
    {
        var carry = string.Empty;

        Assert.True(HtmlTitleExtractor.ContainsClosingTitleTag("<TITLE>X</TITLE>", ref carry));
    }

    [Theory]
    [InlineData("utf-8", "utf-8")]
    [InlineData("UTF-8", "utf-8")]
    [InlineData("\"utf-8\"", "utf-8")]
    [InlineData("gb2312", "gb2312")]
    [InlineData("gbk", "gbk")]
    [InlineData("big5", "big5")]
    [InlineData("shift_jis", "shift_jis")]
    [InlineData("iso-8859-1", "iso-8859-1")]
    [InlineData(null, "utf-8")]
    [InlineData("", "utf-8")]
    [InlineData("not-a-real-charset", "utf-8")]
    public void ResolveEncoding_ShouldMapCharsetTokensAndFallBackToUtf8(string? charSet, string expectedWebName)
    {
        Assert.Equal(expectedWebName, HtmlTitleExtractor.ResolveEncoding(charSet).WebName);
    }

    [Fact]
    public void ResolveEncoding_ShouldActuallyDecodeGbkBytes()
    {
        var encoding = HtmlTitleExtractor.ResolveEncoding("gbk");
        var bytes = new byte[] { 0xB4, 0xF2 };   // "打" in GBK

        var decoded = encoding.GetString(bytes);

        Assert.Equal("打", decoded);
        Assert.DoesNotContain('\uFFFD', decoded);
    }
}
```

- [ ] **Step 3: Run them to verify they fail**

```bash
dotnet test N_m3u8DL_RE_GUI.sln --filter "FullyQualifiedName~HtmlTitleExtractorTests"
```

Expected: FAIL to compile — `HtmlTitleExtractor` does not exist.

- [ ] **Step 4: Write the implementation**

Create `N_m3u8DL_RE_GUI.Core/HtmlTitleExtractor.cs`:

```csharp
#nullable enable
using System;
using System.Text;
using System.Text.RegularExpressions;

namespace N_m3u8DL_RE_GUI.Core;

/// <summary>
/// Streaming-safe HTML title handling. Pure functions so the read loop in UtilityService
/// stays thin and every rule here is testable without a socket.
/// </summary>
public static class HtmlTitleExtractor
{
    private const string ClosingTag = "</title>";

    /// <summary>Overlap kept between chunks: one char short of the tag length.</summary>
    private static readonly int CarrySize = ClosingTag.Length - 1;

    private static readonly Regex TitlePattern = new(
        @"<title[^>]*>([^<]+)</title>",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    static HtmlTitleExtractor()
    {
        // .NET Core ships only Unicode code pages by default.
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    /// <summary>
    /// True once the closing title tag has been seen. Call once per chunk in order,
    /// threading the same <paramref name="carry"/> through; it holds the tail of the
    /// previous chunk so a tag split across a boundary is still found. O(chunk), not
    /// O(total) — the previous implementation re-scanned the whole buffer every chunk.
    /// </summary>
    public static bool ContainsClosingTitleTag(string chunk, ref string carry)
    {
        if (string.IsNullOrEmpty(chunk))
            return false;

        var window = carry.Length > 0 ? carry + chunk : chunk;
        if (window.Contains(ClosingTag, StringComparison.OrdinalIgnoreCase))
        {
            carry = string.Empty;
            return true;
        }

        carry = window.Length <= CarrySize ? window : window[^CarrySize..];
        return false;
    }

    /// <summary>Returns the cleaned title, or empty when the document has none.</summary>
    public static string Extract(string html)
    {
        if (string.IsNullOrEmpty(html))
            return string.Empty;

        var match = TitlePattern.Match(html);
        return match.Success ? Clean(match.Groups[1].Value) : string.Empty;
    }

    /// <summary>Strips known site suffixes and characters Windows forbids in filenames.</summary>
    public static string Clean(string title)
    {
        if (string.IsNullOrWhiteSpace(title))
            return string.Empty;

        title = Regex.Replace(title, "[-_\\s]*(\\u7231\\u5947\\u827A).*?$", "", RegexOptions.IgnoreCase);
        title = Regex.Replace(title, "[-_\\s]*(\\u817E\\u8BAF\\u89C6\\u9891).*?$", "", RegexOptions.IgnoreCase);
        title = Regex.Replace(title, @"[-_\s]*WeTV.*$", "", RegexOptions.IgnoreCase);
        title = Regex.Replace(title, "[-_\\s]*(\\u54D4\\u54E9\\u54D4\\u54E9).*?$", "", RegexOptions.IgnoreCase);
        title = Regex.Replace(title, "[-_\\s]*(\\u4F18\\u9177).*?$", "", RegexOptions.IgnoreCase);

        title = Regex.Replace(title, @"[<>:""/\\|?*]", "");
        return title.Trim();
    }

    /// <summary>
    /// Maps an HTTP Content-Type charset token to an Encoding, falling back to UTF-8 for
    /// anything missing or unrecognised.
    /// </summary>
    public static Encoding ResolveEncoding(string? charSet)
    {
        if (string.IsNullOrWhiteSpace(charSet))
            return Encoding.UTF8;

        var trimmed = charSet.Trim().Trim('"', '\'');
        try
        {
            return Encoding.GetEncoding(trimmed);
        }
        catch (ArgumentException)
        {
            return Encoding.UTF8;
        }
    }
}
```

- [ ] **Step 5: Run the tests to verify they pass**

```bash
dotnet test N_m3u8DL_RE_GUI.sln --filter "FullyQualifiedName~HtmlTitleExtractorTests"
```

Expected: PASS, 27 tests.

- [ ] **Step 6: Rewire UtilityService**

In `UtilityService.cs`, replace `GetHtmlTitleStreamingAsync` and delete the private `CleanTitle`:

```csharp
    private async Task<string> GetHtmlTitleStreamingAsync(string url, CancellationToken cancellationToken)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            using var response = await SharedHttpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();

            // Honour the charset the server declared. Defaulting to UTF-8 turned every
            // GBK/Big5 page title into replacement characters.
            var encoding = HtmlTitleExtractor.ResolveEncoding(response.Content.Headers.ContentType?.CharSet);

            using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var reader = new StreamReader(stream, encoding, detectEncodingFromByteOrderMarks: true);

            var buffer = new char[8192];
            var sb = new StringBuilder();
            var carry = string.Empty;
            int read;

            while ((read = await reader.ReadAsync(buffer, 0, buffer.Length)) > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var chunk = new string(buffer, 0, read);
                sb.Append(chunk);

                if (HtmlTitleExtractor.ContainsClosingTitleTag(chunk, ref carry))
                    break;

                // Bound allocations: stop buffering just past 256 KB.
                if (sb.Length > 256 * 1024)
                    break;
            }

            return HtmlTitleExtractor.Extract(sb.ToString());
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to get HTML title from {url}: {ex.Message}");
        }
        return string.Empty;
    }
```

In `GetQQTitleAsync`, replace `CleanTitle(...)` with `HtmlTitleExtractor.Clean(...)`. Add `using N_m3u8DL_RE_GUI.Core;` if the compiler asks (it is already present at line 7).

- [ ] **Step 7: Run the whole suite**

```bash
dotnet build N_m3u8DL_RE_GUI.sln -c Debug --no-incremental
dotnet test N_m3u8DL_RE_GUI.sln
```

Expected: 0 warnings, 0 errors; PASS. `UtilityServiceTitleTests` must still pass unchanged — it exercises this path end-to-end over a loopback socket, including the 256 KB cap and cancellation.

- [ ] **Step 8: Commit**

```bash
git add N_m3u8DL_RE_GUI.Core/HtmlTitleExtractor.cs N_m3u8DL_RE_GUI.Core/N_m3u8DL_RE_GUI.Core.csproj N_m3u8DL_RE_GUI.Tests/Unit/Core/HtmlTitleExtractorTests.cs N_m3u8DL_RE_GUI/Services/UtilityService.cs
git commit -m "fix(title): honour the HTTP charset and stop rescanning the buffer

StreamReader defaulted to UTF-8, so GBK and Big5 page titles decoded to
replacement characters. The closing-tag search also called ToString() on
the whole accumulated buffer every 8KB chunk, which is O(N^2) in a method
written to reduce allocations.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

## Task 2: Make the encoding detector's ANSI fallback real

`TextEncodingDetector` returns `Encoding.Default` when a batch `.txt` is not valid UTF-8. On .NET Framework that was the system ANSI code page; on .NET Core it is UTF-8, so the fallback decodes nothing and produces replacement characters. Separately, `IsUtf8Bytes` requires the final multi-byte sequence in the 8192-byte sample to be complete, so a valid UTF-8 file is misdetected purely because of where byte 8192 lands.

**Files:**
- Modify: `N_m3u8DL_RE_GUI.Core/TextEncodingDetector.cs:11-13, 49, 73-103`
- Test: `N_m3u8DL_RE_GUI.Tests/Unit/Core/TextEncodingDetectorEdgeTests.cs`

**Interfaces:**
- Consumes: `CodePagesEncodingProvider` registration performed by `HtmlTitleExtractor`'s static constructor in Task 1. This task registers it independently so `TextEncodingDetector` does not depend on another type being touched first.
- Produces: `static Encoding TextEncodingDetector.AnsiFallback { get; }` — the system ANSI code page, or UTF-8 when it is unavailable.

- [ ] **Step 1: Flip the two characterisation tests**

In `TextEncodingDetectorEdgeTests.cs`, replace `DefaultBranch_DoesNotActuallyDecodeAnsiBytes`:

```csharp
    [Fact]
    public void AnsiFallback_ShouldActuallyDecodeLegacyBytes()
    {
        // On .NET Framework Encoding.Default was the system ANSI code page. On .NET Core
        // it is UTF-8, so the old fallback could not recover a legacy GBK/Big5 batch list.
        var decoded = TextEncodingDetector.AnsiFallback.GetString(new byte[] { 0xB4, 0xF2 });

        Assert.NotEqual("utf-8", TextEncodingDetector.AnsiFallback.WebName);
        Assert.DoesNotContain('\uFFFD', decoded);
    }
```

And replace `DetectFromStream_WhenMultiByteSequenceStraddlesSampleBoundary_FallsBackToDefault`:

```csharp
    [Fact]
    public void DetectFromStream_WhenMultiByteSequenceStraddlesSampleBoundary_ShouldStillReturnUtf8()
    {
        // The sample is cut at exactly 8192 bytes. A sequence that starts inside the window
        // and finishes outside it is not evidence of non-UTF-8 data.
        var bytes = new byte[SampleSize + 16];
        Array.Fill(bytes, (byte)'a', 0, SampleSize - 1);
        bytes[SampleSize - 1] = 0xC3; // lead byte is the last byte of the sample
        bytes[SampleSize] = 0xA9;     // continuation byte is never read
        Array.Fill(bytes, (byte)'b', SampleSize + 1, 15);

        using var stream = new MemoryStream(bytes);

        Assert.Same(Encoding.UTF8, TextEncodingDetector.DetectFromStream(stream));
    }
```

Then update the three remaining `Assert.Same(Encoding.Default, ...)` assertions — in `DetectFromStream_WithTruncatedUtf8Bom_ShouldNotBeTreatedAsBom`, `DetectFromStream_WithLegacyAnsiHighBytes_ShouldReturnDefault` and `DetectFromStream_WithChunkedStream_ShouldReadFullSampleNotJustFirstChunk` — to `Assert.Same(TextEncodingDetector.AnsiFallback, ...)`. Rename the middle one to `..._ShouldReturnTheAnsiFallback`.

Finally update `EncodingDefaultAndUtf8_AreDistinctInstances_SoBranchAssertionsAreMeaningful`:

```csharp
    [Fact]
    public void AnsiFallbackAndUtf8_AreDistinctInstances_SoBranchAssertionsAreMeaningful()
    {
        Assert.NotSame(TextEncodingDetector.AnsiFallback, Encoding.UTF8);
    }
```

- [ ] **Step 2: Run them to verify they fail**

```bash
dotnet test N_m3u8DL_RE_GUI.sln --filter "FullyQualifiedName~TextEncodingDetectorEdgeTests"
```

Expected: FAIL to compile — `TextEncodingDetector.AnsiFallback` does not exist.

- [ ] **Step 3: Add the fallback and fix the boundary rule**

In `TextEncodingDetector.cs`, add after the `MaxSampleSize` constant:

```csharp
    /// <summary>
    /// The system ANSI code page, or UTF-8 where it is unavailable. Encoding.Default is
    /// UTF-8 on .NET Core, which made the non-UTF-8 branch a no-op.
    /// </summary>
    public static Encoding AnsiFallback { get; } = ResolveAnsiFallback();

    private static Encoding ResolveAnsiFallback()
    {
        try
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            return Encoding.GetEncoding(0);   // 0 = the process's ANSI code page
        }
        catch (Exception)
        {
            return Encoding.UTF8;
        }
    }
```

Change line 49 from `Encoding.Default` to `AnsiFallback`:

```csharp
            return IsUtf8Bytes(buffer, bytesRead) ? Encoding.UTF8 : AnsiFallback;
```

Replace `IsUtf8Bytes` so a sequence cut off by the sample boundary is tolerated:

```csharp
    /// <summary>
    /// Validates UTF-8 structure over a sample. A multi-byte sequence that begins inside
    /// the sample but finishes past its end is accepted: the sample boundary is arbitrary
    /// and truncation there says nothing about the file's encoding.
    /// </summary>
    private static bool IsUtf8Bytes(byte[] data, int length)
    {
        var charByteCounter = 1;

        for (var i = 0; i < length; i++)
        {
            byte currentByte = data[i];
            if (charByteCounter == 1)
            {
                if (currentByte >= 0x80)
                {
                    while (((currentByte <<= 1) & 0x80) != 0)
                    {
                        charByteCounter++;
                    }

                    if (charByteCounter == 1 || charByteCounter > 6)
                        return false;
                }
            }
            else
            {
                if ((currentByte & 0xC0) != 0x80)
                    return false;

                charByteCounter--;
            }
        }

        // charByteCounter > 1 means the last sequence is incomplete. That is only a real
        // failure when we reached the true end of the data, not the end of the sample.
        return charByteCounter <= 1 || length == MaxSampleSize;
    }
```

Add `using System.Text;` if not already present (it is, at line 4).

- [ ] **Step 4: Run the tests to verify they pass**

```bash
dotnet test N_m3u8DL_RE_GUI.sln --filter "FullyQualifiedName~TextEncodingDetector"
```

Expected: PASS. Both `TextEncodingDetectorTests` and `TextEncodingDetectorEdgeTests` must be green; the older file also asserts `Encoding.Default.WebName` in two places, which still passes because it compares web names.

- [ ] **Step 5: Run the whole suite and commit**

```bash
dotnet build N_m3u8DL_RE_GUI.sln -c Debug --no-incremental
dotnet test N_m3u8DL_RE_GUI.sln
git add N_m3u8DL_RE_GUI.Core/TextEncodingDetector.cs N_m3u8DL_RE_GUI.Tests/Unit/Core/TextEncodingDetectorEdgeTests.cs
git commit -m "fix(encoding): give the ANSI fallback a real code page

Encoding.Default is UTF-8 on .NET Core, so the non-UTF-8 branch decoded
legacy batch lists as UTF-8 and produced replacement characters. Also
stop treating a multi-byte sequence cut off by the 8KB sample boundary
as evidence that the file is not UTF-8.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

## Task 3: Close the two argument-escaping gaps

`QuoteForWindowsArgument`'s fast path allocates a two-element `char[]` on every call — in the method whose changelog entry claims a zero-allocation fast path. And `MuxBinPath` is interpolated into the `--mux-after-done` option with no escaping, so a quote in the path terminates `bin_path` early.

**Files:**
- Modify: `N_m3u8DL_RE_GUI.Core/ArgsBuilder.cs:160-177, 230-236`
- Test: `N_m3u8DL_RE_GUI.Tests/Unit/Core/ArgsBuilderQuotingTests.cs`

**Interfaces:**
- Consumes: `StringBuilderExtensions.AppendQuoted`, `AppendIfNotEmpty`, `AppendIfTrue` (unchanged public surface).
- Produces: no signature change.

- [ ] **Step 1: Flip the characterisation test**

In `ArgsBuilderQuotingTests.cs`, replace `Build_MuxBinPathContainingAQuote_BreaksTheOption`:

```csharp
    [Fact]
    public void Build_MuxBinPathContainingAQuote_ShouldSurviveAsOneArgument()
    {
        var options = new DownloadOptions
        {
            Input = "https://example.com/a.m3u8",
            MuxAfterDone = true,
            MuxFormat = "mkv",
            Muxer = "mkvmerge",
            MuxBinPath = @"C:\od""d\mkvmerge.exe"
        };

        var parsed = ParseCommandLine("prog " + ArgsBuilder.Build(options));

        Assert.Contains(@"format=mkv:muxer=mkvmerge:bin_path=C:\od""d\mkvmerge.exe", parsed);
    }
```

And add a guard for the allocation fix:

```csharp
    [Fact]
    public void AppendQuoted_ShouldNotAllocateAFreshSearchArrayPerCall()
    {
        // Guards the "zero-allocation fast path": the escape-character set must be a
        // cached static, not a `new[]` literal evaluated on every invocation.
        var source = File.ReadAllText(ArgsBuilderSourcePath());

        Assert.DoesNotContain("IndexOfAny(new", source);
        Assert.Contains("EscapeChars", source);
    }

    private static string ArgsBuilderSourcePath()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            var candidate = Path.Combine(dir.FullName, "N_m3u8DL_RE_GUI.Core", "ArgsBuilder.cs");
            if (File.Exists(candidate))
                return candidate;
            dir = dir.Parent;
        }
        throw new FileNotFoundException("Could not locate ArgsBuilder.cs from " + AppContext.BaseDirectory);
    }
```

- [ ] **Step 2: Run them to verify they fail**

```bash
dotnet test N_m3u8DL_RE_GUI.sln --filter "FullyQualifiedName~ArgsBuilderQuotingTests"
```

Expected: FAIL — the quote test reports the argument was split, and the allocation test finds `IndexOfAny(new`.

- [ ] **Step 3: Cache the escape set**

In `ArgsBuilder.cs`, inside `StringBuilderExtensions`, add above `QuoteForWindowsArgument`:

```csharp
    /// <summary>Cached so the fast path below really is allocation-free.</summary>
    private static readonly char[] EscapeChars = { '\\', '"' };
```

And change the fast-path test:

```csharp
        if (value.IndexOfAny(EscapeChars) < 0)
```

- [ ] **Step 4: Escape the mux binary path**

In `ArgsBuilder.Build`, replace the `MuxBinPath` line inside the mux block:

```csharp
            if (!string.IsNullOrWhiteSpace(options.MuxBinPath))
            {
                // The whole mux option is appended without going through the escaper, so
                // a quote inside the path would terminate bin_path early and spill the
                // remainder into separate arguments.
                var escapedBinPath = options.MuxBinPath.Replace("\"", "\\\"");
                muxOptions.Append($":bin_path=\"{escapedBinPath}\"");
            }
```

- [ ] **Step 4b: Escape the custom range the same way**

`RangeStart` and `RangeEnd` come straight from free-text boxes and are interpolated with the same raw pattern. Replace the `--custom-range` line in `ArgsBuilder.Build`:

```csharp
        if (options.HasTimeRange)
        {
            var start = options.RangeStart!.Replace("\"", "\\\"");
            var end = options.RangeEnd!.Replace("\"", "\\\"");
            sb.Append($" --custom-range \"{start}-{end}\"");
        }
```

Then flip the matching characterisation test in `ArgsBuilderQuotingTests.cs`:

```csharp
    [Fact]
    public void Build_CustomRangeContainingAQuote_ShouldSurviveAsOneArgument()
    {
        var options = new DownloadOptions
        {
            Input = "https://example.com/a.m3u8",
            RangeStart = "00:01:00\"",
            RangeEnd = "00:02:00"
        };

        var parsed = ParseCommandLine("prog " + ArgsBuilder.Build(options));

        Assert.Contains("00:01:00\"-00:02:00", parsed);
    }
```

- [ ] **Step 5: Run the tests to verify they pass**

```bash
dotnet test N_m3u8DL_RE_GUI.sln --filter "FullyQualifiedName~ArgsBuilder"
```

Expected: PASS. Both `ArgsBuilderTests` and `ArgsBuilderQuotingTests` green — the CommandLineToArgvW round-trip tests are the real check that nothing else shifted.

- [ ] **Step 6: Run the whole suite and commit**

```bash
dotnet build N_m3u8DL_RE_GUI.sln -c Debug --no-incremental
dotnet test N_m3u8DL_RE_GUI.sln
git add N_m3u8DL_RE_GUI.Core/ArgsBuilder.cs N_m3u8DL_RE_GUI.Tests/Unit/Core/ArgsBuilderQuotingTests.cs
git commit -m "fix(args): cache the escape set and escape the mux binary path

The 'zero-allocation fast path' allocated a char[] per call, and a quote
in MuxBinPath terminated bin_path early.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

## Task 4: Stop the legacy config separator from eating data

`ConfigService` joins entries with `;` and splits on it, with no escaping. Fields stored raw rather than base64 — `AdKeyword`, `SavePattern`, `UrlProcessorArgs`, `LiveRecordLimit` — lose everything after the first `;` and leave a bogus extra record behind. `ConfigServiceFormatTests.RoundTrip_WithSemicolonInAValue_LosesData` currently asserts the data loss.

**Files:**
- Create: `N_m3u8DL_RE_GUI.Core/LegacyConfigCodec.cs`
- Create: `N_m3u8DL_RE_GUI.Tests/Unit/Core/LegacyConfigCodecTests.cs`
- Modify: `N_m3u8DL_RE_GUI/Services/ConfigService.cs:52-68, 78-85`
- Test: `N_m3u8DL_RE_GUI.Tests/Unit/Services/ConfigServiceFormatTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces:
  - `static string LegacyConfigCodec.EscapeValue(string? value)`
  - `static string LegacyConfigCodec.UnescapeValue(string? value)`

- [ ] **Step 1: Write the failing codec tests**

Create `N_m3u8DL_RE_GUI.Tests/Unit/Core/LegacyConfigCodecTests.cs`:

```csharp
#nullable enable
using N_m3u8DL_RE_GUI.Core;
using Xunit;

namespace N_m3u8DL_RE_GUI.Tests.Unit.Core;

public class LegacyConfigCodecTests
{
    [Theory]
    [InlineData("plain", "plain")]
    [InlineData("", "")]
    [InlineData(null, "")]
    [InlineData("ads;sponsor", "ads%3Bsponsor")]
    [InlineData("a;b;c", "a%3Bb%3Bc")]
    [InlineData("100%", "100%25")]
    [InlineData("%3B", "%253B")]                       // a literal %3B must not decode to ';'
    [InlineData("mix%and;match", "mix%25and%3Bmatch")]
    [InlineData("$Title_$Id=$Res", "$Title_$Id=$Res")]  // '=' is safe: only the first one splits
    public void EscapeValue_ShouldEncodeOnlyTheSeparatorAndTheEscapeCharacter(string? raw, string expected)
    {
        Assert.Equal(expected, LegacyConfigCodec.EscapeValue(raw));
    }

    [Theory]
    [InlineData("ads;sponsor")]
    [InlineData("100% sure; really")]
    [InlineData("%3B")]
    [InlineData("%25%3B")]
    [InlineData("ตอนที่ 1;中文")]
    [InlineData("")]
    public void EscapeThenUnescape_ShouldRoundTrip(string raw)
    {
        Assert.Equal(raw, LegacyConfigCodec.UnescapeValue(LegacyConfigCodec.EscapeValue(raw)));
    }

    [Theory]
    [InlineData("no escapes here", "no escapes here")]
    [InlineData("", "")]
    [InlineData(null, "")]
    public void UnescapeValue_ShouldLeaveOldUnescapedValuesAlone(string? stored, string expected)
    {
        // Files written before this codec existed contain no % sequences, so they decode
        // to themselves. That is what keeps existing config.txt files loading.
        Assert.Equal(expected, LegacyConfigCodec.UnescapeValue(stored));
    }

    [Fact]
    public void UnescapeValue_ShouldBeCaseInsensitiveOnHexDigits()
    {
        Assert.Equal(";", LegacyConfigCodec.UnescapeValue("%3b"));
        Assert.Equal(";", LegacyConfigCodec.UnescapeValue("%3B"));
    }
}
```

- [ ] **Step 2: Run them to verify they fail**

```bash
dotnet test N_m3u8DL_RE_GUI.sln --filter "FullyQualifiedName~LegacyConfigCodecTests"
```

Expected: FAIL to compile — `LegacyConfigCodec` does not exist.

- [ ] **Step 3: Write the codec**

Create `N_m3u8DL_RE_GUI.Core/LegacyConfigCodec.cs`:

```csharp
#nullable enable
using System;
using System.Text;

namespace N_m3u8DL_RE_GUI.Core;

/// <summary>
/// Escaping for the legacy "key=value;key=value" config.txt format.
///
/// Only two characters need encoding: ';' because it is the record separator, and '%'
/// because it introduces an escape. '=' is safe — the reader splits on the first one only.
/// Values written before this codec existed contain no '%' sequences and therefore decode
/// to themselves, which is what keeps old files loading.
/// </summary>
public static class LegacyConfigCodec
{
    /// <summary>Cached, for the same reason ArgsBuilder caches its escape set.</summary>
    private static readonly char[] NeedsEscaping = { ';', '%' };

    public static string EscapeValue(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return string.Empty;

        if (value.IndexOfAny(NeedsEscaping) < 0)
            return value;

        var sb = new StringBuilder(value.Length + 8);
        foreach (var ch in value)
        {
            switch (ch)
            {
                case '%': sb.Append("%25"); break;
                case ';': sb.Append("%3B"); break;
                default: sb.Append(ch); break;
            }
        }
        return sb.ToString();
    }

    public static string UnescapeValue(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return string.Empty;

        if (value.IndexOf('%') < 0)
            return value;

        var sb = new StringBuilder(value.Length);
        for (var i = 0; i < value.Length; i++)
        {
            if (value[i] == '%' && i + 2 < value.Length && TryHex(value[i + 1], value[i + 2], out var decoded))
            {
                sb.Append(decoded);
                i += 2;
                continue;
            }
            sb.Append(value[i]);
        }
        return sb.ToString();
    }

    private static bool TryHex(char high, char low, out char result)
    {
        result = '\0';
        if (!Uri.IsHexDigit(high) || !Uri.IsHexDigit(low))
            return false;

        var code = Convert.ToInt32($"{high}{low}", 16);
        // Only the two characters this codec produces are decoded. Anything else is a
        // literal '%' the user typed, and must survive untouched.
        if (code != '%' && code != ';')
            return false;

        result = (char)code;
        return true;
    }
}
```

- [ ] **Step 4: Run the codec tests to verify they pass**

```bash
dotnet test N_m3u8DL_RE_GUI.sln --filter "FullyQualifiedName~LegacyConfigCodecTests"
```

Expected: PASS, 20 tests.

- [ ] **Step 5: Flip the data-loss test in ConfigServiceFormatTests**

Replace `RoundTrip_WithSemicolonInAValue_LosesData`:

```csharp
    [Fact]
    public void RoundTrip_WithSemicolonInAValue_ShouldPreserveTheWholeValue()
    {
        WithTempFile(string.Empty, path =>
        {
            var service = new ConfigService();
            var state = new AppConfigState();
            state.Set("AdKeyword", "ads;sponsor");
            state.Set("SavePattern", "100% of $Title");
            state.Set("NoLog", "1");

            service.Save(path, state);
            var loaded = service.Load(path);

            Assert.Equal("ads;sponsor", loaded.Get("AdKeyword"));
            Assert.Equal("100% of $Title", loaded.Get("SavePattern"));
            Assert.True(loaded.GetBool("NoLog"));
        });
    }

    [Fact]
    public void Load_ShouldStillReadFilesWrittenBeforeEscapingExisted()
    {
        WithTempFile("AdKeyword=plain value;NoLog=1", path =>
        {
            var loaded = new ConfigService().Load(path);

            Assert.Equal("plain value", loaded.Get("AdKeyword"));
            Assert.True(loaded.GetBool("NoLog"));
        });
    }
```

- [ ] **Step 6: Wire the codec into ConfigService**

In `ConfigService.cs`, add `using N_m3u8DL_RE_GUI.Core;` at the top. In `Load`, change the value assignment:

```csharp
            var key = segment[..separatorIndex].Trim();
            var value = LegacyConfigCodec.UnescapeValue(segment[(separatorIndex + 1)..]);
```

In `Save`, change the append:

```csharp
            builder.Append(pair.Key).Append('=').Append(LegacyConfigCodec.EscapeValue(pair.Value));
```

- [ ] **Step 7: Run the whole suite and commit**

```bash
dotnet build N_m3u8DL_RE_GUI.sln -c Debug --no-incremental
dotnet test N_m3u8DL_RE_GUI.sln
git add N_m3u8DL_RE_GUI.Core/LegacyConfigCodec.cs N_m3u8DL_RE_GUI.Tests/Unit/Core/LegacyConfigCodecTests.cs N_m3u8DL_RE_GUI/Services/ConfigService.cs N_m3u8DL_RE_GUI.Tests/Unit/Services/ConfigServiceFormatTests.cs
git commit -m "fix(config): escape the record separator in legacy config.txt

Values containing ';' were truncated and left a bogus extra record. Only
';' and '%' are encoded, so files written before this change still load.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

## Task 5: Catch reserved device names before the first dot

`GetValidFileName` checks `Path.GetFileNameWithoutExtension(sanitized)` against the DOS device names. Windows matches the reserved name against the segment before the **first** dot, so `CON.txt.bak` is still reserved but `GetFileNameWithoutExtension` returns `"CON.txt"` and the guard misses it.

**Files:**
- Modify: `N_m3u8DL_RE_GUI/Services/UtilityService.cs:155-184`
- Test: `N_m3u8DL_RE_GUI.Tests/Unit/Services/UtilityServiceTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces: no signature change to `string UtilityService.GetValidFileName(string path)`.

- [ ] **Step 1: Write the failing tests**

Append to `UtilityServiceTests.cs`:

```csharp
    [Theory]
    [InlineData("CON.txt.bak", "_CON.txt.bak")]
    [InlineData("con.mp4.part", "_con.mp4.part")]
    [InlineData("NUL.a.b.c", "_NUL.a.b.c")]
    [InlineData("COM1.log.1", "_COM1.log.1")]
    [InlineData("LPT9.x.y", "_LPT9.x.y")]
    public void GetValidFileName_ShouldSanitizeReservedNamesBeforeTheFirstDot(string input, string expected)
    {
        Assert.Equal(expected, new UtilityService().GetValidFileName(input));
    }

    [Theory]
    [InlineData("CONSOLE.txt")]
    [InlineData("CONTENT.mp4")]
    [InlineData("COM10.log")]
    [InlineData("MyCON.txt")]
    [InlineData("NULL.txt")]
    public void GetValidFileName_ShouldNotTouchNamesThatMerelyStartWithAReservedWord(string input)
    {
        Assert.Equal(input, new UtilityService().GetValidFileName(input));
    }
```

- [ ] **Step 2: Run them to verify they fail**

```bash
dotnet test N_m3u8DL_RE_GUI.sln --filter "FullyQualifiedName~GetValidFileName"
```

Expected: FAIL on the first theory — `CON.txt.bak` comes back unchanged.

- [ ] **Step 3: Split at the first dot**

In `UtilityService.cs`, replace the reserved-name check at the end of `GetValidFileName`:

```csharp
        // Windows matches DOS device names against the segment before the FIRST dot, so
        // "CON.txt.bak" is still reserved. Path.GetFileNameWithoutExtension strips only the
        // last extension and returned "CON.txt", which never matched.
        var firstDot = sanitized.IndexOf('.');
        var baseName = firstDot < 0 ? sanitized : sanitized[..firstDot];
        if (_reservedDeviceNames.Contains(baseName))
        {
            return $"_{sanitized}";
        }

        return sanitized;
```

- [ ] **Step 4: Run the tests to verify they pass**

```bash
dotnet test N_m3u8DL_RE_GUI.sln --filter "FullyQualifiedName~GetValidFileName"
```

Expected: PASS. `GetValidFileName_ShouldSanitizeReservedDosDeviceNames` must also still pass.

- [ ] **Step 5: Run the whole suite and commit**

```bash
dotnet test N_m3u8DL_RE_GUI.sln
git add N_m3u8DL_RE_GUI/Services/UtilityService.cs N_m3u8DL_RE_GUI.Tests/Unit/Services/UtilityServiceTests.cs
git commit -m "fix(filename): match reserved device names before the first dot

CON.txt.bak is reserved on Windows but GetFileNameWithoutExtension
returned CON.txt, so the guard never fired.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

## PART B — Contrast

## Task 6: Replace the four failing colour tokens

Assessment B measured 23 foreground/background pairs; ten failed. Commit 2f42467 fixed the update pill. Nine remain, and they collapse to four token changes. Every replacement value below was computed with a WCAG 2.1 relative-luminance calculator, not chosen by eye.

| Token | Old | Ratio | New | Ratio | Fixes |
|---|---|---|---|---|---|
| `BorderBrushCustom` | `#2A2A38` | 1.20 / 1.30 / 1.37 | **`#66667C`** | 3.03 / 3.29 / 3.47 | every textbox and GroupBox border (needs 3.0) |
| `AccentBrush` **as text** | `#5865F2` | 3.68 / 3.99 | **`#7A87FF`** | 5.44 / 5.89 | 13 GroupBox headers, selected tab, main title |
| `AccentHoverBrush` | `#6B78FF` | 3.65 | **`#4350D8`** | 6.22 | Download button on hover |
| Stop button background | `#E74C3C` | 3.82 | **`#C0392B`** | 5.44 | Stop button label |
| "Drop" label foreground | `#E74C3C` | 4.44 | **`#EC7063`** | 5.70 | Drop Video / Audio / Sub labels |

`AccentBrush` stays `#5865F2` where it is a **surface**, not text — the Download button fill (white on it measures 4.61, which passes) and the selected-tab indicator bar (non-text, 3.68 against Surface, passes). A new `AccentTextBrush` carries the lighter value for text use.

Note the hover direction: interactive states now get **darker**, matching the ramp commit 2f42467 established for the update pill. The old ramp got lighter and lost contrast exactly when the user was reaching for the control.

**Files:**
- Modify: `N_m3u8DL_RE_GUI/MainWindow.xaml` (resource block plus the three "Drop" labels)
- Create: `N_m3u8DL_RE_GUI.Tests/Unit/UI/XamlContrastTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces: a new resource `<SolidColorBrush x:Key="AccentTextBrush" Color="#7A87FF"/>`.

- [ ] **Step 1: Write the failing contrast test**

Create `N_m3u8DL_RE_GUI.Tests/Unit/UI/XamlContrastTests.cs`:

```csharp
#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using Xunit;

namespace N_m3u8DL_RE_GUI.Tests.Unit.UI;

/// <summary>
/// Reads the palette straight out of MainWindow.xaml and checks the pairs that actually
/// occur against WCAG 2.1. A colour edit that regresses contrast fails here.
/// </summary>
public class XamlContrastTests
{
    private static string XamlPath()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            var candidate = Path.Combine(dir.FullName, "N_m3u8DL_RE_GUI", "MainWindow.xaml");
            if (File.Exists(candidate))
                return candidate;
            dir = dir.Parent;
        }
        throw new FileNotFoundException("Could not locate MainWindow.xaml from " + AppContext.BaseDirectory);
    }

    /// <summary>Maps every x:Key'd SolidColorBrush to its hex value.</summary>
    private static Dictionary<string, string> Palette()
    {
        var text = File.ReadAllText(XamlPath());
        var result = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (Match m in Regex.Matches(
                     text, @"<SolidColorBrush\s+x:Key=""(?<key>[^""]+)""\s+Color=""(?<color>#[0-9A-Fa-f]{6})""\s*/>"))
        {
            result[m.Groups["key"].Value] = m.Groups["color"].Value;
        }

        return result;
    }

    private static double Channel(int v)
    {
        var c = v / 255.0;
        return c <= 0.03928 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
    }

    private static double Luminance(string hex)
    {
        hex = hex.TrimStart('#');
        var r = int.Parse(hex[..2], NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        var g = int.Parse(hex.Substring(2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        var b = int.Parse(hex.Substring(4, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        return 0.2126 * Channel(r) + 0.7152 * Channel(g) + 0.0722 * Channel(b);
    }

    public static double Contrast(string a, string b)
    {
        double la = Luminance(a), lb = Luminance(b);
        var (hi, lo) = la > lb ? (la, lb) : (lb, la);
        return (hi + 0.05) / (lo + 0.05);
    }

    [Fact]
    public void ContrastFormula_ShouldMatchTheKnownReferenceValues()
    {
        Assert.Equal(21.00, Contrast("#FFFFFF", "#000000"), 2);
        Assert.Equal(1.00, Contrast("#123456", "#123456"), 2);
    }

    [Fact]
    public void Palette_ShouldExposeEveryTokenTheseTestsReference()
    {
        var palette = Palette();
        foreach (var key in new[]
                 {
                     "BgDarkBrush", "SurfaceBrush", "CardBrush", "BorderBrushCustom",
                     "AccentBrush", "AccentTextBrush", "AccentHoverBrush", "AccentPressedBrush",
                     "TextPrimaryBrush", "TextSecondaryBrush", "CfAmberBrush",
                     "CommandBarBrush", "CommandTextBrush"
                 })
        {
            Assert.True(palette.ContainsKey(key), $"Palette is missing {key}");
        }
    }

    [Theory]
    // foreground token, background token, minimum ratio, where it is used
    [InlineData("TextSecondaryBrush", "CardBrush", 4.5, "field labels")]
    [InlineData("TextSecondaryBrush", "SurfaceBrush", 4.5, "unselected tab text")]
    [InlineData("TextPrimaryBrush", "CardBrush", 4.5, "input text and checkbox labels")]
    [InlineData("AccentTextBrush", "CardBrush", 4.5, "GroupBox headers, selected tab text")]
    [InlineData("AccentTextBrush", "SurfaceBrush", 4.5, "main title")]
    [InlineData("CommandTextBrush", "CommandBarBrush", 4.5, "command preview")]
    [InlineData("CfAmberBrush", "CardBrush", 4.5, "Cloudflare section")]
    public void TextPairs_ShouldMeetWcagAaNormalText(string fg, string bg, double minimum, string usage)
    {
        var palette = Palette();
        var ratio = Contrast(palette[fg], palette[bg]);

        Assert.True(ratio >= minimum, $"{fg} on {bg} ({usage}) is {ratio:F2}:1, needs {minimum}:1");
    }

    [Theory]
    [InlineData("BorderBrushCustom", "CardBrush", 3.0, "textbox and GroupBox borders")]
    [InlineData("BorderBrushCustom", "SurfaceBrush", 3.0, "Zone A and Zone D borders")]
    [InlineData("BorderBrushCustom", "BgDarkBrush", 3.0, "secondary button border")]
    [InlineData("AccentBrush", "CardBrush", 3.0, "focused textbox border")]
    public void NonTextPairs_ShouldMeetWcagAaUiBoundaries(string fg, string bg, double minimum, string usage)
    {
        var palette = Palette();
        var ratio = Contrast(palette[fg], palette[bg]);

        Assert.True(ratio >= minimum, $"{fg} on {bg} ({usage}) is {ratio:F2}:1, needs {minimum}:1");
    }

    [Theory]
    // White label on a coloured button fill, at every interaction state.
    [InlineData("#5865F2", "Download button, rest")]
    [InlineData("#4350D8", "Download button, hover")]
    [InlineData("#3E4ACB", "Download button, pressed")]
    [InlineData("#C0392B", "Stop button")]
    [InlineData("#1E8449", "update pill, rest")]
    [InlineData("#196F3D", "update pill, hover")]
    [InlineData("#145A32", "update pill, pressed")]
    public void WhiteOnButtonFills_ShouldMeetWcagAa(string fill, string usage)
    {
        var ratio = Contrast("#FFFFFF", fill);

        Assert.True(ratio >= 4.5, $"White on {fill} ({usage}) is {ratio:F2}:1, needs 4.5:1");
    }

    [Fact]
    public void InteractionStates_ShouldNeverReduceContrast()
    {
        // Hover and pressed must darken, not lighten. The original ramps got lighter and
        // lost contrast exactly when the user was reaching for the control.
        Assert.True(Contrast("#FFFFFF", "#4350D8") > Contrast("#FFFFFF", "#5865F2"),
            "Download hover must not be lower-contrast than its resting state");
        Assert.True(Contrast("#FFFFFF", "#196F3D") > Contrast("#FFFFFF", "#1E8449"),
            "Update pill hover must not be lower-contrast than its resting state");
    }

    [Fact]
    public void DropLabels_ShouldMeetWcagAaAgainstTheCardBackground()
    {
        var palette = Palette();
        var text = File.ReadAllText(XamlPath());

        // The three "Drop *" labels are coloured inline rather than via a token.
        Assert.DoesNotContain("Foreground=\"#E74C3C\"", text);
        Assert.True(Contrast("#EC7063", palette["CardBrush"]) >= 4.5);
    }
}
```

- [ ] **Step 2: Run it to verify it fails**

```bash
dotnet test N_m3u8DL_RE_GUI.sln --filter "FullyQualifiedName~XamlContrastTests"
```

Expected: FAIL — `AccentTextBrush` is missing from the palette, the border pairs report 1.20/1.30/1.37, and the inline `#E74C3C` is still present.

- [ ] **Step 3: Update the palette**

In `MainWindow.xaml`'s `Window.Resources`, replace the colour brush block:

```xml
        <!-- Color Brushes. Ratios in comments are WCAG 2.1, verified by XamlContrastTests. -->
        <SolidColorBrush x:Key="BgDarkBrush" Color="#0D0D0F"/>
        <SolidColorBrush x:Key="SurfaceBrush" Color="#141418"/>
        <SolidColorBrush x:Key="CardBrush" Color="#1C1C22"/>
        <!-- #2A2A38 measured 1.20:1 on Card — a border nobody could see. -->
        <SolidColorBrush x:Key="BorderBrushCustom" Color="#66667C"/>
        <!-- Accent as a SURFACE (button fill, tab indicator). White on it is 4.61:1. -->
        <SolidColorBrush x:Key="AccentBrush" Color="#5865F2"/>
        <!-- Accent as TEXT. #5865F2 was 3.68:1 on Card; this is 5.44:1. -->
        <SolidColorBrush x:Key="AccentTextBrush" Color="#7A87FF"/>
        <!-- Hover/pressed darken so contrast rises on interaction, never falls. -->
        <SolidColorBrush x:Key="AccentHoverBrush" Color="#4350D8"/>
        <SolidColorBrush x:Key="AccentPressedBrush" Color="#3E4ACB"/>
        <SolidColorBrush x:Key="TextPrimaryBrush" Color="#F2F2F8"/>
        <SolidColorBrush x:Key="TextSecondaryBrush" Color="#8888A8"/>
        <SolidColorBrush x:Key="CfAmberBrush" Color="#F39C12"/>
        <SolidColorBrush x:Key="CommandBarBrush" Color="#0A0A0D"/>
        <SolidColorBrush x:Key="CommandTextBrush" Color="#A8C0FF"/>
        <SolidColorBrush x:Key="DropLabelBrush" Color="#EC7063"/>
```

- [ ] **Step 4: Point the text usages at AccentTextBrush**

Three places read the accent as text. Change each:

1. `GroupBoxStyle`'s foreground setter:
```xml
            <Setter Property="Foreground" Value="{StaticResource AccentTextBrush}"/>
```

2. `LeftTabItemStyle`'s `IsSelected` trigger:
```xml
                            <Trigger Property="IsSelected" Value="True">
                                <Setter TargetName="TabBorder" Property="Background" Value="#1C1C22"/>
                                <Setter TargetName="TabBorder" Property="BorderThickness" Value="3,0,0,0"/>
                                <Setter Property="Foreground" Value="{StaticResource AccentTextBrush}"/>
                            </Trigger>
```

3. The main title `TextBlock` in Zone A:
```xml
                        <TextBlock Text="N_m3u8DL-RE GUI" FontSize="18" FontWeight="Bold" Foreground="{StaticResource AccentTextBrush}"/>
```

- [ ] **Step 5: Fix the Stop button and the Drop labels**

Change the Stop button's inline background:

```xml
                        <Button x:Name="Button_Stop" Content="⏹ S_top"
                                Style="{StaticResource SecondaryButtonStyle}"
                                Background="#C0392B" Foreground="#FFFFFF" BorderThickness="0"
```

Replace `Foreground="#E74C3C"` with `Foreground="{StaticResource DropLabelBrush}"` on all three "Drop" labels (Drop Video, Drop Audio, Drop Sub) in the Media tab.

Also update the `TextBoxStyle` invalid trigger to use the same token so the error colour is defined once:

```xml
                <Trigger Property="Tag" Value="invalid">
                    <Setter Property="BorderBrush" Value="{StaticResource DropLabelBrush}"/>
                </Trigger>
```

- [ ] **Step 6: Run the tests to verify they pass**

```bash
dotnet test N_m3u8DL_RE_GUI.sln --filter "FullyQualifiedName~XamlContrastTests"
```

Expected: PASS, 24 tests. If `NonTextPairs` still fails on `AccentBrush`/`CardBrush`, that pair is 3.68 and passes — recheck the palette parse rather than the colour.

- [ ] **Step 7: Run the whole suite, then look at it**

```bash
dotnet build N_m3u8DL_RE_GUI.sln -c Debug --no-incremental
dotnet test N_m3u8DL_RE_GUI.sln
```

Expected: 0 warnings, 0 errors; PASS. `XamlAccessibilityTests` must still be green.

Then launch the app and confirm the lighter border does not read as noisy at rest. A 3:1 border on a dark card is a visible change; if it looks heavy, reduce `BorderThickness` to `1` where it is currently higher rather than walking the colour back below 3.0.

- [ ] **Step 8: Commit**

```bash
git add N_m3u8DL_RE_GUI/MainWindow.xaml N_m3u8DL_RE_GUI.Tests/Unit/UI/XamlContrastTests.cs
git commit -m "fix(a11y): bring the remaining nine colour pairs up to WCAG AA

Borders measured 1.20:1 against the card they sat on, and accent-as-text
measured 3.68:1. Splits the accent into surface and text tokens, darkens
the interaction ramps so contrast rises rather than falls on hover, and
adds XamlContrastTests so a future colour edit cannot regress this.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

## PART C — Option Conflicts

## Task 7: Make overridden and ignored options visible

Three settings lie to the user:
1. `CheckBox_AudioOnly` rewrites `SelectAudio` to `"best"` and `DropVideo` to `".*"` at build time, three tabs away, while `TextBox_SelectAudio` and `TextBox_DropVideo` keep displaying what the user typed.
2. Cloudflare mode reads seven controls; every option on the Download, Security, Media, Live and Advanced tabs is discarded, with nothing in the UI saying so.
3. `Combo_UILanguage` is labelled "UI Language" but feeds `--ui-language` to the downloader. It does not change this GUI's language.

**Files:**
- Modify: `N_m3u8DL_RE_GUI/MainWindow.xaml` (Media tab labels, Advanced tab label, a CF banner)
- Modify: `N_m3u8DL_RE_GUI/MainWindow.xaml.cs` (`CheckBoxChanged`, plus a new `SyncDependentControlStates`)
- Modify: `N_m3u8DL_RE_GUI.Core/DownloadOptions.cs:171-182`
- Test: `N_m3u8DL_RE_GUI.Tests/Unit/Core/DownloadOptionsLegacyTests.cs`

**Interfaces:**
- Consumes: `ArgsBuilder.Build(DownloadOptions)` (unchanged).
- Produces: `private void MainWindow.SyncDependentControlStates()` — called from `CheckBoxChanged` and at the end of `Window_Loaded`.

- [ ] **Step 1: Flip the AudioOnly characterisation test**

In `DownloadOptionsLegacyTests.cs`, replace `AudioOnly_Getter_DoesNotRecogniseTheDropPatternTheGuiActuallyWrites`:

```csharp
    [Fact]
    public void AudioOnly_Getter_ShouldRecogniseBothDropSpellings()
    {
        // MainWindow writes DropVideo = ".*"; the legacy setter writes "all". Both mean
        // "drop every video track", so the getter must accept either.
        Assert.True(new DownloadOptions { SelectAudio = "best", DropVideo = ".*" }.AudioOnly);
        Assert.True(new DownloadOptions { SelectAudio = "best", DropVideo = "all" }.AudioOnly);
        Assert.False(new DownloadOptions { SelectAudio = "best", DropVideo = "1080p" }.AudioOnly);
        Assert.False(new DownloadOptions { SelectAudio = null, DropVideo = ".*" }.AudioOnly);
    }
```

- [ ] **Step 2: Run it to verify it fails**

```bash
dotnet test N_m3u8DL_RE_GUI.sln --filter "FullyQualifiedName~AudioOnly"
```

Expected: FAIL — the `.*` case returns false.

- [ ] **Step 3: Accept both spellings**

In `DownloadOptions.cs`:

```csharp
    [Obsolete("Use SelectAudio + DropVideo pattern instead")]
    public bool AudioOnly
    {
        // MainWindow writes ".*", the setter below writes "all". Both are "drop all video".
        get => SelectAudio == "best" && (DropVideo == "all" || DropVideo == ".*");
        set
        {
            if (value)
            {
                SelectAudio = "best";
                DropVideo = "all";
            }
        }
    }
```

- [ ] **Step 4: Run it to verify it passes**

```bash
dotnet test N_m3u8DL_RE_GUI.sln --filter "FullyQualifiedName~AudioOnly"
```

Expected: PASS.

- [ ] **Step 5: Add the Cloudflare scope banner to the XAML**

In the Cloudflare GroupBox on the Network tab, directly under `CheckBox_BypassCF`, add:

```xml
                                <Border x:Name="Border_CfScopeWarning" Visibility="Collapsed"
                                        Background="#2A2113" BorderBrush="{StaticResource CfAmberBrush}"
                                        BorderThickness="1" CornerRadius="3" Padding="8,6" Margin="0,0,0,8">
                                    <TextBlock TextWrapping="Wrap" FontSize="11"
                                               Foreground="{StaticResource CfAmberBrush}"
                                               AutomationProperties.Name="Cloudflare Mode Scope Warning"
                                               Text="Cloudflare mode runs a Python script instead of N_m3u8DL-RE. It uses only the input URL, save folder, save name, and the fields in this section — options on the Download, Security, Media, Live and Advanced tabs are ignored."/>
                                </Border>
```

- [ ] **Step 6: Add the dependent-state sync to the code-behind**

In `MainWindow.xaml.cs`, add the method and call it from `CheckBoxChanged`:

```csharp
        private void CheckBoxChanged(object sender, RoutedEventArgs e)
        {
            SyncDependentControlStates();
            GetParameter();
        }

        /// <summary>
        /// Reflects option dependencies in the UI instead of silently overriding them at
        /// build time. Every field disabled here is one BuildArgsRE would otherwise
        /// discard while the user kept looking at the value they typed.
        /// </summary>
        private void SyncDependentControlStates()
        {
            var audioOnly = CheckBox_AudioOnly?.IsChecked == true;
            if (TextBox_SelectAudio != null)
            {
                TextBox_SelectAudio.IsEnabled = !audioOnly;
                TextBox_SelectAudio.ToolTip = audioOnly
                    ? "Overridden by Audio Only, which forces the best audio track."
                    : "Regex selecting which audio track to download";
            }
            if (TextBox_DropVideo != null)
            {
                TextBox_DropVideo.IsEnabled = !audioOnly;
                TextBox_DropVideo.ToolTip = audioOnly
                    ? "Overridden by Audio Only, which drops every video track."
                    : "Regex selecting which video tracks to discard";
            }

            var bypassCf = CheckBox_BypassCF?.IsChecked == true;
            if (Border_CfScopeWarning != null)
                Border_CfScopeWarning.Visibility = bypassCf ? Visibility.Visible : Visibility.Collapsed;

            // The dependent CF fields are meaningless until the mode is on.
            foreach (var control in new System.Windows.Controls.Control?[]
                     { Combo_CFImpersonate, TextBox_CFReferer, TextBox_CFCookie, CheckBox_CFKeepSegs })
            {
                if (control != null)
                    control.IsEnabled = bypassCf;
            }
        }
```

Add the call at the end of `Window_Loaded`'s `finally` block, immediately before `GetParameter();`:

```csharp
                SyncDependentControlStates();
```

- [ ] **Step 7: Rename the mislabelled language control**

In the Advanced tab, change the label and the control's accessible name:

```xml
                                    <TextBlock Grid.Column="3" Text="DL Language" Style="{StaticResource LabelStyle}"/>
                                    <ComboBox Grid.Column="4" x:Name="Combo_UILanguage"
                                              AutomationProperties.Name="Downloader Console Language"
                                              ToolTip="Language for N_m3u8DL-RE's own console output. This does not change the GUI's language."
                                              SelectedIndex="0" SelectionChanged="Combo_UILanguage_SelectionChanged">
```

Leave the `x:Name` as `Combo_UILanguage` — `MainWindowConfigMapper` persists it under the key `"UILanguage"` and renaming would orphan existing configs.

- [ ] **Step 8: Build, test, and check by hand**

```bash
dotnet build N_m3u8DL_RE_GUI.sln -c Debug --no-incremental
dotnet test N_m3u8DL_RE_GUI.sln
```

Expected: 0 warnings, 0 errors; PASS. `XamlAccessibilityTests` must still be green — the new `TextBlock` inside the banner is not an interactive element, but the `Border` carries a name on its child.

Manual check: tick **Audio Only** on the Download tab, then open the Media tab. `Select Audio` and `Drop Video` are greyed with an explanatory tooltip. Tick **Enable Cloudflare Bypass** on the Network tab: the amber banner appears and the four CF fields become enabled; untick it and they grey out.

- [ ] **Step 9: Commit**

```bash
git add N_m3u8DL_RE_GUI/MainWindow.xaml N_m3u8DL_RE_GUI/MainWindow.xaml.cs N_m3u8DL_RE_GUI.Core/DownloadOptions.cs N_m3u8DL_RE_GUI.Tests/Unit/Core/DownloadOptionsLegacyTests.cs
git commit -m "fix(ux): show which options override or ignore each other

Audio Only silently rewrote two fields three tabs away while they kept
displaying the discarded input; Cloudflare mode discarded five tabs of
settings with no indication; and 'UI Language' set the downloader's
console language, not the GUI's.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

## Deferred — needs its own plan

**Information architecture.** The critique's central finding was that six tabs are named after `N_m3u8DL-RE --help`'s argument categories rather than after anything a person wants to do, and that 4 of 96 controls can complete the primary task. Restructuring into task-named groups with progressive disclosure is a design problem, not a coding one: it needs a `/impeccable shape` pass to decide the grouping and what hides behind an Options affordance before any plan can specify tasks. Do not fold it into this plan.

**Structural cleanup**, which can ride along with the IA work since it touches the same file:
- `BuildArgsRE` (`MainWindow.xaml.cs:183`) and `BuildDownloadOptions` (`:295`) are ~100 lines of identical field mapping differing only in `ExePath` and `Input`. Have one call the other.
- `StartExecutableWithArguments` is dead code.
- `UtilityService.Dispose()` is empty but the class declares `IDisposable`.
- `GitHubUpdateCheckService` hard-codes a `static HttpClient`, so its HTTP paths cannot be unit-tested. Inject an `HttpMessageHandler`.
- `CleanStaleTempBatchFiles` sweeps `cf_dl_*.bat` but not the `batch_*.bat` files `BatchScriptService` creates.
- `Window_Closing` saves to the relative path `"config.txt"`, which depends on the process working directory.
- `CheckBox_NoAnsiColor` became vestigial when commit f4178d3 forced `--no-ansi-color` on the GUI-run path; remove it from the XAML.

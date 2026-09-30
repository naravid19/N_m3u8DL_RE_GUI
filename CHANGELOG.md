# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

---

## [Unreleased]

## [2.1.6] - 2026-09-29

### Added

- **`publish.bat` Builds The Release Folder And Zip**: the old script was git-ignored and dropped from `dev`, so the release procedure could vanish on a merge; its version default (`2.1.4`) had also drifted two patches behind the binary it wrapped, and it never copied `extension/`. The new one takes the version from `AppVersion` in `Directory.Build.props` (there is no version argument to forget) and refuses to package an exe whose `FileVersion` disagrees. It copies the engine, ffmpeg, the Cloudflare script and the extension without its tests, `package.json` or `node_modules`, ships no `.pdb` files and no `config.json`/`config.txt`, and writes `Publish\N_m3u8DL_RE_GUI_v<version>\` plus a zip with one top-level folder and forward-slash entry names (Windows PowerShell 5.1's `ZipFile` writes backslashes, so the zip is made with the system `tar`). It assembles everything in `Publish\.staging` and swaps it in only after every check passes: the previous release is renamed aside first (which fails cleanly if the folder is open in Explorer or the exe is running), the new one is moved in and checked again, and any failure puts the previous release back untouched. A missing prerequisite stops it before any build. It is no longer git-ignored. `/nopause` skips the Explorer window and the final pause for use from other scripts.
- **Choose The Abyss/Hydrax Quality**: the GUI always downloaded the largest rendition. When a video offers more than one, a person pressing GO now gets a small picker (largest first, with size and codec); a download started by a browser capture still takes the largest, since nobody is at the keyboard. `AbyssSourceChoice` holds the ordering and labels; `StreamPickerWindow` and the new `AbyssQualityWindow` share `Resources/PickerStyles.xaml`.
- **Simplified Chinese README**: `README.zh-CN.md` mirrors the English README (linked from the top of each); the English README also gained the version badge and a working *Getting Started* link, and its license link now points at `LICENSE`, the file that exists.
- **Browser Support Note**: the native host is registered for Chrome, Edge and Brave (`NativeHostManifest`).
- **Full GUI Multi-Language Localization (i18n/l10n with Hot-Reload)**:
  - **3 Supported Languages (`en-US`, `zh-CN`, `zh-TW`)**: Shipped comprehensive resource dictionaries for English (Default), Simplified Chinese (简体中文), and Traditional Chinese (繁體中文), with 100% key parity enforced by automated unit tests.
  - **Live Dynamic Hot-Switching**: Bound all UI controls, tabs, group boxes, labels, buttons, dialogs, and tooltips to `{DynamicResource Str_*}` via `LanguageService`, switching the interface instantly upon selecting a language with zero restart required. Runtime status messages (import, batch, Abyss, Cloudflare bypass), the resume banner (including "x minutes ago") and the update-check status go through the same dictionaries via one `Localize(key, fallback, args)` helper that `LanguageUsageParityTests` also checks; the diagnostic log stays English.
  - **System Culture Auto-Detection**: Automatically detects the host operating system's UI culture on first launch (`zh-Hans*` / `zh-CN` / `zh-SG` -> `zh-CN`, `zh-Hant*` / `zh-TW` / `zh-HK` / `zh-MO` -> `zh-TW`, other cultures -> `en-US`).
  - **Preference Persistence**: Saves the chosen `GuiLanguage` in `config.json` via `JsonConfigService` and restores it reliably on next launch.

- **Deep JSON.parse & XHR Interception (MAIN World)**: Added `deep-detect.js` running in the page execution context (`world: "MAIN"`) to uncover hidden manifest URLs (`.m3u8`, `.mpd`, `.ism`), inline `#EXTM3U` playlist text blobs, and 16-byte AES-128 keys delivered inside JSON API responses or XHR arraybuffers.
- **Ad & Tracker CDN Blocklist**: Pure substring blocklist in `lib/ad-blocklist.js` silently drops video ad prerolls, tracking beacons, and analytics traffic from DoubleClick, Google Ad Services, imasdk, Criteo, Taboola, and Outbrain before classification.
- **Auto Page Title Extraction & Windows Sanitizer**: `content.js` reads page titles from `og:title`, `twitter:title`, and `document.title`, while `lib/format.js` strips Windows-forbidden characters (`\/:*?"<>|`) and zero-width spaces to attach clean filename hints for automated GUI save name fill.
- **Browser extension bumped to v1.4.3**.
- **Content-Disposition Header Classification**: `background.js` and `classify.js` now parse `Content-Disposition: attachment; filename="..."` to extract the true filename when a stream URL has no extension.
- **Deduplication Policy**: Added `deduplicateStreams` to collapse token-rotating URLs that resolve to the same canonical file path.
- **Comprehensive Unit Tests**:
  - `extension/test/deep-detect.test.js`: Full coverage for JSON.parse / XHR arraybuffer interception, inline M3U8 blob URL generation, and single-hooking guards.
  - `extension/test/ad-blocklist.test.js`: Validates ad CDN substring matches and silent classification drops.
  - `extension/test/impersonate-profiles.test.js`: Validates User-Agent pattern regexes, descending minVersion sorting, and Edge-before-Chrome priority invariants.
  - `N_m3u8DL_RE_GUI.Tests/Unit/Services/NativeHostRegistrarTests.cs`: Validates manifest creation and exception-safety during Chrome host registration.
  - `tests/test_probe_media_info.py`: Validates media info extraction from FFmpeg outputs and graceful handling of missing binaries.
- **Detection For AVI, ASF, MPEG-PS And Audio-Only Containers**: `.avi`, `.wmv`, `.asf`, `.divx`, `.f4v`, `.mpeg` and `.mpg` classify as Media; `.weba` and `.wma` as Audio. Previously dropped without a trace.
- **`extension/test/platform-compat.test.js`**: guards the declared browser floor and the incognito mode, and fails if any shipped script reintroduces an import-attribute dependency that would raise the floor again.
- **`AbyssDownloadServiceTests`**: the Abyss downloader's 325-line download, resume and merge orchestration had no coverage — only two of its static helpers did. It already accepted an injected `HttpClient`, so the whole loop is now driven from tests through a stub handler: chunk ordering, short final chunks, resume skipping valid segments, re-fetching wrong-sized ones, atomic writes, temp-directory determinism and cleanup, retry, exhausted retry preserving resume state, cancellation, header shaping, and progress accounting.
- **1-Click Download From The Browser Extension (Native Messaging)**: The popup now shows a `⬇ Download` button beside `📋 Copy as cURL`. It sends the same payload `toCurl()` already produces to the GUI over Chrome native messaging, so the GUI applies it with the existing `CurlCommandParser` → `CaptureApplyPlan` path — no second wire format. The GUI registers itself as the host (`com.nm3u8dlre.gui`) under HKCU on every normal startup, so a moved or re-extracted copy repairs its own registration with no installer. Captures land in `%LOCALAPPDATA%\N_m3u8DL_RE_GUI\inbox`, which doubles as the download queue: a capture that arrives mid-download waits and starts on its own when the current download finishes. If the host is not registered the button hides itself and the clipboard flow is unchanged.
- **Browser extension bumped to v1.4.1**, and its ID is now pinned by a `key` field in `manifest.json`.
- **Cloudflare-Bypass Segment Resume**: `download_segments()` scans `--seg-dir` for already-downloaded, non-empty segments before starting and skips them, so stopping a download and starting the exact same one again (same URL and Save Name) continues instead of restarting at 0%. The job directory is now a deterministic hash of URL + Save Name (`CfCommandBuilder.ComputeJobId`) rather than a random GUID per click, so two attempts at the same download land in the same folder. Segment writes are atomic (`.tmp` → `os.replace`), so a process killed mid-write never leaves a corrupt segment that would be mistaken for a complete one.
- **Abyss / Hydrax Segment Resume**: `AbyssDownloadService` now uses a deterministic temp directory name (video slug + quality label, not a per-run timestamp) and verifies each `.part` chunk's byte size against its expected length before trusting it as complete, so a stopped download resumes from its exact chunk offset instead of restarting. Chunk writes are atomic (`.part.tmp` → move).
- **Proxy Server Integration Test Suite (`tests/test_proxy_routes.py`)**: Real-subprocess tests against `m3u8_cf_bypass.py --serve` covering the route round trip, token rejection, SSRF host rejection, `Range` forwarding, and the stdin-close shutdown handshake.
- **Resume End-to-End Test (`tests/test_resume_end_to_end.py`)**: Runs the real script as a subprocess against a local origin that always fails two segments, asserts the partial run's segments survive on disk (exit 3), then reruns against a healthy origin reusing the same `--seg-dir` and asserts the download completes and the segment directory is cleaned up.
- **`extension/lib/inflight-headers.js`**: Pure header-payload and TTL/size-prune logic extracted from `background.js`, tested independently of the `chrome.webRequest` API.
- **`CfEngineSelector` (`N_m3u8DL_RE_GUI.Core`)**: Pure decision function backing the Auto engine table.
- **`extension/test/module-load.test.js`**: Regression guard that `import()`s `popup.js` and `background.js` directly, catching a missing/renamed named export before it can silently kill an entry point's entire script.
- **`extension/test/ad-blocklist-anchoring.test.js`**: Regression guard for the hostname-anchoring fix above — reproduces the exact false-positive/false-negative pairs and fails if substring matching ever creeps back in.
- **`N_m3u8DL_RE_GUI.Tests/Unit/Services/LocalizationCoverageTests.cs`**: Fails the build if `MainWindow.xaml` ever ships a hardcoded `ToolTip="..."` instead of a `{DynamicResource Str_*}` binding.
- **`N_m3u8DL_RE_GUI.Tests/Unit/Services/ImpersonateProfileParityTests.cs`**: Guards that the five Cloudflare impersonate profiles (`chrome`, `chrome120`, `chrome131`, `edge101`, `safari17_0`) stay in sync across the four places they are declared independently — the extension, `CaptureApplyPlan.cs`, `MainWindow.xaml`, and `m3u8_cf_bypass.py` — since no single source can be shared across JavaScript, C#, XAML, and Python.
- **`extension/test/background-dispatch.test.js`**: Source-read guard asserting the detection dispatch table stays a `Map`, never a plain object indexed with brackets.

- **Cloudflare Pipeline Hardening & Correctness Fixes**:
  - **Capture Apply Plan (`N_m3u8DL_RE_GUI.Core.Capture.CaptureApplyPlan`)**: Full state replacement contract for GUI fields upon capture paste, resetting previously owned controls and routing `CfReferer`, `CfCookie`, `CfImpersonate`, and `EnableCfBypass` deterministically (fixes H2, C1).
  - **Shared Header Policy & Parity Test Suite (`HeaderPolicy`, `header-policy.js`)**: Shared JSON fixture (`header-policy-cases.json`) ensuring parity between .NET and Node.js header filtering, dropping transport and browser fingerprint pseudo-headers (`range`, `accept-encoding`, `sec-*`, etc.) while forwarding user and authentication headers.
  - **Wire Format v3 (`extension/lib/curl.js`)**: Extends cURL serialization with directives `# nre-page-url:`, `# nre-impersonate:`, `# nre-cf: 1`, `# nre-select-audio:`, and `# nre-select-subtitle:`.
  - **Browser Cookie Store Merging (`extension/lib/cookies.js`)**: Merges captured request headers with full session cookies retrieved via `chrome.cookies.getAll({ url })` with request header precedence on name collision.
  - **TLS Fingerprint Profile Resolver (`extension/lib/impersonate.js`)**: Maps browser User-Agent to `curl_cffi` impersonation fingerprints (`chrome131`, `chrome120`, `edge101`, `safari17_0`, `chrome`).
  - **Multi-Threaded Segment Downloader (`m3u8_cf_bypass.py`)**: Thread-local `curl_cffi` sessions downloading segments in parallel with ThreadPoolExecutor, contiguous prefix verification, and per-job isolated temporary segment directories (`cf_segments/<jobId>`).
  - **Standardized Process Exit Code Contract**:
    - `0`: Success (green status, reveal folder button).
    - `1`: General error (red status, opens log drawer).
    - `2`: `curl_cffi` missing (install guidance dialog).
    - `3`: Partial download (amber status, preserves downloaded contiguous prefix video and reveals folder).
    - `4`: Encrypted stream (`#EXT-X-KEY` with `METHOD!=NONE`) (amber status, routes user to N_m3u8DL-RE with `--key`).
    - `5`: Origin access denied 401/403 (amber status, highlights credential/cookie validation).
  - **Local Reverse Proxy & Manifest Rewriter (`--serve`)**: Ephemeral HTTP server (`127.0.0.1:0`) with random authentication tokens and SSRF origin protection; rewrites HLS (`#EXT-X-STREAM-INF`, `#EXT-X-KEY`, segments) and DASH (`<BaseURL>`) manifests to route requests through TLS impersonation proxies.
  - **GUI Cloudflare Engine Selector (`Combo_CFEngine`)**: Options for `Auto (HLS direct / DASH proxy)`, `CF Direct (HLS only)`, and `CF Proxy + RE (DASH / MSS / DRM)`.
  - **Parameter Preview Credential Masking (`CfCommandBuilder.BuildMaskedCommand`)**: Masks sensitive cookie values in the UI parameters box while preserving full credentials in background execution scripts.
- **Capture Contract v2 (`N_m3u8DL_RE_GUI.Core.Capture`)**:
  - **Save Name Auto-Population (`# nre-save-name:`)**: Browser extension sanitizes active tab title (stripping file-unsafe characters) and appends `# nre-save-name: <title>` to clipboard cURL commands. GUI automatically populates the `TextBox_Title` (Save Name) field on paste.
  - **Referer Mismatch Surfacing (`# nre-warn:`)**: Browser extension identifies conflicting Referer origins in multi-stream selections and emits `# nre-warn: Referer mismatch — <count> stream(s) differ from primary origin <origin>`. GUI reads the directive and surfaces the warning in the status bar (C2 bug fix).
  - **Amber Status Color**: Added dedicated warning state (`WarnBrush` `#F0A030`) in GUI `TextBlock_Status` for non-blocking warnings.
  - **Card Keyboard Handler Guard**: Explicit event target verification (`e.target === card`) in browser extension card keydown listener, preventing accidental cURL copying or checkbox toggling when activating interactive buttons via keyboard (C1 bug verification).
- **Automated Test Suite**: Expanded to 1,024 automated tests across three platforms (744 .NET unit tests, 260 Node.js extension tests, 20 Python script unit tests).

---

### Changed

- **Popup Visual Refinement**: the popup's copy no longer contradicts its own primary action — the always-visible footer taught "Copy as cURL then Paste from browser" as the instruction, and Abyss cards read "try Copy as cURL" directly above their own Download button; both now describe the Download path with the clipboard route named as the fallback. Download becomes a full-width primary action with `Copy as cURL` and `URL only` in a lighter row beneath, instead of three co-equal buttons outweighing the URL that is the card's actual content. The ten emoji standing in for icons are replaced by one stroked set that inherits the colour of the text it labels. Colour and spacing now come from tokens — the stylesheet had reasoned scales for type, space and radius but 95 colour literals and 47 off-scale spacing values — with tests holding all three invariants closed.
- **The browser extension's ID is now fixed.** An unpacked extension's ID is otherwise derived from its install path, which would make the native host's `allowed_origins` entry correct only on the machine that wrote it. **Existing users must remove and re-add the unpacked extension once** (`chrome://extensions` → Remove → Load unpacked). Nothing durable is lost: captured streams live in `chrome.storage.session` and are discarded when the browser closes regardless.
- **Cleared dead exports and duplication**: removed `deduplicateStreams` and `sanitizeWindowsFilename` from `extension/lib/` (both had zero production callers), de-duplicated `formatBitrate` into a single implementation, collapsed two identical `background.js` message handlers into one lookup table, and inlined the `DownloadService.StartTrackedProcessWithExitCodeAsync` `redirect` parameter, which was `true` at every call site.

---

### Removed

- **Extension Key Sniffing**: the MAIN-world script reported 16-byte XHR responses as `DEEP_KEY_DETECTED`, and the background page stored them in `detectedKeys`, but nothing ever read them and a sniffed key could belong to a different Stream. The XHR hook is gone, so the page's `XMLHttpRequest` is no longer patched; `JSON.parse` manifest detection is unchanged.
- **Dead *No ANSI Color* Checkbox And A Machine-Specific Python Probe**: the GUI always forces `--no-ansi-color` (it parses redirected output), so the checkbox did nothing; it is removed with its saved-config key and strings. The Python search no longer looks in `~/.workbuddy`.

### Fixed

- **The Cloudflare Proxy Runner And The Python Probe Are Testable**: starting `m3u8_cf_bypass.py --serve`, the `PROXY_READY` handshake, the stdout drain and the shutdown lived inside `MainWindow.xaml.cs`, reachable only through a live window. They are now `CfProxyHost` and `PythonLocator` in `N_m3u8DL_RE_GUI.Core` (about 250 lines out of the window), covered by tests that run a stand-in process speaking the same protocol, and one that runs the real script. The pipe-full stall fix is now proven by a 30,000-line chatty proxy rather than by reading source. Behaviour is unchanged; `MainWindow.xaml.cs` is down by roughly 220 lines.
- **Two Swallowed Failures Now Leave A Trace**: a Python candidate that failed the `curl_cffi` probe was dropped without a word, so "No Python interpreter with curl_cffi found" gave nothing to go on; it is now traced per candidate. A finished Abyss download that could not remove its temp folder logged nothing; it now writes a warning to the run log.
- **Unticking *Delete After Done* Or *Auto Subtitle Fix* Had No Effect**: N_m3u8DL-RE defaults both options to `true` (`CommandInvoker`: `.WithDefault(true)`), but the GUI only wrote the flag when the box was checked, so an unchecked box sent nothing and RE still deleted the temp files and fixed subtitles. An unchecked box now sends `--del-after-done false` / `--auto-subtitle-fix false`.
- **A Browser Capture Inherited The Previous Capture's Key, Cloudflare Bypass And TLS Profile**: a capture starts its download immediately, so one Cloudflare capture made every later capture run through the Python engine, and a Key typed for one video was sent with the next. Applying a Capture now resets the Key, the bypass checkbox and the impersonation profile; the user's proxy is kept. `CaptureApplyPlan.EnableCfBypass` is now a plain `bool` (off unless the capture asks for it).
- **A Late Title Fetch Could Overwrite A Capture's Save Name**: on a cold start `Window_Loaded` awaited the clipboard URL's title while the Capture Inbox applied a browser capture and started its download; the fetch then replaced `nre-save-name`. A fetch that was in flight when a capture was applied now discards its result.
- **The Cloudflare Proxy Engine Could Stall Or Truncate A Download**: (1) RE was still given the user's custom/system proxy for its loopback traffic; both are now off on that path (the bypass proxy has no upstream-proxy option, and the log says so). (2) Nothing read the proxy's stdout after `PROXY_READY`, so a full pipe could block it. It is now drained. (3) The proxy forwarded the origin's `Content-Length` even when curl_cffi had de-chunked or decoded the body, so a keep-alive client either waited for bytes that never came (chunked) or was cut short (gzip). The length is now forwarded only when it is reliable; otherwise the body ends by closing the connection.
- **The Direct Cloudflare Downloader Silently Produced Broken fMP4 / Audio-less HLS**: it concatenated every URI line, dropping `#EXT-X-MAP` init segments, ignoring `#EXT-X-MEDIA` audio renditions and treating `#EXT-X-BYTERANGE` segments as whole files. It now refuses such manifests with exit code `6`, and *Auto* engine mode hands the job to the proxy + N_m3u8DL-RE path automatically (a pinned *Direct* engine reports it instead).
- **A Custom Muxer Path Reached The Downloader As Bare `C`**: `--mux-after-done` is a colon-delimited option and N_m3u8DL-RE's `ComplexParamParser` stops at the first `:` that is not escaped, so a *Bin Path* of `C:\Program Files\MKVToolNix\mkvmerge.exe` was read as `C`. RE then *overwrites* the `--mkvmerge-binary-path` the GUI passes separately with that truncated value, so filling the field made mkvmerge unfindable instead of pointing at it and muxing failed. The drive letter is now escaped as `C\:` — the form RE's own `--morehelp mux-after-done` documents — and `bin_path` is emitted last within the option, because RE's unescaper drops every backslash it has collected each time it meets an escaped colon, which mangles a path ending in `\` when another `:key=value` pair follows it.
- **The README Overstated What The Parameters Preview Masks**: the docs said the preview bar masks credentials as you enter them. It does not, deliberately — the bar mirrors the real command so **📋 Copy** yields something that runs, and masking applies to the snapshot written when a Cloudflare-bypass run starts. The two claims now describe that, rather than masking the live bar and leaving Copy producing an unrunnable command.
- **Extension Popup UX/UI Refinement (Browser Extension v1.4.5)**:
  - **CSS Specificity [hidden] Override Bug**: `.btn` (`display: inline-flex;`) and `.bulk-bar` (`display: flex;`) previously overrode the browser's User-Agent `hidden` attribute due to the missing `[hidden] { display: none !important; }` rule in `popup.css`. This caused `Download` to stay visible when GUI was offline, squashing all four action buttons into a single 400px row with awkward text wraps ("Copy \n as \n cURL"), and kept the `0 selected` bulk bar permanently visible. Fixed with explicit `[hidden] !important` and `white-space: nowrap`.
  - **Dynamic Action Hierarchy & CTA Promotion**: Stream cards now structure actions in a clean two-row hierarchy: when the GUI is online, Row 1 contains full-width `Download` (accent fill) and Row 2 contains `Copy as cURL`, `URL only`, and `Qualities`; when offline, `Download` is hidden and `Copy as cURL` promotes to full-width primary CTA in Row 1, with Row 2 containing `URL only` and `Qualities`.
  - **Clean Filename & Query Parameter Segmentation**: `splitUrl()` now separates logical filenames from query parameters, rendering filenames in bold white monospace and session/token query strings in a distinct muted tone (`.url-query`).
  - **Manifest Loading Skeleton Shimmer Loader**: Replaced the hollow black `Reading manifest...` box with a 3-row animated skeleton shimmer matching actual quality option dimensions, fully respecting `@media (prefers-reduced-motion: reduce)`.
  - **Balanced Primary Card Boundaries**: Eliminated the chunky 3.5px left border slab on primary stream cards in favor of a clean, balanced 1px border (`--accent-dim`) with subtle surface tint.


- **Clarified GUI Language vs. Downloader Console Language**: The previous dropdown labeled `"DL Language"` only controlled N_m3u8DL-RE's own console output (`--ui-language`) while the GUI remained English. The header now features a dedicated `"GUI Language"` dropdown for translating the app interface, while the engine setting is explicitly labeled `"CLI Console Language (N_m3u8DL-RE)"` with helpful tooltips.

- **A Crash While Saving Could Erase Every Setting And Secret**: `config.json` was written with `File.WriteAllText`, which truncates before it writes — so a crash, a full disk, or a power cut between those two steps left the file empty or half-written. The next launch did not notice: the parse error was caught and factory defaults returned silently. Because the secret entries hold DPAPI ciphertext, there was nothing left to decrypt, and the user's custom headers, proxy, decryption keys and IVs had to be entered again from memory. Settings, the legacy `config.txt`, and the `active-job.json` resume record are now written to a temporary sibling and moved into place, so an interrupted save leaves the previous good copy untouched — the same approach `CaptureInbox` and the Abyss downloader already used.
- **Save Failures Left No Trace**: both save paths caught every exception and reported only through `Debug.WriteLine`, which is compiled out of Release builds. A save that failed because the disk was full or the file was locked produced no evidence at all while the user closed the application believing their settings were kept.
- **Deep In-Page Interception Hardening (Browser Extension v1.4.4)**:
  - **Memory Leak in Inline Manifest Blob URLs**: Dropped the dead `#EXTM3U` blob URL branch in `deep-detect.js` which generated un-revoked `Blob` objects that were 100% rejected by `classify()` and inaccessible to external CLI downloaders.
  - **Main-Thread CPU Stall on Large JSON Payloads**: Enforced a strict node walk budget (`MAX_NODES = 5000`) and max string length limit (`MAX_STRING = 2048`) on `JSON.parse` inspection, preventing main thread lag when web pages load wide data arrays.
  - **Seen URLs Unbounded Set Growth**: Bounded `seenUrls` in `deep-detect.js` with a 512-item LRU eviction cache matching `content.js` policy.
  - **False Positive AES-128 Key Detections**: Removed the arbitrary 16-number array heuristic from JSON object walks, keeping high-confidence binary 16-byte XHR `arraybuffer` key sniffing intact.
  - **XHR Listener Stacking on Reused Instances**: Attached the `load` event listener once per `XMLHttpRequest` instance on `open()`, preventing duplicate event triggers on reused XHR instances.
  - **Dead MAIN World Extension Messaging**: Cleaned up the non-functional `chrome.runtime.sendMessage` call in `deep-detect.js`, channeling all detections cleanly through `window.postMessage`.
- **Manifest Probing OOM Guard on Chunked/Unbounded Streams**: `probe.js` previously used `response.text()`, which allowed an untrusted server omitting `Content-Length` or streaming chunked data to exhaust memory. Both page and direct probes now use a streaming reader that enforces a strict 2MB ceiling and aborts immediately if exceeded.
- **HLS Audio & Subtitle Tracks Rewritten in CF Proxy**: `rewrite_hls_manifest` in `m3u8_cf_bypass.py` previously skipped `#EXT-X-MEDIA` lines because they start with `#`, causing alternate audio/subtitle streams to bypass the proxy and 403. Handled alongside `#EXT-X-KEY` and `#EXT-X-MAP`.
- **DASH Injected BaseURL Retains Query Parameters**: In `m3u8_cf_bypass.py`, deriving the fallback base directory previously stripped query tokens from the MPD URL. Uses `urlsplit`/`urlunsplit` to preserve query tokens.
- **Process OS Handle Leaks in GUI**: Fire-and-forget and shell process invocations in `MainWindow.xaml.cs` and `App.xaml.cs` now cleanly dispose their `Process` handles.
- **Storage Quota Failure Graceful Handling**: `storage.js` wraps session storage writes in a safe handler, preventing unhandled promise rejections if quotas are reached.
- **Extension Silently Dead on Chrome Older Than 123**: `lib/impersonate.js` imported a JSON file with import attributes (`with { type: 'json' }`), which is Chrome 123+, while the manifest declared no `minimum_chrome_version`. Chrome installed the extension on older versions and then failed to parse that module — and because `curl.js` imports it and `popup.js` imports `curl.js`, the entire popup script never ran, leaving the static "Waiting for video stream..." placeholder on screen with no error. Nothing else read that file; it is now a plain ES module and the real floor, Chrome 111 (`color-mix` in `popup.css`), is declared.
- **Incognito Captures Leaked Into The Normal Window**: with no `incognito` key, MV3 defaults to `spanning` — one service worker and one `chrome.storage.session` shared between normal and private browsing. Since captures include the request's `Cookie` header, a stream sniffed in an incognito window appeared, credentials included, in the normal window's "All Recent" list. Now `"incognito": "split"`.
- **Stopping an Abyss / Hydrax Download Reported a Red Error**: the retry handler's exception filter excluded cancellation, so a cancelled request fell through to the fallback handler and was rethrown as `HttpRequestException`. `MainWindow`'s dedicated `catch (OperationCanceledException)` — the one that shows the neutral "Download stopped by user." — never ran, and pressing Stop painted a red *"Download error: … A task was canceled."* instead. It was intermittent because workers parked on the connection semaphore surfaced cancellation correctly while workers already mid-request did not, and `Task.WhenAll` surfaces whichever it reaches first.
- **Abyss / Hydrax Progress Reached 100% Before The Download Finished**: streamed bytes were added to the shared progress counter as they arrived and never returned when a stream failed partway, so a chunk that dropped at 70% and then succeeded on retry contributed 170% of its size. Because `Percentage` clamps at 100, the symptom was not an impossible number but a bar that hit 100% early, an ETA that collapsed to `00:00:00`, and an inflated speed reading. The bytes an attempt contributed are now returned if that attempt does not complete.
- **Popup Permanently Stuck on "Waiting for video stream..." (both Current Tab and All Recent)**: `popup.js` imported `formatDuration` and `formatBitrate` from `lib/format.js`, but neither function exists there (and neither is actually called anywhere in `popup.js` — dead imports). A named import that does not exist on its target module fails at ESM module-link time, before any of the importing module's code runs — so the entire popup script never executed: no `init()`, no listeners, no render, regardless of what background.js had already captured and stored. Detection itself (background.js) was never affected, which is why the toolbar badge could still show a count while the popup stayed empty. Removed the two dead imports. **Reload the extension** (`chrome://extensions` → reload) for this fix to take effect — Chrome caches the previously-loaded (broken) script.
- **CF Proxy Route Scheme Rewritten (path-prefix, not query-param)**: The `--serve` proxy's `/proxy?url=<encoded>&token=<token>` route stomped its own query string when a DASH `<BaseURL>` was resolved against a `$Number$`/`$Time$` segment template (`urljoin` replaces the whole query on a relative reference) — DASH downloads through the proxy were completely broken. Routes are now `/u/<token>/<scheme>/<host>/<path>?<query>`, which `urljoin` resolves correctly because relative references only ever touch the path.
- **Nested Manifests Now Rewritten (HLS master → media, any manifest fetched through the proxy)**: The proxy previously rewrote only the single manifest fetched at `/manifest`; a media playlist referenced by a master playlist was served through `/proxy` unrewritten, pointing straight back at the origin CDN with no TLS impersonation. Every request is now classified (by extension, then by content-type/content-sniffing) and rewritten if manifest-shaped, recursively.
- **SSRF Allowlist No Longer Blocks a Manifest's Own CDN Hosts**: The allowlist was pinned to the initial manifest's host only; a manifest legitimately referencing a second host (a common CDN split between manifest and segment origins) was rejected by the proxy itself. Hosts are now added to the allowlist as they are discovered while rewriting a manifest served by this run.
- **CF Proxy Engine Orchestration Wired Up**: Selecting "CF Proxy + RE" previously appended `--serve` to the launch command and then awaited it with the ordinary single-process runner, which hangs forever (`--serve` never exits on its own). The GUI now launches the proxy directly, waits for its `PROXY_READY` handshake on stdout, points `N_m3u8DL-RE.exe` at the local proxy URL, and shuts the proxy down (stdin close, falling back to a kill) once RE finishes.
- **Auto Engine Selection Implemented**: `Combo_CFEngine`'s "Auto" option previously behaved identically to "CF Direct" regardless of stream kind (`CfEngineSelector.ShouldUseProxy`). It now follows the documented table: HLS without a decryption key stays on the direct engine, everything else (DASH, MSS, HLS with a key, or an unclassified URL) uses the proxy engine.
- **Cookie Store Merge Was Dead Code**: `getMergedCookies()`'s result was written to `stream.cookie`, but `toCurl()` prefers `stream.headers` whenever present — which, since the M2 header-capture change, is always present. The merged cookie never reached a captured cURL command. `applyMergedCookie()` now upserts the merged value into the headers array `toCurl` actually reads.
- **Cookie Merge Now Covers the Page Domain, Not Just the Stream/CDN Domain**: Cloudflare issues `cf_clearance` against the page's own origin, not the CDN a manifest was fetched from; merging only the stream domain silently dropped the one cookie this feature exists to carry. `getMergedCookies()` now fetches and merges both domains.
- **`.bat`/cmd.exe Wrapper Removed From Both CF Engines**: Cloudflare-bypass runs were launched via a temp `.bat` file executed through `cmd.exe`, whose quoting rules disagree with the C-runtime-style `\"` escaping `CfCommandBuilder` emits — a cookie value containing `"` desynchronised cmd's parsing of the rest of the line. Since both engines already run headless with redirected I/O (no console is ever shown), python.exe is now launched directly via `ProcessStartInfo`, removing the incompatible quoting layer entirely instead of adding a second escaping function to patch around it.
- **In-Flight Header Cache No Longer Double-Stores Per Request**: `inFlightHeaders` was written under both `requestId` and the request URL, wasting memory and reintroducing the exact cross-tab collision the `requestId` keying was meant to fix (on the rare miss, a same-URL request from a different tab could be read back). Keyed by `requestId` only.
- **Credential Masking Extended to Extra Headers**: `TextBox_Parameter`'s preview masked only the Cookie value and mislabelled it `cf_clearance=...` even when the cookie held no such name. Every extra captured header (`Authorization`, `X-Api-Key`, etc.) is now masked the same way, with no fabricated label.
- **`clearTabView` No Longer Reads the Whole Storage Area**: Fetched every `chrome.storage.session` key to read the one it needed (`recent_streams`).
- **Cloudflare-Bypass Resume Was Deleting Its Own Resume State**: A partial download (some segments permanently failed after 5 retries) merged what it had and exited 3 — then, unless "Keep segment files after merge" was ticked (unchecked by default), `main()` deleted the segment directory before exiting. The disk-based resume scan added alongside it could never find anything to resume from, because the previous run had already erased it on the way out. `--keep-segs` now governs only what happens to segments after a *fully successful* merge; a partial run always keeps its segments, since they are the resume state.
- **Stream Detection Widened**: `classify()` treated 301/302/303/304/307/308 responses as unusable, so a manifest served behind a redirect or a 304 cache revalidation was silently dropped. `content.js` now also reads `data-src`/`data-url`/`data-hls`/`data-dash` and similar attributes used by non-native players (hls.js, video.js, Plyr) that never set the element's `src`, and the popup's active-tab lookup falls back through `lastFocusedWindow` and then any active tab when `{active: true, currentWindow: true}` returns nothing (observed on some window-manager/multi-monitor setups).
- **Ad Blocklist Silently Dropped Legitimate Streams**: `isAdCdnUrl()` matched blocklisted ad/tracker domains with a plain substring check across the *entire* URL, so any host that merely ended with a blocklisted domain (`socialmedia.net`, `mymedia.net` both matched the `media.net` entry) — or any innocent URL that simply carried a blocklisted domain inside a query parameter — was silently rejected by `classify()` before it ever reached the popup, with no error and no log line. Matching is now anchored to the parsed hostname (`host === domain || host.endsWith('.' + domain)`), which can only ever let more legitimate streams through, never drop additional ones.
- **GUI Tooltips Were Never Localized**: the localization feature's own spec promised translated "tooltips, dialogs, and status text", but 40 of the 41 `ToolTip` attributes in `MainWindow.xaml` shipped hardcoded in English — so switching to 简体中文 or 繁體中文 translated every label while every hover tooltip stayed English. All 41 are now bound to `{DynamicResource Str_*Tooltip}`, with a unit test failing the build if a future tooltip ships hardcoded again. (Screen-reader `AutomationProperties` text is a separate, larger pass and intentionally not included here.)
- **Detection Dispatch Made Prototype-Pollution Safe**: consolidating two duplicate `background.js` message handlers into one lookup table briefly keyed it with a plain object (`DETECTION_SOURCES[message.type]`), which resolves `constructor`/`__proto__`/`toString`/`valueOf`/`hasOwnProperty` to truthy `Object.prototype` members — reachable by any page through the same relay the real detection messages use. Caught in review before release; the table is now a `Map`, which has no prototype chain to walk into.

### Performance

- **Popup Filter Debounced**: Text search input in `popup.js` is debounced to 150ms to prevent redundant DOM recreations and storage reads on fast typing.
- **Background Stream Batching**: Progressive media detections in `background.js` are batched and written to session storage every 2 seconds or 5 items, reducing storage I/O by ~80% on media-heavy pages while manifests/Abyss streams continue to flush immediately.
- **Python Streaming Chunk Downloads**: Segment downloads in `m3u8_cf_bypass.py` now use streaming responses (`stream=True`) and incremental chunk writes instead of buffering full segments into RAM, significantly reducing peak memory footprint during concurrent downloads.

## [2.1.5] - 2026-08-23

### Added

- **Resume Interrupted Downloads (`N_m3u8DL_RE_GUI.Core.Resume`)**:
  - **Deterministic Temp Directory (`ResumePaths`)**: Derives `<save folder>/.nre-tmp/<sanitised saveName>` automatically when `--tmp-dir` is empty, ensuring N_m3u8DL-RE reuses downloaded segments across sessions. Includes DOS reserved device name protection (`CON`, `PRN`, `AUX`, `NUL`, `COM1..9`, `LPT1..9`) and stable prefix hash deduplication for long filenames.
  - **Active Job Tracking (`ResumeJobStore`)**: Writes `%LOCALAPPDATA%\N_m3u8DL_RE_GUI\active-job.json` on download start and deletes it upon successful completion. Interrupted or stopped downloads leave the record intact so existing segments are recoverable. *(This entry originally described the write as atomic; it was not until the Unreleased fix above.)*
  - **Credential Safety**: The job record stores only the source hostname (`SourceHost`), never full stream URLs or access tokens, ensuring signed authentication tokens and cookies are never stored in plaintext.
  - **Startup Resume Banner (`Border_ResumeBanner`)**: On application launch, checks for incomplete downloads with segments on disk. Displays an amber banner naming the unfinished file, saved byte size, time elapsed, and source domain with 1-click **Resume** and **Discard** actions.
  - **Fresh Link Re-attachment Workflow**: Restores save name, save folder, and temp directory into GUI fields while prompting the user to paste a fresh link (avoiding expired token 403 errors), seamlessly continuing the download from existing segments.
  - **Safe Discard**: Confirms deletion naming the exact byte size and cleans up both the temp segment directory and active job record.
- **Honest 3-State Update Checker & Single Source of Truth (`Directory.Build.props`)**:
  - **Single Source of Truth (`Directory.Build.props`)**: Solution-wide MSBuild configuration defining `<AppVersion>2.1.5</AppVersion>`, automatically propagating assembly and file versions across all projects (`N_m3u8DL_RE_GUI`, `N_m3u8DL_RE_GUI.Core`, `N_m3u8DL_RE_GUI.Tests`) without duplicate hardcoded literals.
  - **Honest 3-State Checking (`GitHubUpdateCheckService`)**: Replaced binary boolean checking with `UpdateCheckStatus` enum (`UpToDate`, `UpdateAvailable`, `CheckFailed`). Removed fallback guesses from User-Agent and version comparisons; network failures or unparseable release tags report `CheckFailed` rather than falsely claiming up-to-date.
  - **Dynamic GUI Branding**: Window title and version header text dynamically derive from assembly metadata at runtime.
- **N-RE Stream Bridge Browser Extension (v1.3.0) & Suite Update Check**:
  - **Suite Release Checker (`update-check.js`, `suite-version.js`)**: MSBuild target `WriteSuiteVersionForExtension` auto-generates `extension/suite-version.json` from `$(AppVersion)` on every build. Extension reads suite version and checks GitHub releases using `response.url` resolution to avoid browser opaque-redirect restrictions.
  - **Daily Cached Checks (`storage.js`)**: Caches update check results in `chrome.storage.local` with 24-hour TTL (success) and 5-minute TTL (failure) to prevent redundant GitHub requests on every popup open.
  - **Suite Update Badge**: Shows `🎉 N_m3u8DL-RE GUI v... available ↗` linking to GitHub releases when a new suite version is published.
  - **Neutral Stream Presentation**: Removed presumptive `⭐ Recommended` and `⭐ Best match` badges, presenting all sniffed streams objectively with their exact MIME type, bitrate, and resolution.
  - **On-Demand Quality Probing (`probe.js`, `manifest.js`)**: Pure parser for HLS master playlists (`#EXT-X-STREAM-INF`) and DASH MPDs (`<AdaptationSet>`, `<Representation>`); parses resolution, bandwidth, and codecs into interactive radio choices upon clicking `▸ Qualities`. Probing is strictly on-demand with replay of captured CDN authentication headers and 2MB/8s safety limits.
  - **Quality Directives via Clipboard**: Appends `# nre-select-video: res="1080*"` to cURL commands when a rendition is selected, instantly setting GUI quality selectors.
  - **Multi-Select & Batch List Export (`toBatchList`)**: Checkbox multi-selection, select all, and `📋 Copy as list` with `# Referer:` headers.
  - **Smooth Streaming (MSS), Audio & Wide Format Support**: Added detection for Smooth Streaming (`.ism`, `.isml`, `/Manifest`), standalone audio (`.m4a`, `.opus`, `.flac`, `.wav`, `.aac`, `.mp3`), alternate DASH MIME types (`video/vnd.mpeg.dash.mpd`), and progressive media (`.mp4`, `.m4v`, `.webm`, `.mkv`, `.mov`, `.flv`, `.ogv`, `.3gp`).
  - **Manifest-First Segment Suppression**: Enforced invariant across tab storage and `recent_streams` so tabs with an active manifest (`HLS`/`DASH`/`MSS`) drop incoming segments and auto-purge previously buffered fragments.
  - **Content-Range & Real Size Reporting**: Extracted true file sizes from 206 Partial Content responses with approximate `~` indicators.
  - **Dedicated cURL Serializer (`toCurl`)**: Pure modular cURL generator matching C# bash escaping semantics.
  - **Confidence Tiers & Query Fallback**: Scoped low-confidence query string analyzer (`guess` badge) restricted to stream-carrying parameters (`type`, `format`, `file`, `stream`).
  - **420px Adaptive UI & Grouping**: Neatly groups "All Recent" streams by origin domain, adds prominent primary card styling, two-line URL display (filename top, path bottom), roving keyboard navigation (Space/Enter/Arrows), and WCAG AA contrast compliance.
- **Native Abyss / Hydrax Stream Downloader (`N_m3u8DL_RE_GUI.Core.Abyss`)**:
  - Implemented pure C# crypto engine (`AbyssCrypto`) supporting AES-CTR (Counter Mode 128-bit block feedback), MD5 key derivation (string & byte-mapped numeric), and Double-Base64 chunk token encoding with **zero external dependencies**.
  - Created `AbyssMetadataFetcher` with dual-engine architecture: in-process `HttpClient` with automatic fallback to native `curl.exe` and DNS-over-HTTPS (`1.1.1.1`) to transparently bypass Cloudflare Managed Challenges / JA3-JA4 TLS fingerprint filters on Abyss hosts (`abysscdn.com`, `playhydrax.com`, `zplayer.io`, `short.ink`, `abyss.to`).
  - Added `HeaderParser` (`N_m3u8DL_RE_GUI.Core.Capture`) supporting multi-line, cURL `-H`, and pipe-delimited headers, dynamically propagating custom `Referer` and `User-Agent` credentials to both metadata fetch and 2MB chunk segment downloads.
  - Created `AbyssDownloadService` for concurrent 2MB chunk downloading with `SemaphoreSlim` rate limiting, transient failure retries, live speed & ETA reporting, and automatic byte reassembly into continuous `.mp4` video files.
  - Wired direct Abyss stream handling into `MainWindow`: pasting an Abyss link automatically triggers metadata extraction, selects the optimal resolution, tracks progress in the GUI progress bar & log view, and supports cancellation via the Stop button.
- **Universal Stream Capture & cURL Directives (`N_m3u8DL_RE_GUI.Core.Capture`)**:
  - `CapturedRequest`, `HeaderPolicy` (stripping `:authority`, `sec-*`, `accept-encoding`), and `CurlCommandParser` (tokenizing single/multi-line bash, cmd, and Firefox cURL commands).
  - `CaptureDirectives`: Reads `# nre-key: value` comments appended to clipboard cURL commands, seamlessly applying parameters like `select-video` (`TextBox_SelectVideo`) without breaking backward compatibility.
  - `BatchPasteHelper`: Distinguishes multi-stream batch payloads from single cURL commands, writes temp `.txt` queues, and auto-dispatches into the batch downloader.
  - Added "📋 Paste from browser" (`Button_PasteCurl`) and automatic clipboard listener on `TextBox_URL` for instant 1-click importing.
  - `HarStreamExtractor`: Drop a `.har` network capture file directly onto the GUI; automatically filters noise, deduplicates byte-range requests, and prioritizes master manifests.
  - `StreamPickerWindow`: Interactive multi-stream dialog with stream badges (`HLS`, `DASH`, `MSS`, `Abyss`, `Media`, `Audio`) when a capture contains multiple streams.
- **In-Window Feedback Surface & Progress Reporting (Zone D)**:
  - Added live progress bar and status strip (`TextBlock_Status`, `ProgressBar_Download`) directly in the main window.
  - Added collapsible live log viewer (`TextBox_Log`) with `ToggleButton_Log` toggle.
  - Added "Open Folder" button (`Button_OpenFolder`) upon successful download completion for instant folder access.
  - Created `ConsoleOutputParser` in `N_m3u8DL_RE_GUI.Core` for pure ANSI sequence stripping and real-time percentage extraction.
  - Redirected standard output and error streams in `DownloadService` and forwarded clean log lines and progress to GUI.
- **P0 Hardening & DPAPI Secret Protection Alignment**:
  - Added legacy secret keys (`请求头`, `代理`, `IV`) to DPAPI protection registry in `JsonConfigService`.
  - Hardened DPAPI decryption failure handling: preserves raw ciphertext (`dpapi:<blob>`) instead of wiping credentials to empty string.
  - Stopped writing duplicate plaintext `IV` in `MainWindowConfigMapper` while maintaining backward-compatible read resolution.
  - Extracted pure `CfCommandBuilder` to `N_m3u8DL_RE_GUI.Core` with cmd.exe `%` doubling and UTF-8 batch header.
- **P1 Correctness & Non-UTF-8 Encoding Recovery**:
  - `HtmlTitleExtractor`: Added streaming-safe title extractor respecting server-declared HTTP `Content-Type: charset` (GBK, Big5, Shift-JIS, ISO-8859-1) with `System.Text.Encoding.CodePages`. Replaced O(N²) buffer rescanning with fixed 7-char overlap window (`ContainsClosingTitleTag`).
  - `TextEncodingDetector`: Real system ANSI fallback on .NET Core (`AnsiFallback`) and sample boundary tolerance for multi-byte UTF-8 sequences straddling the 8 KB boundary.
  - `LegacyConfigCodec`: Safe escaping/unescaping (`%3B`, `%25`) for `key=value;` legacy `config.txt` format, preventing data loss in raw string fields (`AdKeyword`, `SavePattern`, etc.) while maintaining backward compatibility.
  - `ArgsBuilder`: Cached static `EscapeChars` set eliminating allocations in fast-path argument quoting; escaped quotes in `MuxBinPath`, `RangeStart`, and `RangeEnd`.
  - `UtilityService`: DOS reserved device name sanitization matching segments before the first dot (e.g. `CON.txt.bak` -> `_CON.txt.bak`).
- **WCAG 2.1 AA Contrast Compliance (Part B)**:
  - Resolved 9 measured contrast failures across dark theme palette tokens:
    - Replaced `BorderBrushCustom` (`#2A2A38` -> `#66667C`, 3.03:1 on Card).
    - Introduced `AccentTextBrush` (`#7A87FF`, 5.44:1 on Card) for GroupBox headers, selected tab text, and window title while retaining `AccentBrush` (`#5865F2`) for surfaces.
    - Adjusted button hover ramps to darken on interaction (`AccentHoverBrush` `#4350D8`, `AccentPressedBrush` `#3E4ACB`) ensuring contrast increases on hover.
    - Updated Stop button (`#C0392B`, 5.44:1) and Drop labels / validation borders (`DropLabelBrush` `#EC7063`, 5.70:1).
  - Added automated `XamlContrastTests` to prevent contrast regressions.
- **Option Conflict & Dependency Visibility (Part C)**:
  - `SyncDependentControlStates`: Dynamically disables and tooltips overridden fields (`TextBox_SelectAudio`, `TextBox_DropVideo`) when **Audio Only** is active.
  - Added Cloudflare Mode Scope Warning banner (`Border_CfScopeWarning`) in amber (`#F39C12`) explaining that CF mode ignores non-network tab settings. Enabled/disabled CF fields based on bypass toggle.
  - Renamed Advanced tab label to "DL Language" with tooltip explaining it configures the downloader's console output rather than the GUI.
  - Updated `DownloadOptions.AudioOnly` getter to accept both `all` and `.*` drop patterns.
- **Process & Concurrency Lifetime Safety**:
  - Implemented `BeginCancellableOperation()` / `EndCancellableOperation()` helper to prevent cross-operation `CancellationTokenSource` disposal in `async void` UI handlers.
  - Added global crash protection in `App.xaml.cs` (`DispatcherUnhandledException`, `AppDomain.UnhandledException`, `TaskScheduler.UnobservedTaskException`).
  - Clamped window dimensions to desktop work area on high DPI displays to prevent Zone D from sliding under the taskbar.
- **Desktop Accessibility (a11y) & Keyboard Navigation**:
  - Added `AccessibleFocusVisual` high-contrast dashed focus rectangle across all controls.
  - Added keyboard bindings: `Alt+G` / `Enter` for GO, `Alt+S` / `Escape` for Stop.
  - Added `AutomationProperties.Name` across all interactive inputs.
  - Added `XamlAccessibilityTests` headless automated XAML validation suite.
- **Automated Test Suite (723 .NET Tests + 246 Node.js Extension Tests)**:
  - Total automated test suite expanded to **969 tests** (722 passing C# tests with 1 live integration skip, and 246 passing Node.js extension tests) with 0 errors and 0 warnings.

### Changed

- **Temp Directory Default Location**: When `TextBox_TmpDir` is left empty, segments now land deterministically in `<save folder>/.nre-tmp/<saveName>` instead of N_m3u8DL-RE's default arbitrary location.
- Forced `--no-ansi-color` on GUI download execution paths to ensure clean log parsing.
- Standardized all application text and messages to clean English.
- Updated window height default to 660px with work-area clamping.

### Notes & Limitations

- **Batch runs are not resumable**: A single job record cannot describe a multi-item run; batch queue resume remains deferred.
- **Abyss module scope**: The Abyss module is not covered by this series of audits, and is excluded from every test-count figure quoted in them.

---

## [2.1.4] - 2026-08-08

### Added

- **Windows DPAPI Secret Protection**:
  - Automated encryption for sensitive configuration fields (`Headers`, `Proxy`, `CustomHLSKey`, `CustomHLSIv`, `Key`) using Windows DPAPI (`ProtectedData.Protect` / `DataProtectionScope.CurrentUser`) stored as `dpapi:<base64>` in `config.json`.
  - Automatic plaintext secret scrubbing when writing legacy `config.txt`.
- **Download Process & Cancellation Lifecycle Hardening**:
  - Thread-safe process cancellation in `IDownloadService` with process tree termination (`proc.Kill(entireProcessTree: true)`).
  - Visible interactive CMD console window support (`UseShellExecute = true`).
  - Asynchronous and cancellable Python discovery (`FindPythonWithCurlCffiAsync`) using `CancellationTokenSource`.
- **Fail-Fast Input & Title Resolution**:
  - Two-stage `InputValidation.IsHttpUrl` using `Uri.TryCreate` enforcing absolute HTTP/HTTPS schemes with a non-empty host.
  - Title lookup timeout (15s) and `CancellationToken` support in `IUtilityService.GetTitleFromUrlAsync`.
- **Isolated Batch Execution & Cleanup**:
  - Unique temp batch file path generation in `%TEMP%` (`batch_{timestamp}_{guid}.bat`).
  - Automatic `finally` deletion of temporary `.bat` files after process termination or cancellation.
  - Corrected batch script progress title denominator `[1/N]` based on valid parsed entries.
- **Desktop Accessibility (a11y)**:
  - Added `AutomationProperties.Name` and `AutomationProperties.HelpText` across core WPF controls (`TextBox_URL`, `Button_GO`, `Button_Stop`, `TextBox_WorkDir`, `TextBox_Title`, `TextBox_Parameter`, `Button_CopyCommand`).
- **Comprehensive Unit & Integration Test Suite (164 Tests)**:
  - Added `NSubstitute` (v6.0.0) package for ViewModel mocking.
  - Reorganized tests into `Unit/Core`, `Unit/Services`, `Unit/ViewModels`, `Integration`, and `Fixtures`.

### Changed

- Updated Window Title to `N_m3u8DL-RE GUI v2.1.4`.
- Updated `AssemblyVersion` and `AssemblyFileVersion` to `2.1.4.0`.

### Verification

- `dotnet build N_m3u8DL_RE_GUI.sln /warnaserror` passes cleanly (0 Error, 0 Warning).
- `dotnet test N_m3u8DL_RE_GUI.Tests/N_m3u8DL_RE_GUI.Tests.csproj` passes cleanly (164/164 tests passed).

---

## [2.1.3] - 2026-08-06

### Added

- **3-Zone Modern UX/UI Architecture Redesign**:
  - **Zone A (Top Dock)**: Prominent Hero Input URL box, Quick Save Directory / Save Name controls, Always-on-Top toggle, and interactive **🎉 GUI Update Pill Badge** (`#2ECC71` -> `#27AE60` hover).
  - **Zone B & C (Left Nav Sidebar & Content)**: Replaced monolithic vertical scrolling with clean 6-Tab sidebar navigation (`📦 Download`, `🌐 Network`, `🔒 Security`, `🎬 Media`, `📡 Live`, `⚙️ Advanced`).
  - **Zone D (Bottom Command Bar)**: Fixed-bottom command line preview bar with monospace code font and copyable argument string.
- **GUI Auto-Update Engine (`IUpdateCheckService`)**:
  - Parity HTTP 302 Redirect resolution parsing GitHub `Location` header without hitting REST API rate limits.
  - Background async auto-check on startup + `Check Now` manual trigger in Tab 6 (⚙️ Advanced).
  - Concurrency lock (`_isCheckingUpdate`), button loading state, and 3-second auto-clear micro-interaction for `✓ Latest version` confirmation.
  - Config persistence (`AutoCheckGuiUpdate` in `config.txt`).

### Changed

- **Unified Premium Dark Theme**:
  - Applied cohesive dark color tokens (`#0D0D0F` dark canvas, `#141418` surface container, `#1C1C22` card containers, `#5865F2` Discord/Indigo accent, `#8888A8` muted text).
  - Dynamic UserAgent version header formatting in `GitHubUpdateCheckService`.
  - Updated Window Title to `N_m3u8DL-RE GUI v2.1.3`.
  - Updated `AssemblyVersion` and `AssemblyFileVersion` to `2.1.3.0`.

### Fixed

- **ComboBox Dark Mode & Dropdown Text Visibility**:
  - Implemented custom `ComboBoxToggleButtonTemplate` and `ComboBoxItemStyle` to eliminate WPF system-default white backgrounds and invisible text.
  - Applied custom `ControlTemplate` for `GroupBox` headers and content borders to prevent Windows standard background leaks.
  - Styled `ContextMenu`, `MenuItem`, `Separator`, and `ScrollBar` components for dark theme consistency.

### Verification

- `dotnet build N_m3u8DL_RE_GUI.sln` passes cleanly (0 Error, 0 Warning).
- `dotnet test N_m3u8DL_RE_GUI.sln` passes cleanly (112/112 tests passed).

---

## [2.1.2] - 2026-08-06

### Added

- **Dedicated Cloudflare Bypass UX/UI Expander**:
  - Dedicated Cloudflare section styled with amber accent (`#F39C12`) matching VS Code dark theme.
  - TLS Fingerprint impersonation selector dropdown (`chrome`, `chrome120`, `chrome131`, `edge101`, `safari17_0`).
  - Dedicated **Referer** input with dynamic origin auto-derivation from input M3U8 URL.
  - Dedicated **CF Cookie** input for `cf_clearance` / `__cf_bm` headers.
  - Independent **Keep Segments** toggle decoupled from global file deletion settings.
  - Contextual tip panel for Cloudflare challenge bypass guidance.
- **Enhanced Python Downloader (`m3u8_cf_bypass.py`)**:
  - Auto-derivation of `Referer` from M3U8 URL domain via `urllib.parse.urlparse`.
  - Robust URL resolution for relative, root-relative, and query-string URLs using `urllib.parse.urljoin`.
  - HLS Encryption detection warning (`#EXT-X-KEY` detection).
  - Real-time download progress percentage logging.
  - **Upstream N_m3u8DL-RE Log & UX Parity**:
    - `Mediainfo.ToString()` stream probing formatting (`[0x100]: Video, h264 (High), 640x360, 29.97 fps, 130 kb/s`) matching C# upstream `MediainfoUtil`.
    - Corrected stream ID fallback to `"NaN"` matching upstream `IdRegex`.
    - Standardized terminal log strings matching upstream `StaticText.cs` (`Content Matched: HTTP Live Streaming`, `Master List detected, try parse all streams`, `Selected streams:`).
    - Optimized single-pass `ffmpeg` binary path resolution.

### Changed

- Updated Window Title to `N_m3u8DL-RE GUI v2.1.2`.
- Updated `AssemblyVersion` and `AssemblyFileVersion` to `2.1.2.0`.

### Verification

- `python m3u8_cf_bypass.py --help` executed cleanly (Exit code 0).
- `dotnet build N_m3u8DL_RE_GUI.sln` passes cleanly (0 Error, 0 Warning).
- `dotnet test N_m3u8DL_RE_GUI.sln` passes cleanly (104/104 tests passed).

---

## [2.1.1] - 2026-08-01

### Added

- **Enhanced Cloudflare Bypass (`m3u8_cf_bypass.py`)**:
  - Automatic Master Playlist resolution (`#EXT-X-STREAM-INF`) to select highest bandwidth stream.
  - Per-segment download retry loop (`max_retries=5`) for resilient downloads under unstable network conditions.
  - Batch command injection prevention via `EscapeBatchArg()` argument sanitization.
  - Expanded Python interpreter probing (`FindPythonWithCurlCffi`) supporting standard CPython, WorkBuddy managed Python, Anaconda/Miniconda, `py` launcher, and PATH resolvers.
  - Real-time parameter preview for Cloudflare bypass script in UI parameter box.
- **Improved UX & Format Guidance**:
  - Enhanced tooltips for `MuxImport`, `MuxBinPath`, `CustomRange`, and `AdKeyword` controls with exact CLI format examples.
- **Repository Structure Normalization**:
  - Migrated agent handoff notes to `docs/dev-notes/` to separate development context from runtime logs (`/Logs/`).
  - Added `.gitignore` rules for `/cf_segments/` and temporary batch execution files.

### Changed

- **Updated Core Engine Binary (`N_m3u8DL-RE.exe`)**:
  - Upgraded bundled core engine to `N_m3u8DL-RE v0.6.0-beta` (latest git master branch build version `2026-07-30-git-2ae2413488`).

### Fixed

- **Audio Only Stream Selection**: Corrected `--drop-video` argument in Audio Only mode to use regex wildcard (`.*`) instead of invalid boolean string (`"true"`).
- **Mux Skip Subtitles**: Verified and added unit test coverage for `skip_sub=true` mapping in `-M` parameter.

### Verification

- `dotnet build N_m3u8DL_RE_GUI.sln` passes cleanly (0 Error, 0 Warning).
- `dotnet test N_m3u8DL_RE_GUI.sln` passes cleanly (104/104 tests passed).

---

## [2.1.0] - 2026-03-03

### Added

- **Mux After Done** section - Enable muxing with Format (mp4/mkv), Muxer (ffmpeg/mkvmerge), Bin Path, Keep Files, Skip Subtitles
- **Live Recording** section - Perform as VOD, Realtime Merge, Keep Segments, Pipe Mux, Fix VTT by Audio, Record Limit, Wait Time, Take Count
- **Stream Selection (Regex)** section - Select/Drop Video, Audio, and Subtitle streams using regex patterns
- **Decryption Engine** section - Engine selection (MP4DECRYPT/SHAKA/FFMPEG), HLS Method, Binary Path, Key Text File, Real-Time Decryption
- **Advanced Settings** section - Save Pattern, FFmpeg Path, Ad Keyword, Log Level, UI Language, Append URL Params, No Log, Write Meta JSON, FFmpeg Concat, Multi EXT-MAP, Disable Update Check
- Config persistence for all 40+ new settings (save/restore on close/open)
- 4 helper methods for clean config restoration (`RestoreCheckBox`, `RestoreTextBox`, `RestoreComboByIndex`, `RestoreComboByContent`)
- Safe config abstraction with legacy compatibility:
  - `AppConfigState`
  - `IConfigService` / `ConfigService`
- Core helpers for safer parsing and normalization:
  - `OptionValueNormalizer` (preserves drive roots like `C:\`)
  - `BatchInputParser` (stable `.txt` batch line parsing)
  - `TextEncodingDetector` (safe encoding detection for short/malformed files)
- Batch script orchestration service:
  - `IBatchScriptService` / `BatchScriptService`
  - `BatchScriptBuildResult`
- Expanded test coverage:
  - `ConfigServiceTests`
  - `BatchInputParserTests`
  - `TextEncodingDetectorTests`
  - `BatchScriptServiceTests`
  - `InputValidationTests`
  - `MainWindowConfigMapperTests`
  - `UtilityServiceTests`

### Changed

- **Collapsible sections** - Converted 11 GroupBox sections to Expander controls; sections can be collapsed/expanded to reduce scrolling
- **ComboBox UX improvements** - High-contrast dropdown list (white background + dark text) and reliable item selection behavior
- **Sub Format moved inside Download Options** - No longer floating between sections
- Cleaned up unused `using` directives and added `#nullable enable`
- Added `WpfComboBox` type alias to prevent `ComboBox` ambiguity between WPF and WinForms
- Version bump to v2.1.0 across `.csproj`, window title, README, and CHANGELOG
- Refactored `MainWindow` to reduce code-behind complexity while preserving behavior:
  - Batch generation moved into `IBatchScriptService`
  - Encoding detection delegated to `TextEncodingDetector`
- Added null-safe validation refresh during startup to prevent early `TextChanged` crashes
- Hardened GO flow with safer process launch wrappers and `try/finally` UI state restoration
- Startup argument handling now uses shared `InputValidation.IsSupportedStartupInputArgument(...)`
  - Supports `http/https`, directory paths, `.m3u8`, `.json`, `.txt`, `.mpd`
- Directory-based batch script generation now sorts inputs for deterministic output ordering
- Implemented Windows-safe argument quoting in `ArgsBuilder` (supports trailing `\` and embedded quotes)
- Startup/title handling now separates URL vs local file resolution paths
- Utility title resolver now short-circuits for non-HTTP input to avoid unnecessary network work
- Directory batch titles now use file names directly and are escaped safely for CMD title context

### Fixed

- Startup crash (`NullReferenceException`) triggered by `TextChanged` before all controls were initialized
- Startup XAML parse crash in ComboBox styling (`Setter.Property=Resources` misuse)
- Potential config IO failures now fail safely without crashing app startup/close
- Intermittent clipboard access failures now fail safely (no startup/UI crash when clipboard is locked)
- Potential malformed command arguments caused by root paths or embedded quotes are now escaped correctly

### Verification

- `dotnet build N_m3u8DL_RE_GUI.sln /warnaserror` passes
- `dotnet test N_m3u8DL_RE_GUI.sln` passes (`94/94`)

---

## [2.0.0] - 2026-01-23

### Added

- Full compatibility with N_m3u8DL-RE command-line arguments
- Subtitle format selection (SRT/VTT)
- Auto subtitle fix option
- Concurrent download toggle
- Auto select option for best quality
- Speed limit configuration

### Changed

- Refactored argument building logic using `ArgsBuilder` pattern
- Migrated to .NET 9.0
- Improved code architecture with Services layer
- Translated all Chinese/Thai comments to English for international maintainability
- Changed batch file encoding from system default to UTF-8 for cross-platform compatibility

### Fixed

- Empty catch blocks now properly log errors using `Debug.WriteLine`
- Resource leaks in file encoding detection methods
- Batch processing with Thai and Chinese filenames

### Security

- Updated TLS configuration for better compatibility

---

## [1.1.0] - 2026-01-13

### Changed

- Refactored DownloadOptions with proper stream settings

---

## [1.0.0] - 2025-08-05

### Added

- Initial release
- GUI wrapper for N_m3u8DL-RE CLI tool
- Dark theme UI
- Batch download support from text files and folders
- Custom headers support
- Proxy configuration
- Thread and retry settings
- Time range download
- iQiyi DASH direct download
- Tencent Video and WeTV title extraction
- Auto file encoding detection
- Clipboard URL detection
- Drag-and-drop support for m3u8/mpd/json files
- Multi-language support (EN/CN/TW)
- Configuration persistence

---

## Version History Summary

| Version | Date       | Highlights                                                |
| ------- | ---------- | --------------------------------------------------------- |
| 2.1.6   | 2026-09-29 | Capture Contract v2, Abyss quality picker, GUI in English / 简体中文 / 繁體中文, Cloudflare proxy and direct-engine fixes, Cloudflare runner and Python probe moved into Core, Browser Extension v1.4.5, 1,336 total tests |
| 2.1.5   | 2026-08-23 | Resume download, SSOT versioning, honest update checks, Abyss downloader, Browser Extension v1.3.0, 969 total tests |
| 2.1.4   | 2026-08-08 | Windows DPAPI secret protection, lifecycle hardening, 164 tests |
| 2.1.3   | 2026-08-06 | 3-Zone Modern UX/UI Architecture, Dark Mode ComboBox fixes|
| 2.1.2   | 2026-08-06 | Dedicated CF Bypass Expander UX/UI, TLS fingerprinting    |
| 2.1.1   | 2026-08-01 | Cloudflare bypass hardening, AudioOnly regex fix, UX hints |
| 2.1.0   | 2026-03-03 | 5 new settings sections, Expander UI, stability hardening |
| 2.0.0   | 2026-01-23 | Code refactoring, English codebase, UTF-8 encoding        |
| 1.1.0   | 2026-01-13 | Stream settings refactor                                  |
| 1.0.0   | 2025-08-05 | Initial release                                           |

[Unreleased]: https://github.com/naravid19/N_m3u8DL_RE_GUI/compare/v2.1.6...HEAD
[2.1.6]: https://github.com/naravid19/N_m3u8DL_RE_GUI/compare/v2.1.5...v2.1.6
[2.1.5]: https://github.com/naravid19/N_m3u8DL_RE_GUI/compare/v2.1.4...v2.1.5

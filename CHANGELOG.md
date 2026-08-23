# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

---

## [Unreleased]

---

## [2.1.5] - 2026-08-23

### Added

- **Resume Interrupted Downloads (`N_m3u8DL_RE_GUI.Core.Resume`)**:
  - **Deterministic Temp Directory (`ResumePaths`)**: Derives `<save folder>/.nre-tmp/<sanitised saveName>` automatically when `--tmp-dir` is empty, ensuring N_m3u8DL-RE reuses downloaded segments across sessions. Includes DOS reserved device name protection (`CON`, `PRN`, `AUX`, `NUL`, `COM1..9`, `LPT1..9`) and stable prefix hash deduplication for long filenames.
  - **Active Job Tracking (`ResumeJobStore`)**: Atomically writes `%LOCALAPPDATA%\N_m3u8DL_RE_GUI\active-job.json` on download start and deletes upon successful completion. Interrupted or stopped downloads leave the record intact so existing segments are recoverable.
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
| 2.1.5   | 2026-08-23 | Resume download, SSOT versioning, honest update checks, Abyss downloader, Browser Extension v1.3.0, 969 total tests |
| 2.1.4   | 2026-08-08 | Windows DPAPI secret protection, lifecycle hardening, 164 tests |
| 2.1.3   | 2026-08-06 | 3-Zone Modern UX/UI Architecture, Dark Mode ComboBox fixes|
| 2.1.2   | 2026-08-06 | Dedicated CF Bypass Expander UX/UI, TLS fingerprinting    |
| 2.1.1   | 2026-08-01 | Cloudflare bypass hardening, AudioOnly regex fix, UX hints |
| 2.1.0   | 2026-03-03 | 5 new settings sections, Expander UI, stability hardening |
| 2.0.0   | 2026-01-23 | Code refactoring, English codebase, UTF-8 encoding        |
| 1.1.0   | 2026-01-13 | Stream settings refactor                                  |
| 1.0.0   | 2025-08-05 | Initial release                                           |

[Unreleased]: https://github.com/naravid19/N_m3u8DL_RE_GUI/compare/v2.1.5...HEAD
[2.1.5]: https://github.com/naravid19/N_m3u8DL_RE_GUI/compare/v2.1.4...v2.1.5

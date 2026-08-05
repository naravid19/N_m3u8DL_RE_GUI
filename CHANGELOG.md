# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

---

## [2.1.2] - 2026-08-05

### Added

- **Dedicated Cloudflare Bypass UX/UI Expander**:
  - Dedicated `<Expander>` section styled with amber accent (`#F39C12`) matching VS Code theme.
  - TLS Fingerprint impersonation selector dropdown (`chrome`, `chrome120`, `chrome131`, `edge101`, `safari17_0`).
  - Dedicated **Referer** input with dynamic origin auto-derivation from input M3U8 URL.
  - Dedicated **CF Cookie** input for `cf_clearance` / `__cf_bm` headers.
  - Independent **Keep Segments** toggle decoupled from global file deletion settings.
  - Contextual tip panel for Cloudflare challenge bypass guidance.
- **Enhanced Python Downloader (`m3u8_cf_bypass.py`)**:
  - Auto-derivation of `Referer` from M3U8 URL domain via `urllib.parse.urlparse`.
  - Robust URL resolution for relative, root-relative, and query-string URLs using `urllib.parse.urljoin`.
  - HLS Encryption detection warning (`#EXT-X-KEY` detection).
  - Real-time download progress percentage logging (every 5-10 segments).

### Changed

- Renamed previous version entry from `2.2.0` to `2.1.1`.
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
| 2.2.0   | 2026-08-01 | Cloudflare bypass hardening, AudioOnly regex fix, UX hints |
| 2.1.0   | 2026-03-03 | 5 new settings sections, Expander UI, stability hardening |
| 2.0.0   | 2026-01-23 | Code refactoring, English codebase, UTF-8 encoding        |
| 1.1.0   | 2026-01-13 | Stream settings refactor                                  |
| 1.0.0   | 2025-08-05 | Initial release                                           |

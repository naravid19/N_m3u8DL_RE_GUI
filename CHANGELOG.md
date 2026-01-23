# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

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

| Version | Date       | Highlights                                         |
| ------- | ---------- | -------------------------------------------------- |
| 2.0.0   | 2026-01-23 | Code refactoring, English codebase, UTF-8 encoding |
| 1.1.0   | 2026-01-13 | Stream settings refactor                           |
| 1.0.0   | 2025-08-05 | Initial release                                    |

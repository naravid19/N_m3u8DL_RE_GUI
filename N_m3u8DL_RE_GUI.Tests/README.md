# N_m3u8DL_RE_GUI Test Suite Documentation

Welcome to the automated test suite for **N_m3u8DL_RE_GUI**! This directory contains all unit, integration, and live-validation tests for the application.

---

## 📁 Directory Structure & Organization

```
N_m3u8DL_RE_GUI.Tests/
├── README.md                           <- Test suite documentation & context
├── Fixtures/                           <- Shared test fixtures & constants
│   └── TestConstants.cs                <- Centralized test URLs & constants
├── Unit/                               <- Unit tests (fast, isolated, mocked)
│   ├── Core/                           <- Tests for N_m3u8DL_RE_GUI.Core
│   │   ├── ArgsBuilderTests.cs         <- Command-line argument builder tests
│   │   ├── BatchInputParserTests.cs    <- Batch text/CSV parser tests
│   │   ├── DownloadOptionsTests.cs     <- Model defaults & property tests
│   │   ├── DropInputRulesTests.cs      <- Drag & drop input rules tests
│   │   ├── InputValidationTests.cs     <- URL, proxy & path validation tests
│   │   ├── OptionValueNormalizerTests.cs <- Directory path normalizer tests
│   │   └── TextEncodingDetectorTests.cs<- File BOM & encoding detection tests
│   ├── Services/                       <- Tests for application services
│   │   ├── BatchScriptServiceTests.cs  <- Batch script generator tests
│   │   ├── ConfigServiceTests.cs       <- INI configuration service tests
│   │   ├── DownloadServiceTests.cs     <- Download process lifecycle tests
│   │   ├── JsonConfigServiceTests.cs   <- JSON config persistence tests
│   │   ├── MainWindowConfigMapperTests.cs <- GUI-to-Config mapping tests
│   │   ├── UpdateCheckServiceTests.cs  <- GitHub update checker tests
│   │   └── UtilityServiceTests.cs      <- Title resolver & file utility tests
│   └── ViewModels/                     <- Tests for ViewModels
│       └── MainViewModelTests.cs       <- MainViewModel command & state tests
└── Integration/                        <- Integration & Live Stream tests
    └── LiveStreamValidationTests.cs    <- Validation tests for real HLS/M3U8 streams
```

---

## 🚀 How to Run Tests

### Run All Tests via CLI
```bash
dotnet test N_m3u8DL_RE_GUI.Tests/N_m3u8DL_RE_GUI.Tests.csproj
```

### Run Specific Test Categories
```bash
# Run only Unit Tests
dotnet test --filter "FullyQualifiedName~Tests.Unit"

# Run only Integration Tests
dotnet test --filter "FullyQualifiedName~Tests.Integration"
```

---

## 🔗 Test URLs & Context (Provided Targets)

The test suite includes dedicated validation for user-provided real-world streaming formats:

1. **Cloudflare-Protected / Surrit Stream (with Referrer)**
   - **Stream URL:** `https://surrit.com/33ece07f-3229-41eb-b189-ec2485619e02/360p/video.m3u8`
   - **Referrer:** `https://missav123.com/`
   - **Test Target:** Verified in `LiveStreamValidationTests` and `ArgsBuilderTests` for correct `-H "Referer: ..."` parameter construction.

2. **Open HLS / M3U8 Stream (AnimeIndy)**
   - **Stream URL:** `https://hls.animeindy.com:8443/vid/MN8fWZAdg/video.mp4/playlist.m3u8`
   - **Test Target:** Verified in `LiveStreamValidationTests` for HTTP reachability and CLI argument building.

---

## 🛠️ Testing Frameworks & Libraries

- **xUnit** (v2.6.6) — Primary testing framework
- **NSubstitute** (v6.0.0) — Lightweight mock library for dependency injection
- **Microsoft.NET.Test.Sdk** — Test runner & Visual Studio integration
- **Coverlet Collector** — Code coverage collection

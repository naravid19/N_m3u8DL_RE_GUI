<!-- Improved compatibility of back to top link: See: https://github.com/othneildrew/Best-README-Template/pull/73 -->

<a id="readme-top"></a>

<!-- PROJECT SHIELDS -->

[![.NET][dotnet-shield]][dotnet-url]
[![WPF][wpf-shield]][wpf-url]
[![C#][csharp-shield]][csharp-url]
[![License][license-shield]][license-url]

<!-- PROJECT LOGO -->
<br />
<div align="center">
  <a href="https://github.com/naravid19/N_m3u8DL_RE_GUI">
    <img src="images/logo.ico" alt="Logo" width="80" height="80">
  </a>

  <h3 align="center">N_m3u8DL-RE GUI</h3>

  <p align="center">
    A modern, user-friendly Windows GUI wrapper for the powerful N_m3u8DL-RE CLI tool.
    <br />
    <a href="https://github.com/nilaoda/N_m3u8DL-RE"><strong>View Original CLI Tool</strong></a>
    <br />
    <br />
    <a href="#getting-started">Getting Started</a>
    ·
    <a href="https://github.com/naravid19/N_m3u8DL_RE_GUI/issues/new?labels=bug">Report Bug</a>
    ·
    <a href="https://github.com/naravid19/N_m3u8DL_RE_GUI/issues/new?labels=enhancement">Request Feature</a>
  </p>
</div>

<!-- ABOUT THE PROJECT -->

## About The Project

<div align="center">
  <img src="images/screenshot.png" alt="Product Screenshot" width="80%">
</div>

**N_m3u8DL-RE GUI** provides a graphical interface for the [N_m3u8DL-RE](https://github.com/nilaoda/N_m3u8DL-RE) command-line tool. It makes downloading DASH, HLS, and MSS streams incredibly easy—no need to memorize complex command-line arguments anymore!

### Main Benefits:

- 🚀 **No command-line memorization** - Common options are available through simple UI controls.
- 📦 **Batch processing** - Download multiple streams from text files or folders with one click.
- 🔒 **Privacy First** - Your settings and headers are automatically saved between sessions and heavily encrypted using Windows DPAPI.
- 🛡️ **Cloudflare WAF Bypass** - Built-in TLS fingerprint impersonation to bypass Cloudflare security seamlessly.

<p align="right">(<a href="#readme-top">back to top</a>)</p>

---

<!-- GETTING STARTED -->

## Getting Started (Installation)

We have intentionally kept the installation process as simple as possible. No installers, no complicated setups.

### 1. Download

Download the latest release (`N_m3u8DL_RE_GUI_v2.1.5.zip`) from our [GitHub Releases](https://github.com/naravid19/N_m3u8DL_RE_GUI/releases) page.

### 2. Extract

Extract the `.zip` file anywhere on your computer. Inside the folder, you will find exactly **4 core files** that power everything:

```text
N_m3u8DL_RE_GUI_v2.1.5/
├── N_m3u8DL_RE_GUI.exe    <-- The main application (Double click this!)
├── N_m3u8DL-RE.exe        <-- The core download engine
├── ffmpeg.exe             <-- The video/audio muxing engine
└── m3u8_cf_bypass.py      <-- The Cloudflare TLS bypass script
```

### 3. Run

Simply double-click `N_m3u8DL_RE_GUI.exe` to launch the application.

> [!NOTE]
> **Python Requirement:** If you plan to use the **Cloudflare Bypass** feature, make sure you have Python installed on your Windows machine, and run `pip install curl_cffi` in your command prompt.

<p align="right">(<a href="#readme-top">back to top</a>)</p>

---

<!-- USAGE -->

## Usage Guide

### Quick Start

1. **Enter URL** - Paste your `.m3u8`, `.mpd`, or stream URL in the top URL field.
2. **Configure Options** - Select desired options from the sidebar tabs (e.g., Audio Only, Sub Only).
3. **Click GO** - The application will automatically generate the CLI command and start downloading.

### Input Methods Supported

| Method      | How to use                            |
| ----------- | ------------------------------------- |
| 📋 Paste from Browser | Copy a request as cURL from browser DevTools (F12) or click **Copy as cURL** in the Browser Extension, then click **📋 Paste from browser**. |
| 🗂️ HAR Capture Drop | Drag a `.har` network capture onto the GUI. If multiple streams are found, an interactive picker window lets you select the master stream. |
| Direct URL  | Paste a stream URL directly into the top bar. |
| Drag & Drop | Drag `.m3u8`, `.mpd`, or `.txt` files directly into the window. |
| Batch File  | Drop a `.txt` file containing multiple URLs (one per line). |
| Folder      | Drop a folder containing stream files to batch process them all. |

### Browser Extension Companion

Use the companion browser extension in `extension/` for 1-click stream capture in Chrome, Edge, and Brave:
1. Open `chrome://extensions` and enable **Developer mode**.
2. Click **Load unpacked** and select the `extension/` folder.
3. Play any video in your browser → click the extension icon → **📋 Copy as cURL**.
4. In the GUI, click **📋 Paste from browser** → URL and required headers are filled instantly!

### How to use Cloudflare Bypass

If a website is blocking you with Cloudflare, open the **Network tab (🌐)** and find the **⚡ Cloudflare Bypass (curl_cffi)** section:
1. Tick **Enable Cloudflare Bypass**.
2. Select a browser fingerprint (e.g., `chrome120`).
3. Enter the website's `Referer` URL if required — leave it blank to derive it from the input URL automatically.
4. Click **▶ GO**. The Python script (`m3u8_cf_bypass.py`) will spoof the browser fingerprint and grab the clearance cookies for you.

> Cloudflare mode uses only the URL, save folder, save name, and the fields in this section. Options set on the other tabs are not passed to the Python script.

<p align="right">(<a href="#readme-top">back to top</a>)</p>

---

<!-- FEATURES -->

## Detailed Features

### Core Features
- **Universal Stream Capture** - Paste browser cURL commands directly, drag-and-drop `.har` captures with automated stream ranking and picking, or use the Chrome/Edge extension companion.
- **3-Zone Modern UX/UI Architecture** - Clean layout with a top URL hero bar, a 6-Tab sidebar (`📦 Download`, `🌐 Network`, `🔒 Security`, `🎬 Media`, `📡 Live`, `⚙️ Advanced`), and a fixed command preview bar at the bottom.
- **GUI Auto-Update Engine** - Zero rate-limit HTTP update checker. If a new version is released, a green pill badge (`🎉 vX.X.X Available!`) will appear at the top.
- **Full RE Support** - Compatible with all major N_m3u8DL-RE command-line arguments.
- **Cloudflare WAF Bypass** - Dedicated amber-accented section on the Network tab with browser TLS fingerprint impersonation (`curl_cffi`), dynamic domain auto-derivation, and Referer/Cookie inputs.
- **Batch Downloads** - Process multiple URLs from text files or drop entire folders of streams.
- **Config Persistence** - Settings are saved automatically between sessions.

### Security and Stability
- **Windows DPAPI Secret Protection** - Your custom headers, proxies, decryption keys, and IVs are safely encrypted via Windows DPAPI in your `config.json` file. No plaintext secrets!
- **Thread-Safe Cancellation** - Responsive process cancellation with clean token lifetime management that safely terminates child process trees.
- **In-Window Live Feedback & Progress** - Real-time progress bar, live status messages, collapsible diagnostic log, and an "Open Folder" button on completion.
- **Accessible & Keyboard Ready** - High-contrast focus visual indicators, access keys (`Alt+G` for Go, `Alt+S` / `Escape` for Stop), and full UI automation properties.
- **Automated Test Suite (617 Tests)** - Rock-solid stability backed by 617 unit, integration, contrast, and accessibility tests covering all core models, services, XAML a11y, and view models.

### Download Options
- **Concurrent Downloads** - Download multiple streams simultaneously.
- **Audio/Subtitle Selection** - Download audio-only or subtitles-only easily.
- **Stream Selection (Regex)** - Select or drop video/audio/subtitle streams by standard regex.
- **Time Range** - Download specific portions of a stream (e.g., `00:05:00-00:10:00`).
- **Speed Limit** - Set a maximum download speed to avoid throttling.
- **Custom Proxy** - Support for HTTP and SOCKS5 proxies.

### Muxing and Output
- **Mux After Done** - Automatically mux video and audio to `.mp4` or `.mkv` with `ffmpeg`.
- **Mux Import** - Import external media files during muxing.
- **Subtitle Format** - Choose between SRT and VTT output formats.

### Live Recording
- **Perform as VOD** - Treat live streams as VOD, allowing full download and pausing.
- **Realtime Merge** - Merge segments in real time without waiting for completion.
- **Pipe Mux** - Direct pipe to muxer to save disk I/O.
- **Record Limit** - Set a maximum recording duration.

### Decryption
- **Engine Selection** - Choose between MP4DECRYPT, SHAKA_PACKAGER, or FFMPEG for real-time MP4 segment decryption.
- **HLS Method Override** - Set a custom HLS decryption method.
- **Key Text File** - Load a massive list of decryption keys directly from a file.

### Advanced Control
- **Custom Headers** - Add HTTP headers (Cookie, User-Agent, Origin, etc.).
- **Thread Control** - Customize thread count, retry limits, and timeout parameters.
- **Auto Subtitle Fix** - Automatically fix subtitle synchronization issues.
- **Save Pattern** - Custom naming pattern for downloaded files.
- **Log Level** - Control output verbosity (OFF/ERROR/WARN/INFO/DEBUG).

<p align="right">(<a href="#readme-top">back to top</a>)</p>

---

<!-- ROADMAP -->

## Roadmap

- [x] Full N_m3u8DL-RE argument support
- [x] Batch download from text files
- [x] English-only standardized UI
- [x] Dark theme with a Zone D status strip and a collapsible log panel
- [x] Stream selection with regex
- [x] Safe config parser and Windows DPAPI secret protection
- [x] GUI Auto-Update checking system
- [x] Download progress and live status visualization
- [x] Keyboard shortcuts, visible focus rings, and screen-reader names on every control
- [x] Full WCAG 2.1 AA contrast compliance and option-conflict dependency visibility
- [ ] Collapsible option groups and task-oriented grouping
- [ ] Queue management

<p align="right">(<a href="#readme-top">back to top</a>)</p>

---

<!-- LICENSE & DISCLAIMER -->

## Disclaimer

This application is a **GUI wrapper only**. All downloading and processing is handled by [N_m3u8DL-RE](https://github.com/nilaoda/N_m3u8DL-RE) and [FFmpeg](https://ffmpeg.org/). For issues related to downloading or media processing failures, please refer to their respective repositories.

## License

Distributed under the MIT License. See `LICENSE.txt` for more information.

<!-- MARKDOWN LINKS & IMAGES -->
[dotnet-shield]: https://img.shields.io/badge/.NET-9.0-512BD4?style=for-the-badge&logo=dotnet&logoColor=white
[dotnet-url]: https://dotnet.microsoft.com/
[wpf-shield]: https://img.shields.io/badge/WPF-Windows-0078D6?style=for-the-badge&logo=windows&logoColor=white
[wpf-url]: https://docs.microsoft.com/en-us/dotnet/desktop/wpf/
[csharp-shield]: https://img.shields.io/badge/C%23-12.0-239120?style=for-the-badge&logo=csharp&logoColor=white
[csharp-url]: https://docs.microsoft.com/en-us/dotnet/csharp/
[license-shield]: https://img.shields.io/badge/License-MIT-green?style=for-the-badge
[license-url]: LICENSE.txt
[product-screenshot]: images/screenshot.png

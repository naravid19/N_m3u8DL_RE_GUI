# N_m3u8DL_RE_GUI

A modern, cross-platform GUI wrapper for the powerful [N_m3u8DL-RE](https://github.com/nilaoda/N_m3u8DL-RE) CLI tool. This application provides a user-friendly interface to download DASH, HLS, and MSS streams with ease.

## Features

- **Intuitive Interface:** Easy-to-use graphical interface for configuring download options.
- **Full N_m3u8DL-RE Support:** Supports key features of the RE version, including:
  - **Concurrent Downloads:** Download multiple streams simultaneously.
  - **Audio/Subtitle Selection:** Options to download only audio or subtitles.
  - **Auto Subtitle Fix:** Automatically fix subtitle issues.
  - **Custom Proxy:** Support for custom proxies and disabling system proxy.
  - **Custom Headers:** Add custom HTTP headers (e.g., Cookie, User-Agent).
  - **Time Range:** Download specific time ranges of a stream.
  - **Mux Import:** Import external media files during muxing.
- **Thread Control:** Customize thread count, retry count, and timeouts.
- **Speed Limit:** Set maximum download speed.

## Requirements

- **N_m3u8DL-RE:** You must have the `N_m3u8DL-RE` executable.
- **FFmpeg:** Required for muxing and certain processing tasks.
- **.NET Runtime:** Ensure you have the necessary .NET runtime installed for the GUI.

## Usage

1.  **Setup:**

    - Place `N_m3u8DL-RE.exe` and `ffmpeg.exe` in the same directory as the GUI application, or ensure they are in your system PATH.
    - Alternatively, configure the paths in the settings if available.

2.  **Download:**
    - **Input:** Paste the `.m3u8`, `.mpd`, or `.ism` URL into the "M3u8 Url" field.
    - **Options:** Select your desired options (e.g., "Audio Only", "Sub Only", "Concurrent Download").
    - **Config:** Adjust "Max Threads", "Retry Count", etc., as needed.
    - **Headers:** Add any required headers in the "Headers" box (format: `Key: Value`).
    - **Run:** Click "GO" to start the download. The generated command arguments will be displayed in the "Params" box.

## New Features in this Version

- **Updated Argument Logic:** Fully compatible with `N_m3u8DL-RE` command-line arguments.
- **Enhanced UI:** Refined layout for better usability.
- **Subtitle Support:** Dedicated controls for subtitle format (SRT/VTT) and selection.

## Disclaimer

This tool is a GUI wrapper. All downloading and processing logic is handled by `N_m3u8DL-RE` and `FFmpeg`. Please refer to their respective repositories for specific issues related to downloading or media processing.

---

_Based on [N_m3u8DL-RE](https://github.com/nilaoda/N_m3u8DL-RE) by nilaoda._

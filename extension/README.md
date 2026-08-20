# N_m3u8DL-RE Companion — Browser Extension

A lightweight Chrome / Edge / Chromium extension that observes network activity while you play a video, automatically detects stream manifests (HLS `.m3u8`, DASH `.mpd`) and progressive media (`.mp4`), and lets you copy the exact cURL command with required headers (`Referer`, `User-Agent`, `Cookie`) into **N_m3u8DL-RE GUI** with 1 click.

---

## 🚀 Installation (Takes 30 seconds)

1. Open your browser's extensions page:
   - **Google Chrome / Brave:** Navigate to `chrome://extensions`
   - **Microsoft Edge:** Navigate to `edge://extensions`
2. Enable **Developer mode** (toggle switch in the top-right corner).
3. Click **Load unpacked** (top-left).
4. Select the `extension/` folder from this repository / release.
5. (Optional) Click the puzzle piece icon in the browser toolbar and pin **N_m3u8DL-RE Companion**.

---

## 🎬 How to Use

1. Navigate to any video/streaming webpage.
2. Start playing the video.
3. The extension icon will show a badge count (e.g. `1`) when a stream is detected.
4. Click the extension icon to view detected streams.
5. Click **📋 Copy as cURL**.
6. Switch to **N_m3u8DL-RE GUI** and click **📋 Paste from browser** (or paste directly into the URL field).
7. All stream URLs and headers (`Referer`, `Cookie`, `User-Agent`) are instantly filled. Click **▶ GO** to download!

---

## 🔒 Permissions & Privacy

- **`webRequest` & `<all_urls>`**: Required to observe network requests across video CDNs in real-time.
- **`storage`**: Uses `chrome.storage.session` to keep detected streams in memory during your browser session.
- **No data is ever logged, stored remotely, or transmitted anywhere.** The extension runs 100% locally in your browser.

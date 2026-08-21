# N_m3u8DL-RE Companion — Browser Extension (v1.1.0)

A lightweight Chrome / Edge / Chromium extension (Manifest V3) that observes network activity while you play a video, automatically detects stream manifests (HLS `.m3u8`, DASH `.mpd`, Abyss/Hydrax) and progressive media (`.mp4`, `.webm`), and lets you copy the exact cURL command with required headers (`Referer`, `User-Agent`, `Cookie`, `Origin`) into **N_m3u8DL-RE GUI** with 1 click.

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
7. All stream URLs and headers (`Referer`, `Cookie`, `User-Agent`, `Origin`) are instantly filled. Click **▶ GO** to download!

---

## 🔒 Permissions & Privacy

When installing this extension unpacked or from source, the browser prompts for host permissions (`<all_urls>`). Here is a clear, transparent explanation of what is used and why:

* **Why `<all_urls>` is required:** Video streams and manifests are hosted on third-party Content Delivery Networks (CDNs) and dynamic media servers whose domains cannot be known in advance. Intercepting outgoing request headers (such as `Cookie` and `Referer` required for protected stream playback) requires host permission matching the CDN origin.
* **What is captured:** Only requests that classify as video streams or manifests (HLS, DASH, Abyss, progressive MP4/WebM). For each detected stream, the extension records:
  - Stream URL
  - Standard playback headers: `Referer`, `User-Agent`, `Cookie`, and `Origin`
  - Originating Tab ID and timestamp
* **Where it is kept:** Stored strictly in `chrome.storage.session` (in-memory only). Data survives background service-worker sleeps within the browsing session, but is **instantly purged when the browser is closed**. Captured session cookies are **never written unencrypted to disk**.
* **Where it is sent:** **Nowhere.** The extension contains zero outbound telemetry, analytical trackers, or external API endpoints. Captured stream details leave the extension only when you explicitly click **"📋 Copy as cURL"** or **"Copy URL"** to place them on your local system clipboard.
* **Scope & Reach:** The extension only inspects requests initiated by the browser during your navigation. It cannot access or request content that the browser itself cannot access.

---

## 🧪 Testing

Run the automated test suite from the repository root:

```bash
node --test "extension/test/*.test.js"
```

> [!NOTE]
> On Windows, use the quoted glob `"extension/test/*.test.js"` (or `cd extension && node --test`) rather than a bare directory argument so Node resolves test files accurately.


# N-RE Stream Bridge — Browser Extension (v1.3.0)

A lightweight Chrome / Edge / Chromium extension (Manifest V3) that observes network activity while you play a video, automatically detects stream manifests (HLS `.m3u8`, DASH `.mpd`, Smooth Streaming `.ism`/`.isml`, Abyss/Hydrax), audio streams (`.m4a`, `.opus`, `.flac`, `.wav`, `.aac`, `.mp3`), and progressive media (`.mp4`, `.m4v`, `.webm`, `.mkv`, `.mov`, `.flv`, `.ogv`, `.3gp`), and lets you copy the exact cURL command with required headers (`Referer`, `User-Agent`, `Cookie`, `Origin`) into **N_m3u8DL-RE GUI** with 1 click.

---

## 🌟 Key Features

* **Quality Probing on Demand:** Click **`▸ Qualities`** on any manifest (HLS/DASH/MSS) to inspect available video renditions (e.g. `1080p · 5.0 Mbps`, `720p · 2.5 Mbps`). The extension fetches the manifest only when requested, replaying captured credentials so CDN authentication passes.
* **Direct Quality Handoff via `# nre-*:` Directives:** Selecting a quality attaches a shell comment `# nre-select-video: res="1080*"` to the cURL payload. The GUI automatically applies the resolution selector to the download configuration without breaking bash cURL compatibility.
* **Multi-Select & Batch Export:** Select multiple stream URLs using checkboxes or Select All, then click **`📋 Copy as list`**. Pasting into **N_m3u8DL-RE GUI** instantly generates a numbered batch run.
* **Page Grouping & 420px Adaptive UI:** "All Recent" streams are neatly grouped by origin domain with visible card hierarchy, live relative timestamps, and WCAG AA contrast compliance.

---

## 📺 Supported Stream Formats

| Format / Kind | Detection Rules & MIME Types |
|---|---|
| **HLS** | `.m3u8`, `.m3u` path extension; `*mpegurl*` content types (`application/x-mpegurl`, `audio/x-mpegurl`) |
| **DASH** | `.mpd` path extension; `application/dash+xml`, `video/vnd.mpeg.dash.mpd` |
| **Smooth Streaming (MSS)** | `.ism`, `.isml` path extensions, paths ending in `/Manifest`; `application/vnd.ms-sstr+xml` |
| **Abyss / Hydrax** | `abysscdn.com/?v=`, `playhydrax.com/?v=`, `zplayer.io/?v=`, `abyss.to/?v=`, `short.ink/` |
| **Progressive Media** | `.mp4`, `.m4v`, `.webm`, `.mkv`, `.mov`, `.flv`, `.ogv`, `.3gp` extensions; `video/*` content type |
| **Standalone Audio** | `.m4a`, `.opus`, `.flac`, `.wav`, `.oga` extensions; `.aac`/`.mp3` with explicit `audio/*` content type |
| **Low-confidence Hints** | Manifest extensions or format hints (`?type=m3u8`, `?format=hls`, `?format=mpd`) in query strings (labeled `guess`) |

> [!NOTE]
> **What is NOT detected:** DRM-encrypted streams (Widevine/PlayReady keys are not bypassed by network capture) and streams that the browser itself cannot access.

---

## 🚀 Installation (Takes 30 seconds)

1. Open your browser's extensions page:
   - **Google Chrome / Brave:** Navigate to `chrome://extensions`
   - **Microsoft Edge:** Navigate to `edge://extensions`
2. Enable **Developer mode** (toggle switch in the top-right corner).
3. Click **Load unpacked** (top-left).
4. Select the `extension/` folder from this repository / release.
5. (Optional) Click the puzzle piece icon in the browser toolbar and pin **N-RE Stream Bridge**.

---

## 🎬 How to Use

### Single Stream with Quality Choice
1. Navigate to any video/streaming webpage and play the video.
2. Click the extension icon.
3. Click **`▸ Qualities`** to expand and select your preferred rendition (e.g. `1080p`, `720p`).
4. Click **`📋 Copy as cURL`**.
5. Switch to **N_m3u8DL-RE GUI** and click **`📋 Paste from browser`** (or press Ctrl+V).
6. Stream URL, headers, and the quality selector are filled automatically. Click **▶ GO**!

### Batch Multi-Stream Download
1. Browse to multiple episodes or open several video tabs.
2. In the popup, switch to **All Recent** or select desired streams via checkboxes.
3. Click **`📋 Copy as list`**.
4. In the GUI, click **`📋 Paste from browser`** or paste into the URL box.
5. The GUI loads all URLs as a batch queue. Click **▶ GO** to download all in order!

---

## 🔒 Permissions & Privacy

When installing this extension unpacked or from source, the browser prompts for host permissions (`<all_urls>`). Here is a clear, transparent explanation of what is used and why:

* **Why `<all_urls>` is required:** Video streams and manifests are hosted on third-party Content Delivery Networks (CDNs) and dynamic media servers whose domains cannot be known in advance. Intercepting outgoing request headers (such as `Cookie` and `Referer` required for protected stream playback) requires host permission matching the CDN origin.
* **What is captured:** Only requests that classify as video streams or manifests (HLS, DASH, MSS, Abyss, progressive MP4/WebM, audio). For each detected stream, the extension records:
  - Stream URL
  - Standard playback headers: `Referer`, `User-Agent`, `Cookie`, and `Origin`
  - Originating Tab ID and timestamp
* **Where it is kept:** Stored strictly in `chrome.storage.session` (in-memory only). Data survives background service-worker sleeps within the browsing session, but is **instantly purged when the browser is closed**. Captured session cookies are **never written unencrypted to disk**.
* **Where it is sent:** **Nowhere.** The extension contains zero outbound telemetry, analytical trackers, or external API endpoints. Captured stream details leave the extension only when you explicitly click **"📋 Copy as cURL"** or **"Copy as list"** to place them on your local system clipboard.
* **On-Demand Probing Restraint:** Probing manifest qualities makes exactly one network request to the manifest URL **only when you click "Qualities"** and never unprompted in the background.

---

## 🧪 Testing

Run the automated test suite from the repository root:

```bash
node --test "extension/test/*.test.js"
```

> [!NOTE]
> On Windows, use the quoted glob `"extension/test/*.test.js"` (or `cd extension && node --test`) rather than a bare directory argument so Node resolves test files accurately.

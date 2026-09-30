# N-RE Stream Bridge — Browser Extension (v1.4.5)

A lightweight Chrome / Edge / Chromium extension (Manifest V3) that observes network activity while you play a video, automatically detects stream manifests (HLS `.m3u8`, DASH `.mpd`, Smooth Streaming `.ism`/`.isml`, Abyss/Hydrax), audio streams (`.m4a`, `.opus`, `.flac`, `.wav`, `.oga`, `.weba`, `.wma`, `.aac`, `.mp3`), and progressive media (`.mp4`, `.m4v`, `.webm`, `.mkv`, `.mov`, `.flv`, `.ogv`, `.3gp`, `.avi`, `.wmv`, `.asf`, `.divx`, `.f4v`, `.mpeg`, `.mpg`), and lets you copy the exact cURL command with required headers (`Referer`, `User-Agent`, `Cookie`, `Origin`) into **N_m3u8DL-RE GUI** with 1 click.

---

## 🌟 Key Features

* **Deep In-Page Interception (MAIN World):** Hooks `JSON.parse` within the page's execution environment to discover hidden manifest URLs delivered inside API payloads that never make direct network requests. Hardened with a strict node walk budget (`MAX_NODES = 5000`), a bounded LRU cache, and non-blocking event-channel relays. Encryption keys are not sniffed; paste a `--key` yourself.
* **Ad & Tracker CDN Blocklist:** Silently drops video ad prerolls, tracking beacons, and analytics traffic from DoubleClick, Google Ad Services, imasdk, Criteo, Taboola, and Outbrain before classification.
* **Auto Page Title & Windows Sanitization:** Scrapes page titles from `og:title`, `twitter:title`, and `document.title`, strips OS-forbidden filename characters, and attaches a clean title hint to detected streams for automatic GUI naming.
* **Quality Probing on Demand:** Click **`▸ Qualities`** on any manifest (HLS/DASH/MSS) to inspect available video renditions (e.g. `1080p · 5.0 Mbps`, `720p · 2.5 Mbps`). The extension fetches the manifest only when requested, streaming chunks with a strict 2MB ceiling, replaying captured credentials so CDN authentication passes safely.
* **Direct Quality Handoff via `# nre-*:` Directives:** Selecting a quality attaches a shell comment `# nre-select-video: res="1080*"` to the cURL payload. The GUI automatically applies the resolution selector to the download configuration without breaking bash cURL compatibility.
* **Content-Disposition Header Classification:** Fallback filename detection from `Content-Disposition: attachment; filename="..."` headers so streams with extensionless or opaque query URLs are classified properly.
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
| **Progressive Media** | `.mp4`, `.m4v`, `.webm`, `.mkv`, `.mov`, `.flv`, `.ogv`, `.3gp`, `.avi`, `.wmv`, `.asf`, `.divx`, `.f4v`, `.mpeg`, `.mpg` extensions or `Content-Disposition` filename; `video/*` content type |
| **Standalone Audio** | `.m4a`, `.opus`, `.flac`, `.wav`, `.oga`, `.weba`, `.wma` extensions or `Content-Disposition` filename; `.aac`/`.mp3` with explicit `audio/*` content type |
| **Low-confidence Hints** | Manifest extensions or format hints (`?type=m3u8`, `?format=hls`, `?format=mpd`) in query strings (labeled `guess`) |

> [!NOTE]
> **What is NOT detected:** DRM-encrypted streams (Widevine/PlayReady keys are not bypassed by network capture) and streams that the browser itself cannot access.

---

## 🚀 Installation (Takes 30 seconds)

> [!NOTE]
> **Requires Chrome 111 or newer** (Chrome, Edge, or Brave on an equivalent build). The manifest declares this, so an older browser refuses the install instead of loading an extension whose popup cannot start.

> [!NOTE]
> **Incognito is isolated.** If you enable the extension in incognito, anything it captures there stays there — incognito runs its own instance with its own memory, so a stream sniffed in a private window never appears in the normal window's list.

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
4. Click **`⬇ Download`** to send straight to the GUI and start downloading immediately (or **`📋 Copy as cURL`**).
5. If using cURL copy: switch to **N_m3u8DL-RE GUI** and click **`📋 Paste from browser`** (or press Ctrl+V).
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

* **Why `nativeMessaging` is required:** it is what lets the `⬇ Download` button hand a stream to N_m3u8DL-RE GUI directly instead of going through the clipboard. Chrome will only connect the extension to the one host the GUI registers (`com.nm3u8dlre.gui`), and only that host's manifest — which names this extension's ID explicitly — is allowed to answer. No web page can reach this channel; the permission grants no network access and no access to any other program.
* **Why `<all_urls>` is required:** Video streams and manifests are hosted on third-party Content Delivery Networks (CDNs) and dynamic media servers whose domains cannot be known in advance. Intercepting outgoing request headers (such as `Cookie` and `Referer` required for protected stream playback) requires host permission matching the CDN origin.
* **Why `cookies` is required:** Cloudflare's `cf_clearance` cookie is issued against the *page's* origin, not the CDN a stream is fetched from, so the header captured off the network request alone can miss it. On "Copy as cURL", the extension reads `chrome.cookies.getAll()` for both the stream and page domains and merges the result into the payload — the request's own captured `Cookie` header always wins on a name collision. This only runs when you click Copy; it is never read in the background.
* **What is captured:** Only requests that classify as video streams or manifests (HLS, DASH, MSS, Abyss, progressive MP4/WebM, audio). For each detected stream, the extension records:
  - Stream URL
  - Every request header that survives the forwarding policy (drops transport-level headers like `Host`/`Content-Length` and browser fingerprint headers like `Sec-*`) — not just `Referer`/`User-Agent`/`Cookie`/`Origin`, so sites gating a manifest behind `Authorization` or a custom token header are still capturable
  - Originating Tab ID and timestamp
* **Where it is kept:** Stored strictly in `chrome.storage.session` (in-memory only). Data survives background service-worker sleeps within the browsing session, but is **instantly purged when the browser is closed**. Captured session cookies are **never written unencrypted to disk**. Incognito browsing runs a separate instance with separate storage (`"incognito": "split"`), so captures never cross from a private window into the normal one.
* **Where it is sent:** **Nowhere.** The extension contains zero outbound telemetry, analytical trackers, or external API endpoints. Captured stream details leave the extension only when you explicitly click **"📋 Copy as cURL"** or **"Copy as list"** to place them on your local system clipboard.
* **On-Demand Probing Restraint:** Probing manifest qualities makes exactly one network request to the manifest URL **only when you click "Qualities"** and never unprompted in the background.

---

## 🧪 Testing

From inside `extension/`:

```bash
npm test
```

Or from the repository root:

```bash
node --test "extension/test/*.test.js"
```

> [!NOTE]
> On Windows, use the quoted glob `"extension/test/*.test.js"` (or `cd extension && node --test`) rather than a bare directory argument so Node resolves test files accurately.

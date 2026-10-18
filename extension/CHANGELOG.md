# Changelog

All notable changes to the **N-RE Stream Bridge** browser extension will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

---

## [Unreleased]

## [1.4.6] - 2026-10-16

### Fixed

- **Waiting-state scan line** stayed still or vanished when the OS asks for reduced motion, because the global reduced-motion rule jumped it to its last frame, off the track. It now sweeps back and forth in every motion setting.
- **Copied button** no longer shows an accent ring around its green fill.
- **Desktop-app notice** came back right after *Dismiss*: the missing-app toast repeated on every Download click and spelled out the steps again. Dismiss now snoozes the notice for 7 days (it returns sooner if the app was found in between and then lost), a failed storage write no longer re-shows it in the same popup, and the toast appears once per popup open.

### Changed

- **Popup matches the redesign more closely**: the desktop-app notice sits under the header with *Check again* as the accent action and *Get the app* / *Dismiss* as text buttons; toasts float above the footer instead of resizing it; filled buttons carry the accent border; *URL only* is a text button; the header keeps the extension icon and name and drops the stream-count badge (the tabs already show counts); the *App linked* dot breathes.
- **Popup Redesign (flat rows, shared app palette)**: the popup now uses the desktop app's dark palette. Stream cards become flat rows divided by hairlines, the scope tabs are a segmented control with live counts (*Current Tab 3*, *All Recent 14*), and the *Delete temp files after download* option is a switch in the footer with a note that it applies to both Download and Copy as cURL. Every stream carries the same two button rows, so no single stream looks like "the one to download"; the best-match ranking only shows as a quiet border. The waiting state is rewritten ("Listening on this tab", three steps, and a card for streams captured on other tabs).
- **Step numbers in the waiting state** use the muted text colour to meet AA contrast.

### Added

- **App Link Status in the Header**: a pill shows *Checking app*, *App linked* or *App not found*. The native-host probe now starts when the popup opens, so the pill settles even when no stream is detected.
- **Subtitle Track Choice in Qualities Panel**: HLS `#EXT-X-MEDIA:TYPE=SUBTITLES` and DASH text `AdaptationSet`s appear as a *Subtitles* radio group (default *No subtitles*), sent as `# nre-select-subtitle: lang="xx":for=best`. DASH text sets are no longer mistaken for video renditions. One subtitle track per download; `CLOSED-CAPTIONS` are not listed.

- **Audio Track Choice in Qualities Panel**: when a manifest lists two or more distinct audio renditions (HLS `#EXT-X-MEDIA:TYPE=AUDIO`, DASH audio `AdaptationSet` with `lang`), `▸ Qualities` shows an *Audio* radio group. The pick is sent as `# nre-select-audio: lang="xx":for=best` (or `name="…"` when there is no language); the GUI already reads that directive. `parseHlsMaster` now also returns the audio entries (`kind: 'audio'`, after the video variants). Subtitles are not covered yet.

- **DRM Warning in Qualities Panel**: `▸ Qualities` now shows "DRM protected — needs a decryption key (--key)" when the manifest declares Widevine, PlayReady or FairPlay (HLS `#EXT-X-KEY`/`#EXT-X-SESSION-KEY` key formats, DASH `ContentProtection`, MSS `ProtectionHeader`). Plain AES-128 and SAMPLE-AES with an ordinary key URI are not flagged. Limit: a master playlist rarely carries key tags, so HLS DRM that lives only in a media playlist is not detected yet.

- **Delete Temp Files Toggle**: the popup has a *Delete temp files after download* checkbox (default on, stored in `chrome.storage.local` as `del_after_done`). `toCurl` emits `# nre-del-after-done: 1|0` for it, so Download and Copy as cURL carry the same choice. Nothing is emitted when the option is not a boolean.

- **Firefox Extension Support (`dist-firefox/`)**:
  - `build:firefox` script derives a Manifest V3 distribution for Firefox with Gecko ID `nre-stream-bridge@naravid19`, `strict_min_version: "128.0"`, and module event page background scripts.
  - Firefox-tolerant `webRequest` handling with automatic fallback when `extraHeaders` is rejected, and referer recovery from `details.originUrl`.
  - Added unit test suite covering Firefox manifest generation (`test/firefox-manifest.test.js`) and cross-project C# constant parity.

## [1.4.5] - 2026-09-30

### Added

- **Dynamic Action Hierarchy & Primary CTA Promotion (`popup/`)**:
  - Stream cards now feature a distinct two-row action structure based on GUI Native Host connectivity.
  - When the GUI Native Host is online: Row 1 displays a full-width `⬇ Download` button (`.btn-primary`); Row 2 (Secondary Toolbar) hosts `📋 Copy as cURL`, `URL only`, and `▸ Qualities`.
  - When the GUI Native Host is offline: `⬇ Download` hides via `[hidden]`, and `📋 Copy as cURL` dynamically promotes to the full-width Primary CTA in Row 1 (`.btn-primary`), with Row 2 displaying `URL only` and `▸ Qualities`.
- **Manifest Loading Skeleton Shimmer Loader**:
  - Replaced the hollow text status box with a 3-row animated shimmer skeleton loader (`.skeleton-line`, `@keyframes shimmer`) matching actual quality option dimensions when probing manifests.
  - Full accessibility compliance respecting `@media (prefers-reduced-motion: reduce)`.
- **Monospace Filename & Query Parameter Separation**:
  - Card headers now split URLs into logical stream filenames (bold monospace text) and query string tokens (`.url-query`, muted secondary tone), preventing authentication/session tokens from obscuring clean media titles.
- **Automated Test Suite Expansion (361 Passing Tests)**:
  - Added dedicated unit tests covering CTA promotion (`test/popup-cta.test.js`), CSS specificity and display invariants (`test/popup-css.test.js`), design system tokens (`test/design-system.test.js`), manifest probing retry behavior (`test/probe.test.js`), and version synchronization (`test/version.test.js`).

### Changed

- **Stream Card Visual Alignment**: Replaced the asymmetrical 3.5px left border slab on active/primary stream cards with a uniform 1px `--accent-dim` border and subtle surface tint, maintaining WCAG AA contrast compliance.
- **Suite Release Update Check**: `checkSuiteUpdate()` queries GitHub releases against the generated `suite-version` with a 24-hour cache TTL and non-throwing network resilience when offline.
- **Tab-Scoped Stream Dismissals**: Clearing or dismissing streams (`clearTab`, `dismissStreams`) scopes state cleanly to the active tab ID with bounded LRU eviction, preventing tab cross-contamination.

### Fixed

- **CSS Specificity `[hidden]` Display Override Bug**: `.btn` (`display: inline-flex;`) and `.bulk-bar` (`display: flex;`) previously overrode the browser User-Agent `hidden` attribute due to missing specificity rules in `popup.css`. This squashed all four buttons into a single 400px row when offline with awkward multi-line wraps ("Copy \n as \n cURL") and kept an empty bulk selection bar permanently visible. Fixed with explicit `[hidden] { display: none !important; }` and button `white-space: nowrap`.
- **Manifest Probing Retry Re-fetch**: Clicking `Retry` on failed manifest quality probes now forces an active network re-probe instead of replaying the cached error response.

---

## [1.4.4] - 2026-08-27

### Fixed

- **Deep In-Page Interception Hardening (`deep-detect.js`)**:
  - **Main-Thread CPU Stall Guard**: Enforced a strict node walk budget (`MAX_NODES = 5000`) and max string length limit (`MAX_STRING = 2048`) on `JSON.parse` inspection, eliminating UI thread lag when web applications load wide data payloads.
  - **Bounded Seen-URLs Cache**: Replaced unbounded sets with a 512-item LRU eviction cache matching `content.js` policy.
  - **Single Listener Attachment per XHR**: Attached `load` event listeners once per `XMLHttpRequest` instance on `open()`, preventing duplicate event cascades on reused XHR instances.
  - **Isolated Relay Channel**: Cleaned up invalid `chrome.runtime.sendMessage` calls from the MAIN world context, funneling all in-page detections through `window.postMessage`.

### Removed

- **Dead Inline Manifest Blob URLs**: Dropped un-revoked `#EXTM3U` blob URL generation in `deep-detect.js` which generated un-revoked `Blob` objects that were unusable by CLI engines and produced memory leaks.
- **Arbitrary 16-Number Array Sniffing**: Removed the loose 16-number array heuristic from JSON object walks to prevent false positive key detection warnings.

---

## [1.4.3] - 2026-08-27

### Added

- **Deep In-Page Interception (MAIN World Execution Context)**: Injected `deep-detect.js` into the page execution environment (`world: "MAIN"`) at `document_start` to intercept `JSON.parse` returns and uncover hidden manifest URLs (`.m3u8`, `.mpd`, `.ism`) inside API payloads that never make direct browser network requests.
- **Ad & Tracker CDN Blocklist**: Substring filtering in `lib/ad-blocklist.js` silently drops video ad prerolls, tracking beacons, and analytics traffic (DoubleClick, Google Ad Services, imasdk, Criteo, Taboola, Outbrain) before classification.
- **Page Title Extraction & Windows Filename Sanitization**: Scrapes page titles from `og:title`, `twitter:title`, and `document.title`, sanitizing illegal Windows filename characters (`\/:*?"<>|`) to provide clean filename hints (`# nre-save-name:`) for the GUI.

---

## [1.4.2] - 2026-08-27

### Added

- **Content-Disposition Filename Parsing**: Added header classification fallback to parse `Content-Disposition: attachment; filename="..."` when a media stream URL has an opaque or query-only extension.

### Changed

- **Debounced Background Storage Writes**: Bounded and debounced sequential stream detections to reduce disk and session storage write churn.

### Fixed

- **Manifest Probing OOM Protection**: Replaced unconstrained `response.text()` calls in `lib/probe.js` with a chunked streaming reader enforcing a strict 2MB ceiling, aborting immediately on oversized manifests.

---

## [1.4.1] - 2026-08-24

### Added

- **1-Click Download via Native Messaging (`com.nm3u8dlre.gui`)**: Added `⬇ Download` button in stream cards to transmit capture payloads directly to N_m3u8DL-RE GUI over Chrome Native Messaging without touching the clipboard.
- **Pinned Extension ID**: Added fixed public `key` in `manifest.json` ensuring deterministic extension identity across environments.
- **Cookie Store Merging (`lib/cookies.js`)**: Merges captured request headers with full session cookies from `chrome.cookies.getAll()` on copy/download to preserve Cloudflare clearance tokens.
- **Wire Format v3 (`lib/curl.js`)**: Emits extended capture directives (`# nre-page-url:`, `# nre-impersonate:`, `# nre-cf:`, `# nre-select-video:`).

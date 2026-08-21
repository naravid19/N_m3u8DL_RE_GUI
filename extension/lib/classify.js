/**
 * Pure stream classification. No chrome.* access, so this module imports
 * cleanly under `node --test`.
 */

export const SEGMENT_EXTENSIONS = [
  '.ts', '.m4s', '.aac', '.mp3', '.vtt', '.cmfv', '.cmfa', '.cmft', '.init', '.key'
];

export function classify(url, mimeType, status, type) {
  if (!url || typeof url !== 'string') return null;
  if (!url.startsWith('http://') && !url.startsWith('https://')) return null;

  const lowerUrl = url.toLowerCase();
  if (lowerUrl.includes('#mp4/') || lowerUrl.includes('#hls/') || lowerUrl.includes('maxchunksize=')) {
    return null;
  }

  // 1. Abyss / Hydrax stream endpoints
  if (
    lowerUrl.includes('abysscdn.com/?v=') ||
    lowerUrl.includes('playhydrax.com/?v=') ||
    lowerUrl.includes('zplayer.io/?v=') ||
    lowerUrl.includes('abyss.to/?v=') ||
    lowerUrl.includes('short.ink/')
  ) {
    return 'Abyss';
  }

  // 2. Exclude segments (unless it's an m3u8 / mpd playlist)
  const isManifest = lowerUrl.includes('.m3u8') || lowerUrl.includes('.m3u') || lowerUrl.includes('.mpd');
  if (!isManifest) {
    for (const ext of SEGMENT_EXTENSIONS) {
      if (lowerUrl.includes(ext)) return null;
    }
  }

  // 3. HLS Manifests
  if (lowerUrl.includes('.m3u8') || lowerUrl.includes('.m3u')) {
    return 'HLS';
  }

  // 4. DASH Manifests
  if (lowerUrl.includes('.mpd')) {
    return 'DASH';
  }

  // 4. MIME Type matches
  const mime = (mimeType || '').toLowerCase();
  if (mime.includes('mpegurl') || mime.includes('application/x-mpegurl') || mime.includes('audio/x-mpegurl')) {
    return 'HLS';
  }
  if (mime.includes('dash+xml')) {
    return 'DASH';
  }

  // 5. Progressive Video Streams
  if (
    lowerUrl.includes('.mp4') ||
    lowerUrl.includes('.webm') ||
    lowerUrl.includes('.mkv') ||
    lowerUrl.includes('.mov') ||
    lowerUrl.includes('.flv')
  ) {
    return 'Media';
  }

  if (mime.startsWith('video/') || type === 'media') {
    return 'Media';
  }

  return null;
}

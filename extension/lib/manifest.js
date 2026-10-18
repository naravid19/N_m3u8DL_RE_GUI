/**
 * Pure manifest parsers for HLS master playlists and DASH MPD manifests.
 * Runs in both browser popup and node --test (pure JS, zero DOMParser dependency).
 */
import { formatBitrate } from './format.js';

/**
 * Parses an HLS attribute list (e.g. BANDWIDTH=5000000,CODECS="avc1,mp4a",AUDIO="aud").
 * Correctly preserves quoted strings containing commas.
 */
export function parseAttributeList(attrStr) {
  const result = {};
  if (!attrStr || typeof attrStr !== 'string') return result;

  let key = '';
  let value = '';
  let inQuotes = false;
  let parsingKey = true;

  for (let i = 0; i < attrStr.length; i++) {
    const char = attrStr[i];

    if (char === '"') {
      inQuotes = !inQuotes;
    } else if (char === '=' && parsingKey && !inQuotes) {
      parsingKey = false;
    } else if (char === ',' && !inQuotes) {
      if (key.trim()) {
        result[key.trim()] = value.trim();
      }
      key = '';
      value = '';
      parsingKey = true;
    } else {
      if (parsingKey) {
        key += char;
      } else {
        value += char;
      }
    }
  }

  if (key.trim()) {
    result[key.trim()] = value.trim();
  }

  return result;
}

/**
 * Describes a variant in a human-friendly string (e.g. "1080p · 5.0 Mbps", "audio · 128 kbps").
 */
export function describeVariant(v) {
  if (!v) return '';

  const bitrate = v.bandwidth ? formatBitrate(v.bandwidth) : '';

  if (v.height) {
    return bitrate ? `${v.height}p · ${bitrate}` : `${v.height}p`;
  }

  const kind = v.kind || 'video';
  if (bitrate) {
    return `${kind} · ${bitrate}`;
  }

  return kind;
}

/**
 * Parses an HLS Master Playlist text into a sorted list of Variant objects.
 */
export function parseHlsMaster(text, baseUrl) {
  if (!text || typeof text !== 'string' || !text.includes('#EXTM3U')) {
    return [];
  }

  const lines = text.split(/\r?\n/);
  const variants = [];

  for (let i = 0; i < lines.length; i++) {
    const line = lines[i].trim();
    if (!line.startsWith('#EXT-X-STREAM-INF:')) continue;

    const attrStr = line.slice('#EXT-X-STREAM-INF:'.length);
    const attrs = parseAttributeList(attrStr);

    // Look for the next non-empty, non-comment line for the variant URI
    let uri = null;
    for (let j = i + 1; j < lines.length; j++) {
      const nextLine = lines[j].trim();
      if (!nextLine) continue;
      if (nextLine.startsWith('#EXT-X-STREAM-INF:')) break;
      if (nextLine.startsWith('#')) continue;
      uri = nextLine;
      break;
    }

    if (!uri) continue;

    let width = null;
    let height = null;
    if (attrs.RESOLUTION) {
      const resMatch = /^(\d+)x(\d+)$/i.exec(attrs.RESOLUTION);
      if (resMatch) {
        width = Number.parseInt(resMatch[1], 10);
        height = Number.parseInt(resMatch[2], 10);
      }
    }

    const bandwidth = attrs.BANDWIDTH ? Number.parseInt(attrs.BANDWIDTH, 10) : null;
    const codecs = attrs.CODECS || null;

    let resolvedUrl = uri;
    try {
      resolvedUrl = new URL(uri, baseUrl).href;
    } catch {
      // keep relative or original uri on error
    }

    const variant = {
      kind: 'video',
      width: Number.isFinite(width) ? width : null,
      height: Number.isFinite(height) ? height : null,
      bandwidth: Number.isFinite(bandwidth) ? bandwidth : null,
      codecs,
      url: resolvedUrl,
      label: ''
    };
    variant.label = describeVariant(variant);

    variants.push(variant);
  }

  // Sort descending by height, then by bandwidth
  variants.sort((a, b) => (b.height || 0) - (a.height || 0) || (b.bandwidth || 0) - (a.bandwidth || 0));

  // Alternate audio and subtitle renditions (#EXT-X-MEDIA) come after the video variants.
  const MEDIA_KINDS = { AUDIO: 'audio', SUBTITLES: 'subtitle' };
  for (const line of lines) {
    const trimmed = line.trim();
    if (!trimmed.startsWith('#EXT-X-MEDIA:')) continue;
    const attrs = parseAttributeList(trimmed.slice('#EXT-X-MEDIA:'.length));
    const mediaKind = MEDIA_KINDS[attrs.TYPE];
    if (!mediaKind) continue;

    variants.push({
      kind: mediaKind,
      width: null,
      height: null,
      bandwidth: null,
      codecs: null,
      lang: attrs.LANGUAGE || null,
      name: attrs.NAME || null,
      url: null,
      label: attrs.NAME || attrs.LANGUAGE || mediaKind
    });
  }

  return variants;
}

/**
 * The --select-audio / --select-subtitle expression for one audio or subtitle
 * rendition, or null when it has neither a language nor a name to match on.
 * Quotes are dropped from the values because the engine's filter syntax has no
 * escape for them.
 */
export function trackSelector(v) {
  if (!v || (v.kind !== 'audio' && v.kind !== 'subtitle')) return null;
  const clean = (value) => String(value).replace(/"/g, '');
  if (v.lang) return `lang="${clean(v.lang)}":for=best`;
  if (v.name) return `name="${clean(v.name)}":for=best`;
  return null;
}

/**
 * ponytail: Regex-based MPD parser for standard single-period DASH manifests.
 * Upgrade path: if complex multi-Period dynamic MPDs are needed, replace with lightweight XML parser.
 */
export function parseDashManifest(text, baseUrl) {
  if (!text || typeof text !== 'string' || !/<MPD/i.test(text)) {
    return [];
  }

  const variants = [];
  const adaptationChunks = text.split(/<AdaptationSet/i).slice(1);

  for (const chunk of adaptationChunks) {
    // Determine AdaptationSet kind from mimeType / contentType / lang
    const mimeMatch = /mimeType=["']([^"']+)["']/i.exec(chunk);
    const mime = (mimeMatch ? mimeMatch[1] : '').toLowerCase();
    const isAudio = mime.includes('audio') || /contentType=["']audio["']/i.test(chunk);
    const isText = /contentType=["']text["']/i.test(chunk) || /^(text\/|application\/(ttml|x-subrip))/.test(mime);
    const kind = isAudio ? 'audio' : isText ? 'subtitle' : 'video';
    const langMatch = /\blang=["']([^"']+)["']/i.exec(chunk.split(/<Representation/i)[0]);

    // Parse AdaptationSet level dimension fallback (width / maxWidth, height / maxHeight)
    const adaptWidthMatch = /\b(?:width|maxWidth)=["'](\d+)["']/i.exec(chunk);
    const adaptHeightMatch = /\b(?:height|maxHeight)=["'](\d+)["']/i.exec(chunk);
    const adaptWidth = adaptWidthMatch ? Number.parseInt(adaptWidthMatch[1], 10) : null;
    const adaptHeight = adaptHeightMatch ? Number.parseInt(adaptHeightMatch[1], 10) : null;

    // Parse each <Representation ... /> or <Representation ...> inside
    const repRegex = /<Representation\b([^>]*)/gi;
    let repMatch;

    while ((repMatch = repRegex.exec(chunk)) !== null) {
      const repAttrs = repMatch[1];

      const widthMatch = /\bwidth=["'](\d+)["']/i.exec(repAttrs);
      const heightMatch = /\bheight=["'](\d+)["']/i.exec(repAttrs);
      const bwMatch = /\bbandwidth=["'](\d+)["']/i.exec(repAttrs);
      const codecsMatch = /\bcodecs=["']([^"']+)["']/i.exec(repAttrs);

      const width = widthMatch ? Number.parseInt(widthMatch[1], 10) : adaptWidth;
      const height = heightMatch ? Number.parseInt(heightMatch[1], 10) : adaptHeight;
      const bandwidth = bwMatch ? Number.parseInt(bwMatch[1], 10) : null;
      const codecs = codecsMatch ? codecsMatch[1] : null;

      const variant = {
        kind,
        width: Number.isFinite(width) ? width : null,
        height: Number.isFinite(height) ? height : null,
        bandwidth: Number.isFinite(bandwidth) ? bandwidth : null,
        codecs,
        lang: langMatch ? langMatch[1] : null,
        url: null,
        label: ''
      };
      variant.label = describeVariant(variant);

      variants.push(variant);
    }
  }

  // Sort video, audio, subtitle; then highest height, then bandwidth
  const rank = { video: 0, audio: 1, subtitle: 2 };
  return variants.sort((a, b) => {
    if (a.kind !== b.kind) return rank[a.kind] - rank[b.kind];
    return (b.height || 0) - (a.height || 0) || (b.bandwidth || 0) - (a.bandwidth || 0);
  });
}

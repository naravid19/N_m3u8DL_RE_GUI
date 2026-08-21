/**
 * Pure manifest parsers for HLS master playlists and DASH MPD manifests.
 * Runs in both browser popup and node --test (pure JS, zero DOMParser dependency).
 */

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
 * Formats bandwidth (bits per second) into a clean Mbps / kbps string.
 */
function formatBitrate(bps) {
  if (!bps || typeof bps !== 'number' || bps <= 0) return '';
  if (bps >= 1000000) {
    const mbps = bps / 1000000;
    return `${mbps >= 10 ? Math.round(mbps) : mbps.toFixed(1)} Mbps`;
  }
  const kbps = Math.round(bps / 1000);
  return `${kbps} kbps`;
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
  return variants.sort((a, b) => (b.height || 0) - (a.height || 0) || (b.bandwidth || 0) - (a.bandwidth || 0));
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
    const kind = isAudio ? 'audio' : 'video';

    // Parse each <Representation ... /> or <Representation ...> inside
    const repRegex = /<Representation\b([^>]*)/gi;
    let repMatch;

    while ((repMatch = repRegex.exec(chunk)) !== null) {
      const repAttrs = repMatch[1];

      const widthMatch = /\bwidth=["'](\d+)["']/i.exec(repAttrs);
      const heightMatch = /\bheight=["'](\d+)["']/i.exec(repAttrs);
      const bwMatch = /\bbandwidth=["'](\d+)["']/i.exec(repAttrs);
      const codecsMatch = /\bcodecs=["']([^"']+)["']/i.exec(repAttrs);

      const width = widthMatch ? Number.parseInt(widthMatch[1], 10) : null;
      const height = heightMatch ? Number.parseInt(heightMatch[1], 10) : null;
      const bandwidth = bwMatch ? Number.parseInt(bwMatch[1], 10) : null;
      const codecs = codecsMatch ? codecsMatch[1] : null;

      const variant = {
        kind,
        width: Number.isFinite(width) ? width : null,
        height: Number.isFinite(height) ? height : null,
        bandwidth: Number.isFinite(bandwidth) ? bandwidth : null,
        codecs,
        url: baseUrl,
        label: ''
      };
      variant.label = describeVariant(variant);

      variants.push(variant);
    }
  }

  // Sort video first, then highest height, then bandwidth
  return variants.sort((a, b) => {
    if (a.kind !== b.kind) return a.kind === 'video' ? -1 : 1;
    return (b.height || 0) - (a.height || 0) || (b.bandwidth || 0) - (a.bandwidth || 0);
  });
}

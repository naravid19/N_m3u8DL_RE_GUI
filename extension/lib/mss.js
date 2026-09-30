/**
 * Pure Smooth Streaming (MSS) manifest parser.
 * Extracts QualityLevels from <StreamIndex Type="video|audio">.
 */

import { describeVariant } from './manifest.js';

/**
 * Parses a Smooth Streaming Media XML text into a sorted list of Variant objects.
 */
export function parseMssManifest(text, baseUrl) {
  if (!text || typeof text !== 'string' || !/<SmoothStreamingMedia/i.test(text)) {
    return [];
  }

  const variants = [];
  const streamChunks = text.split(/<StreamIndex/i).slice(1);

  for (const chunk of streamChunks) {
    // Determine StreamIndex Type
    const typeMatch = /\bType=["']([^"']+)["']/i.exec(chunk);
    const type = (typeMatch ? typeMatch[1] : '').toLowerCase();
    const isAudio = type.includes('audio');
    const kind = isAudio ? 'audio' : 'video';

    // Parse each <QualityLevel ... /> inside
    const qlRegex = /<QualityLevel\b([^>]*)/gi;
    let qlMatch;

    while ((qlMatch = qlRegex.exec(chunk)) !== null) {
      const qlAttrs = qlMatch[1];

      const bwMatch = /\b(?:Bitrate|bitrate)=["'](\d+)["']/i.exec(qlAttrs);
      const widthMatch = /\b(?:MaxWidth|Width|maxWidth|width)=["'](\d+)["']/i.exec(qlAttrs);
      const heightMatch = /\b(?:MaxHeight|Height|maxHeight|height)=["'](\d+)["']/i.exec(qlAttrs);
      const fourccMatch = /\b(?:FourCC|fourCC|codecs)=["']([^"']+)["']/i.exec(qlAttrs);

      const bandwidth = bwMatch ? Number.parseInt(bwMatch[1], 10) : null;
      const width = widthMatch ? Number.parseInt(widthMatch[1], 10) : null;
      const height = heightMatch ? Number.parseInt(heightMatch[1], 10) : null;
      const codecs = fourccMatch ? fourccMatch[1] : null;

      const variant = {
        kind,
        width: Number.isFinite(width) ? width : null,
        height: Number.isFinite(height) ? height : null,
        bandwidth: Number.isFinite(bandwidth) ? bandwidth : null,
        codecs,
        url: null, // MSS uses manifest URL plus quality selector
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

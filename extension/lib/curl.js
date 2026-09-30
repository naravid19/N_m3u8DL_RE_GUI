import { shouldForward } from './header-policy.js';
import { resolveImpersonate } from './impersonate.js';

/**
 * Serializes a captured stream request into a bash-compatible cURL command string
 * ready for N_m3u8DL-RE GUI clipboard consumption.
 */
// A captured value may never break onto a line of its own: the GUI reads every
// "# nre-*" line as an instruction, so a newline would let a page add one.
const oneLine = (s) => String(s).replace(/[\r\n]+/g, ' ');

export function toCurl(stream, options = {}) {
  if (!stream || !stream.url) return '';

  const q = (s) => `'${oneLine(s).replace(/'/g, `'\\''`)}'`;
  const parts = [`curl ${q(stream.url)}`];

  if (Array.isArray(stream.headers) && stream.headers.length > 0) {
    for (const h of stream.headers) {
      if (h && h.name && shouldForward(h.name)) {
        parts.push(`-H ${q(`${h.name}: ${h.value ?? ''}`)}`);
      }
    }
  } else {
    if (stream.referer && shouldForward('Referer'))     parts.push(`-H ${q('Referer: ' + stream.referer)}`);
    if (stream.userAgent && shouldForward('User-Agent')) parts.push(`-H ${q('User-Agent: ' + stream.userAgent)}`);
    if (stream.cookie && shouldForward('Cookie'))       parts.push(`-H ${q('Cookie: ' + stream.cookie)}`);
    if (stream.origin && shouldForward('Origin'))       parts.push(`-H ${q('Origin: ' + stream.origin)}`);
  }

  let cmd = parts.join(' \\\n  ');

  if (stream.pageUrl) {
    cmd += `\n# nre-page-url: ${oneLine(stream.pageUrl)}`;
  }

  if (stream.userAgent) {
    const imp = resolveImpersonate(stream.userAgent);
    if (imp) {
      cmd += `\n# nre-impersonate: ${imp}`;
    }
  }

  if (stream.isCloudflare) {
    cmd += `\n# nre-cf: 1`;
  }

  if (options && options.selectVideo) {
    cmd += `\n# nre-select-video: ${oneLine(options.selectVideo)}`;
  }

  if (options && options.selectAudio) {
    cmd += `\n# nre-select-audio: ${oneLine(options.selectAudio)}`;
  }

  if (options && options.selectSubtitle) {
    cmd += `\n# nre-select-subtitle: ${oneLine(options.selectSubtitle)}`;
  }

  if (options && options.saveName) {
    cmd += `\n# nre-save-name: ${oneLine(options.saveName)}`;
  }

  if (options && options.warn) {
    cmd += `\n# nre-warn: ${oneLine(options.warn)}`;
  }

  return cmd;
}

function getOrigin(referer) {
  if (!referer || typeof referer !== 'string') return null;
  try {
    const u = new URL(referer);
    return u.origin;
  } catch {
    return referer.trim();
  }
}

/**
 * Checks whether the streams in a selection originate from differing Referer origins.
 * Returns mismatch state, the primary origin (first non-empty referer origin), and offCount.
 */
export function findRefererMismatch(streams) {
  if (!streams || !Array.isArray(streams) || streams.length === 0) {
    return { mismatched: false, primaryOrigin: null, offCount: 0 };
  }

  let primaryOrigin = null;
  let offCount = 0;

  for (const s of streams) {
    if (!s || !s.referer) continue;
    const origin = getOrigin(s.referer);
    if (!origin) continue;

    if (!primaryOrigin) {
      primaryOrigin = origin;
    } else if (origin !== primaryOrigin) {
      offCount++;
    }
  }

  return {
    mismatched: offCount > 0,
    primaryOrigin,
    offCount
  };
}

/**
 * Emits a list of URLs formatted for N_m3u8DL-RE GUI's batch downloader.
 */
export function toBatchList(streams) {
  if (!streams || !Array.isArray(streams) || streams.length === 0) return '';

  const lines = [];

  // Check for differing Referer origins
  const mismatch = findRefererMismatch(streams);
  if (mismatch.mismatched) {
    lines.push(`# nre-warn: Referer mismatch — ${mismatch.offCount} stream(s) differ from primary origin ${oneLine(mismatch.primaryOrigin)}`);
  }

  // Find first stream with any captured headers
  const firstWithHeaders = streams.find(
    (s) => s && (s.referer || s.userAgent || s.cookie || s.origin)
  );

  if (firstWithHeaders) {
    const headerParts = [];
    if (firstWithHeaders.referer) headerParts.push(`Referer: ${oneLine(firstWithHeaders.referer)}`);
    if (firstWithHeaders.userAgent) headerParts.push(`User-Agent: ${oneLine(firstWithHeaders.userAgent)}`);
    if (firstWithHeaders.cookie) headerParts.push(`Cookie: ${oneLine(firstWithHeaders.cookie)}`);
    if (firstWithHeaders.origin) headerParts.push(`Origin: ${oneLine(firstWithHeaders.origin)}`);

    if (headerParts.length > 0) {
      lines.push(`# nre-headers: ${headerParts.join('\\n')}`);
    }
  }

  for (const s of streams) {
    if (!s || !s.url) continue;
    if (s.title) {
      const cleanTitle = oneLine(s.title).replace(/,/g, ' ').trim();
      lines.push(`${cleanTitle},${oneLine(s.url)}`);
    } else {
      lines.push(oneLine(s.url));
    }
  }

  return lines.join('\n');
}

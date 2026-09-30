/**
 * Serializes a captured stream request into a bash-compatible cURL command string
 * ready for N_m3u8DL-RE GUI clipboard consumption.
 */
export function toCurl(stream, options = {}) {
  if (!stream || !stream.url) return '';

  const q = (s) => `'${String(s).replace(/'/g, `'\\''`)}'`;
  const parts = [`curl ${q(stream.url)}`];

  if (stream.referer)   parts.push(`-H ${q('Referer: ' + stream.referer)}`);
  if (stream.userAgent) parts.push(`-H ${q('User-Agent: ' + stream.userAgent)}`);
  if (stream.cookie)    parts.push(`-H ${q('Cookie: ' + stream.cookie)}`);
  if (stream.origin)    parts.push(`-H ${q('Origin: ' + stream.origin)}`);

  let cmd = parts.join(' \\\n  ');

  if (options && options.selectVideo) {
    cmd += `\n# nre-select-video: ${options.selectVideo}`;
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
    lines.push('# note: Selected streams originate from different Referers; using first');
  }

  // Find first stream with any captured headers
  const firstWithHeaders = streams.find(
    (s) => s && (s.referer || s.userAgent || s.cookie || s.origin)
  );

  if (firstWithHeaders) {
    const headerParts = [];
    if (firstWithHeaders.referer) headerParts.push(`Referer: ${firstWithHeaders.referer}`);
    if (firstWithHeaders.userAgent) headerParts.push(`User-Agent: ${firstWithHeaders.userAgent}`);
    if (firstWithHeaders.cookie) headerParts.push(`Cookie: ${firstWithHeaders.cookie}`);
    if (firstWithHeaders.origin) headerParts.push(`Origin: ${firstWithHeaders.origin}`);

    if (headerParts.length > 0) {
      lines.push(`# nre-headers: ${headerParts.join('\\n')}`);
    }
  }

  for (const s of streams) {
    if (!s || !s.url) continue;
    if (s.title) {
      const cleanTitle = s.title.replace(/,/g, ' ').trim();
      lines.push(`${cleanTitle},${s.url}`);
    } else {
      lines.push(s.url);
    }
  }

  return lines.join('\n');
}

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

/**
 * Emits a list of URLs formatted for N_m3u8DL-RE GUI's batch downloader.
 */
export function toBatchList(streams) {
  if (!streams || !Array.isArray(streams) || streams.length === 0) return '';

  const lines = [];
  const firstWithReferer = streams.find((s) => s && s.referer);
  if (firstWithReferer && firstWithReferer.referer) {
    lines.push(`# Referer: ${firstWithReferer.referer}`);
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

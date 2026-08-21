/**
 * Serializes a captured stream request into a bash-compatible cURL command string
 * ready for N_m3u8DL-RE GUI clipboard consumption.
 */
export function toCurl(stream) {
  if (!stream || !stream.url) return '';

  const q = (s) => `'${String(s).replace(/'/g, `'\\''`)}'`;
  const parts = [`curl ${q(stream.url)}`];

  if (stream.referer)   parts.push(`-H ${q('Referer: ' + stream.referer)}`);
  if (stream.userAgent) parts.push(`-H ${q('User-Agent: ' + stream.userAgent)}`);
  if (stream.cookie)    parts.push(`-H ${q('Cookie: ' + stream.cookie)}`);
  if (stream.origin)    parts.push(`-H ${q('Origin: ' + stream.origin)}`);

  return parts.join(' \\\n  ');
}

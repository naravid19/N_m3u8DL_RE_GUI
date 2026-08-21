import { test } from 'node:test';
import assert from 'node:assert/strict';
import { toCurl, toBatchList } from '../lib/curl.js';

const base = {
  url: 'https://cdn.example.com/hls/master.m3u8',
  referer: null, userAgent: null, cookie: null, origin: null
};

test('emits a bare command for a URL with no headers', () => {
  assert.equal(toCurl(base), "curl 'https://cdn.example.com/hls/master.m3u8'");
});

test('emits each header as its own -H flag', () => {
  const out = toCurl({ ...base, referer: 'https://site.example.com/', userAgent: 'Mozilla/5.0' });

  assert.match(out, /-H 'Referer: https:\/\/site\.example\.com\/'/);
  assert.match(out, /-H 'User-Agent: Mozilla\/5\.0'/);
});

test('omits headers that were never captured', () => {
  assert.doesNotMatch(toCurl(base), /Cookie|Referer|User-Agent|Origin/);
});

test('escapes an embedded single quote with the bash idiom', () => {
  // CurlCommandParserTests asserts the reading side of exactly this.
  const out = toCurl({ ...base, referer: "https://site.example.com/a'b" });

  assert.ok(out.includes("'\\''"));
});

test('uses backslash continuation so the command survives a paste', () => {
  const out = toCurl({ ...base, referer: 'https://site.example.com/', cookie: 'a=1' });

  assert.ok(out.includes(' \\\n  '));
});

test('a cookie containing a pipe is not mangled', () => {
  // The GUI splits headers on newline for exactly this reason.
  const out = toCurl({ ...base, cookie: 'sid=a|b|c' });

  assert.ok(out.includes("-H 'Cookie: sid=a|b|c'"));
});

test('a cookie containing a semicolon stays in one header', () => {
  const out = toCurl({ ...base, cookie: 'sid=abc; theme=dark' });

  assert.ok(out.includes("-H 'Cookie: sid=abc; theme=dark'"));
});

test('survives a URL carrying a query string', () => {
  const out = toCurl({ ...base, url: 'https://cdn.example.com/m.m3u8?token=a&b=c' });

  assert.ok(out.includes("'https://cdn.example.com/m.m3u8?token=a&b=c'"));
});

test('tolerates a missing stream object', () => {
  assert.equal(toCurl(null), '');
});

// --- Quality selection directives (Task 3) ---

test('appends a select-video directive when a quality was chosen', () => {
  const out = toCurl(base, { selectVideo: 'res="1080*"' });

  assert.ok(out.endsWith('\n# nre-select-video: res="1080*"'));
});

test('emits no directive line when no quality was chosen', () => {
  assert.ok(!toCurl(base).includes('# nre-'));
});

test('the directive line does not disturb the cURL command above it', () => {
  const out = toCurl(base, { selectVideo: 'best' });

  assert.ok(out.startsWith("curl 'https://cdn.example.com/hls/master.m3u8'"));
});

// --- Batch export list (Task 5) ---

test('toBatchList emits one URL per line', () => {
  const out = toBatchList([
    { url: 'https://cdn.example.com/a.m3u8' },
    { url: 'https://cdn.example.com/b.m3u8' }
  ]);

  assert.deepEqual(out.split('\n').filter((l) => l && !l.startsWith('#')), [
    'https://cdn.example.com/a.m3u8',
    'https://cdn.example.com/b.m3u8'
  ]);
});

test('toBatchList writes a title when one is known', () => {
  // BatchInputParser reads "[title],url".
  const out = toBatchList([{ url: 'https://cdn.example.com/a.m3u8', title: 'Episode 1' }]);

  assert.ok(out.includes('Episode 1,https://cdn.example.com/a.m3u8'));
});

test('toBatchList strips a comma from a title so the separator stays unambiguous', () => {
  const out = toBatchList([{ url: 'https://cdn.example.com/a.m3u8', title: 'Ep 1, part 2' }]);

  assert.ok(!out.split('\n').find((l) => l.startsWith('Ep 1,'))?.includes('part 2,'));
});

test('toBatchList records the shared headers as comments', () => {
  const out = toBatchList([{ url: 'https://x/a.m3u8', referer: 'https://site/' }]);

  // BatchInputParser skips '#' lines, so this is a note to the human only.
  assert.ok(out.startsWith('#'));
});

test('toBatchList returns empty for an empty selection', () => {
  assert.equal(toBatchList([]), '');
});

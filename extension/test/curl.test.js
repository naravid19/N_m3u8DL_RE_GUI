import { test } from 'node:test';
import assert from 'node:assert/strict';
import { toCurl, toBatchList, findRefererMismatch } from '../lib/curl.js';

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

test('appends a save-name directive when saveName is provided', () => {
  const out = toCurl(base, { saveName: 'My Awesome Video' });

  assert.ok(out.endsWith('\n# nre-save-name: My Awesome Video'));
});

test('appends both select-video and save-name directives when both are provided', () => {
  const out = toCurl(base, { selectVideo: 'res="1080*"', saveName: 'Episode 1' });

  assert.ok(out.includes('\n# nre-select-video: res="1080*"'));
  assert.ok(out.includes('\n# nre-save-name: Episode 1'));
});

test('emits wire format v3 directives for page-url, impersonate, and cf', () => {
  const out = toCurl({
    ...base,
    pageUrl: 'https://site.example.com/watch/123',
    userAgent: 'Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 Chrome/131.0.0.0 Safari/537.36',
    isCloudflare: true
  }, {
    selectAudio: 'for="eng"',
    selectSubtitle: 'lang="en"'
  });

  assert.ok(out.includes('\n# nre-page-url: https://site.example.com/watch/123'));
  assert.ok(out.includes('\n# nre-impersonate: chrome131'));
  assert.ok(out.includes('\n# nre-cf: 1'));
  assert.ok(out.includes('\n# nre-select-audio: for="eng"'));
  assert.ok(out.includes('\n# nre-select-subtitle: lang="en"'));
});

test('toCurl formats stream.headers array while filtering dropped transport headers', () => {
  const stream = {
    url: 'https://cdn.example.com/hls/master.m3u8',
    headers: [
      { name: 'Referer', value: 'https://site.example.com/' },
      { name: 'Authorization', value: 'Bearer token123' },
      { name: 'accept-encoding', value: 'gzip, deflate, br' },
      { name: 'range', value: 'bytes=0-1000' },
      { name: 'sec-fetch-mode', value: 'cors' }
    ]
  };

  const out = toCurl(stream);
  assert.match(out, /-H 'Referer: https:\/\/site\.example\.com\/'/);
  assert.match(out, /-H 'Authorization: Bearer token123'/);
  assert.doesNotMatch(out, /accept-encoding/);
  assert.doesNotMatch(out, /range/);
  assert.doesNotMatch(out, /sec-fetch/);
});

// --- Batch export list (Task 5) ---

test('a newline in any captured value cannot start a directive line of its own', () => {
  // The GUI reads every "# nre-*" line as an instruction, so a page URL, header or
  // title carrying a newline must not be able to add one.
  const evil = 'x\n# nre-save-name: evil';
  const out = toCurl(
    { ...base, pageUrl: evil, referer: evil, headers: [{ name: 'X-Test', value: evil }] },
    { saveName: evil, selectVideo: evil, warn: evil }
  );

  assert.equal(out.split('\n').filter((l) => l.startsWith('# nre-save-name')).length, 1);
});

test('toBatchList keeps every captured value on its own line', () => {
  const evil = 'x\n# nre-save-name: evil';
  const out = toBatchList([{ url: 'https://cdn.example.com/a.m3u8', title: evil, referer: evil, cookie: evil }]);

  assert.ok(!out.split('\n').some((l) => l.startsWith('# nre-save-name')));
});

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

test('toBatchList emits headers as a directive, not a bare comment', () => {
  // BatchInputParser skips '#' lines, so a plain comment reaches nothing.
  const out = toBatchList([{ url: 'https://x/a.m3u8', referer: 'https://site/', userAgent: 'UA/1' }]);

  assert.ok(out.includes('# nre-headers: Referer: https://site/'));
  assert.ok(out.includes('User-Agent: UA/1'));
});

test('toBatchList takes headers from the first entry that has them', () => {
  const out = toBatchList([
    { url: 'https://x/a.m3u8' },
    { url: 'https://x/b.m3u8', referer: 'https://site/' }
  ]);

  assert.ok(out.includes('# nre-headers: Referer: https://site/'));
});

test('toBatchList emits no directive when nothing was captured', () => {
  assert.ok(!toBatchList([{ url: 'https://x/a.m3u8' }]).includes('# nre-'));
});

test('toBatchList warns when entries disagree on Referer', () => {
  // One header set applies to the whole batch; say so rather than silently
  // applying one site's Referer to another site's URL.
  const out = toBatchList([
    { url: 'https://a/x.m3u8', referer: 'https://a/' },
    { url: 'https://b/y.m3u8', referer: 'https://b/' }
  ]);

  assert.ok(out.includes('# nre-warn: Referer mismatch'));
});

test('toBatchList returns empty for an empty selection', () => {
  assert.equal(toBatchList([]), '');
});

// --- Referer mismatch detection (Task 2) ---

test('no mismatch when every stream shares a referer', () => {
  const result = findRefererMismatch([
    { url: 'https://a/1.m3u8', referer: 'https://site.example.com/x' },
    { url: 'https://a/2.m3u8', referer: 'https://site.example.com/y' }
  ]);

  assert.equal(result.mismatched, false);
  assert.equal(result.offCount, 0);
});

test('compares origins, not full referer URLs', () => {
  // Two episodes on one site have different paths and the same origin.
  const result = findRefererMismatch([
    { url: 'https://a/1.m3u8', referer: 'https://site.example.com/ep/1' },
    { url: 'https://a/2.m3u8', referer: 'https://site.example.com/ep/2' }
  ]);

  assert.equal(result.mismatched, false);
});

test('counts how many streams fall outside the primary origin', () => {
  const result = findRefererMismatch([
    { url: 'https://a/1.m3u8', referer: 'https://a.example.com/' },
    { url: 'https://b/2.m3u8', referer: 'https://b.example.com/' },
    { url: 'https://c/3.m3u8', referer: 'https://c.example.com/' }
  ]);

  assert.equal(result.mismatched, true);
  assert.equal(result.primaryOrigin, 'https://a.example.com');
  assert.equal(result.offCount, 2);
});

test('streams with no referer are not counted as mismatched', () => {
  // Nothing to conflict with; they simply carry no header.
  const result = findRefererMismatch([
    { url: 'https://a/1.m3u8', referer: 'https://a.example.com/' },
    { url: 'https://a/2.m3u8', referer: null }
  ]);

  assert.equal(result.mismatched, false);
});

test('an unparseable referer does not throw', () => {
  const result = findRefererMismatch([
    { url: 'https://a/1.m3u8', referer: 'not a url' },
    { url: 'https://a/2.m3u8', referer: 'https://a.example.com/' }
  ]);

  assert.ok(typeof result.mismatched === 'boolean');
});

test('an empty selection reports no mismatch', () => {
  assert.equal(findRefererMismatch([]).mismatched, false);
});

test('toBatchList and the UI check agree', () => {
  // One rule, two consumers. The payload note existed while the interface
  // showed a green success toast for a batch the code knew would fail.
  const streams = [
    { url: 'https://a/1.m3u8', referer: 'https://a.example.com/' },
    { url: 'https://b/2.m3u8', referer: 'https://b.example.com/' }
  ];

  assert.equal(findRefererMismatch(streams).mismatched, true);
  assert.ok(toBatchList(streams).includes('# nre-warn: Referer mismatch'));
});

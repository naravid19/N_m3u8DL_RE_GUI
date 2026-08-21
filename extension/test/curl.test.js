import { test } from 'node:test';
import assert from 'node:assert/strict';
import { toCurl } from '../lib/curl.js';

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

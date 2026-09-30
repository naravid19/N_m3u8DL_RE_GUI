import { test } from 'node:test';
import assert from 'node:assert/strict';
import { selectParser, describeProbeFailure } from '../lib/probe-policy.js';

const HLS = '#EXTM3U\n#EXT-X-STREAM-INF:BANDWIDTH=1\na.m3u8\n';
const DASH = '<?xml version="1.0"?><MPD><Period/></MPD>';
const MSS = '<SmoothStreamingMedia MajorVersion="2"><StreamIndex Type="video"/></SmoothStreamingMedia>';

test('an explicit kind wins over the content', () => {
  assert.equal(selectParser('HLS', HLS), 'hls');
  assert.equal(selectParser('DASH', DASH), 'dash');
  assert.equal(selectParser('MSS', MSS), 'mss');
});

test('kind matching is case insensitive', () => {
  assert.equal(selectParser('hls', HLS), 'hls');
});

test('an unknown kind falls back to sniffing the body', () => {
  assert.equal(selectParser('Media', HLS), 'hls');
  assert.equal(selectParser(null, DASH), 'dash');
  assert.equal(selectParser(undefined, MSS), 'mss');
});

test('sniffing tolerates a leading BOM and whitespace', () => {
  assert.equal(selectParser(null, '\uFEFF\n  ' + HLS), 'hls');
});

test('content that is none of the three yields no parser', () => {
  assert.equal(selectParser(null, '<html><body>Not found</body></html>'), null);
  assert.equal(selectParser(null, ''), null);
});

// --- failure messages (R3) ---

test('no tab available points the user at reopening the page', () => {
  const message = describeProbeFailure({
    hadTab: false,
    pageResult: null,
    directResult: { ok: false, status: 403 }
  });

  assert.match(message, /open the page again/i);
});

test('a refusal from inside the open page does not tell them to open it', () => {
  // The request was injected into the page, so it is demonstrably open.
  const message = describeProbeFailure({
    hadTab: true,
    pageResult: { ok: false, status: 403 },
    directResult: { ok: false, status: 403 }
  });

  assert.doesNotMatch(message, /open the page again/i);
  assert.match(message, /refused|expired/i);
});

test('a timeout is reported as a timeout, not a status code', () => {
  const message = describeProbeFailure({
    hadTab: true,
    pageResult: { ok: false, status: 0, error: 'AbortError' },
    directResult: { ok: false, status: 0, error: 'Timeout' }
  });

  assert.match(message, /timed out/i);
});

test('an ordinary status is reported with its code', () => {
  const message = describeProbeFailure({
    hadTab: true,
    pageResult: { ok: false, status: 500 },
    directResult: { ok: false, status: 500 }
  });

  assert.match(message, /500/);
});

test('a message is always produced, even with nothing to go on', () => {
  const message = describeProbeFailure({ hadTab: false, pageResult: null, directResult: null });

  assert.ok(message.length > 0);
});

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

import { detectDrm } from '../lib/probe-policy.js';

test('detectDrm flags HLS key tags that name a DRM key format', () => {
  const widevine = '#EXTM3U\n#EXT-X-KEY:METHOD=SAMPLE-AES-CTR,URI="data:text/plain;base64,AA==",KEYFORMAT="urn:uuid:edef8ba9-79d6-4ace-a3c8-27dcd51d21ed"\n';
  const fairplay = '#EXTM3U\n#EXT-X-SESSION-KEY:METHOD=SAMPLE-AES,URI="skd://abc",KEYFORMAT="com.apple.streamingkeydelivery"\n';
  assert.equal(detectDrm(widevine), true);
  assert.equal(detectDrm(fairplay), true);
});

test('detectDrm leaves decryptable HLS encryption alone', () => {
  const aes = '#EXTM3U\n#EXT-X-KEY:METHOD=AES-128,URI="https://cdn.example.com/key.bin"\n';
  const sampleAes = '#EXTM3U\n#EXT-X-KEY:METHOD=SAMPLE-AES,URI="https://cdn.example.com/key.bin"\n';
  assert.equal(detectDrm(aes), false);
  assert.equal(detectDrm(sampleAes), false);
});

test('detectDrm ignores a signature outside HLS key tags', () => {
  assert.equal(detectDrm('#EXTM3U\n#EXTINF:6,skd://title\nseg.ts\n'), false);
});

test('detectDrm flags DASH and MSS protection by system ID', () => {
  const mpd = '<MPD><ContentProtection schemeIdUri="urn:uuid:EDEF8BA9-79D6-4ACE-A3C8-27DCD51D21ED"/></MPD>';
  const ism = '<SmoothStreamingMedia><Protection><ProtectionHeader SystemID="9A04F079-9840-4286-AB92-E65BE0885F95"/></Protection></SmoothStreamingMedia>';
  assert.equal(detectDrm(mpd), true);
  assert.equal(detectDrm(ism), true);
  assert.equal(detectDrm('<MPD><ContentProtection schemeIdUri="urn:mpeg:dash:mp4protection:2011"/></MPD>'), false);
});

test('detectDrm is false for empty input', () => {
  assert.equal(detectDrm(''), false);
  assert.equal(detectDrm(null), false);
});

test('detectDrm treats the dots in the FairPlay and PlayReady key formats literally', () => {
  const lookalike = '#EXTM3U\n#EXT-X-KEY:METHOD=SAMPLE-AES,URI="https://cdn.example.com/k",KEYFORMAT="comXappleXstreamingkeydelivery"\n';
  assert.equal(detectDrm(lookalike), false);
});

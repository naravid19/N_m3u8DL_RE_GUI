import { test } from 'node:test';
import assert from 'node:assert/strict';
import { formatBytes, formatRelativeTime, elideUrl, describeRole, describeStream, formatBitrate, formatDuration, splitUrl } from '../lib/format.js';

test('formatBytes scales through the units', () => {
  assert.equal(formatBytes(0), '0 B');
  assert.equal(formatBytes(512), '512 B');
  assert.equal(formatBytes(1024), '1.0 KB');
  assert.equal(formatBytes(1536), '1.5 KB');
  assert.equal(formatBytes(1048576), '1.0 MB');
  assert.equal(formatBytes(1073741824), '1.0 GB');
});

test('formatBytes returns empty for absent or nonsensical values', () => {
  assert.equal(formatBytes(null), '');
  assert.equal(formatBytes(undefined), '');
  assert.equal(formatBytes(-1), '');
  assert.equal(formatBytes(Number.NaN), '');
});

test('formatBytes marks a partial figure as approximate', () => {
  assert.equal(formatBytes(5242880, true), '~5.0 MB');
});

test('formatBytes leaves a complete figure unmarked', () => {
  assert.equal(formatBytes(5242880, false), '5.0 MB');
});

test('formatBytes defaults to unmarked when partiality is unknown', () => {
  assert.equal(formatBytes(5242880), '5.0 MB');
});

test('elideUrl keeps the head and tail so the filename stays readable', () => {
  const url = 'https://cdn.example.com/a/very/long/path/that/keeps/going/master.m3u8';
  const short = elideUrl(url, 40);

  assert.ok(short.length <= 40);
  assert.ok(short.startsWith('https://cdn.example.com'));
  assert.ok(short.endsWith('master.m3u8'));
  assert.ok(short.includes('…'));
});

test('elideUrl leaves a short URL alone', () => {
  assert.equal(elideUrl('https://a.co/b.m3u8', 40), 'https://a.co/b.m3u8');
});

test('a manifest is described as carrying every quality', () => {
  assert.equal(describeRole({ kind: 'HLS' }), 'Video + audio · every quality');
  assert.equal(describeRole({ kind: 'DASH' }), 'Video + audio · every quality');
  assert.equal(describeRole({ kind: 'MSS' }), 'Video + audio · every quality');
});

test('a progressive file is described as one fixed quality', () => {
  assert.equal(describeRole({ kind: 'Media' }), 'Video only · one quality');
});

test('an audio stream says so plainly', () => {
  assert.equal(describeRole({ kind: 'Audio' }), 'Audio only');
});

test('a low-confidence result leads with the doubt', () => {
  // The amber "guess" badge says nothing about what to do. This does.
  assert.equal(describeRole({ kind: 'HLS', confidence: 'low' }), 'Might not be a video');
});

test('an Abyss player page is described as a page, not a stream', () => {
  assert.equal(describeRole({ kind: 'Abyss' }), 'Player page · full video');
});

test('an unknown kind falls back rather than throwing', () => {
  assert.equal(describeRole({ kind: 'Mystery' }), 'Stream');
  assert.equal(describeRole(null), '');
});

test('describeStream leads with the role, not the acronym', () => {
  // The kind is already the loudest element on the card; repeating it two
  // centimetres below spends the one line that could teach something.
  assert.equal(
    describeStream({ kind: 'HLS', confidence: 'high', sizeBytes: null }),
    'Video + audio · every quality'
  );
});

test('describeStream still appends a known size', () => {
  assert.equal(
    describeStream({ kind: 'Media', confidence: 'high', sizeBytes: 1073741824 }),
    'Video only · one quality · 1.0 GB'
  );
});

test('describeStream keeps the approximate marker on a partial size', () => {
  assert.equal(
    describeStream({ kind: 'Media', confidence: 'high', sizeBytes: 5242880, isPartial: true }),
    'Video only · one quality · ~5.0 MB'
  );
});

test('formatBitrate formats bps into Mbps and kbps', () => {
  assert.equal(formatBitrate(5000000), '5.0 Mbps');
  assert.equal(formatBitrate(12000000), '12 Mbps');
  assert.equal(formatBitrate(128000), '128 kbps');
  assert.equal(formatBitrate(0), '');
  assert.equal(formatBitrate(null), '');
});

test('formatDuration formats seconds into mm:ss and hh:mm:ss', () => {
  assert.equal(formatDuration(65), '1:05');
  assert.equal(formatDuration(3665), '1:01:05');
  assert.equal(formatDuration(0), '');
  assert.equal(formatDuration(null), '');
});

test('splitUrl cleanly separates filename, queryParams, and hostAndPath', () => {
  const res = splitUrl('https://example.com:8443/video/chunk.m3u8?nimblesessionid=203966360');
  assert.equal(res.filename, 'chunk.m3u8');
  assert.equal(res.queryParams, '?nimblesessionid=203966360');
  assert.equal(res.hostAndPath, 'https://example.com:8443/video/chunk.m3u8');
});

test('splitUrl handles URLs without query parameters', () => {
  const res = splitUrl('https://example.com/media/stream.mpd');
  assert.equal(res.filename, 'stream.mpd');
  assert.equal(res.queryParams, '');
  assert.equal(res.hostAndPath, 'https://example.com/media/stream.mpd');
});

test('splitUrl tolerates invalid URLs gracefully', () => {
  const res = splitUrl('invalid-url-string');
  assert.equal(res.filename, 'invalid-url-string');
  assert.equal(res.queryParams, '');
  assert.equal(res.hostAndPath, '');
});



import { test } from 'node:test';
import assert from 'node:assert/strict';
import { formatBytes, formatRelativeTime, elideUrl, describeStream } from '../lib/format.js';

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

test('describeStream leads with the kind', () => {
  assert.equal(
    describeStream({ kind: 'HLS', confidence: 'high', sizeBytes: null }),
    'HLS'
  );
});

test('describeStream appends a size when one is known', () => {
  assert.equal(
    describeStream({ kind: 'Media', confidence: 'high', sizeBytes: 1073741824 }),
    'Media · 1.0 GB'
  );
});

test('describeStream marks a low-confidence guess', () => {
  assert.equal(
    describeStream({ kind: 'HLS', confidence: 'low', sizeBytes: null }),
    'HLS · guess'
  );
});

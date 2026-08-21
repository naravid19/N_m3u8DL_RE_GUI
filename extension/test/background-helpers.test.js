import { test } from 'node:test';
import assert from 'node:assert/strict';
import { totalSizeFrom } from '../lib/format.js';

test('totalSizeFrom prefers the total in Content-Range', () => {
  // 206 responses report only the range length in content-length.
  assert.deepEqual(
    totalSizeFrom('5242880', 'bytes 0-5242879/1258291200', 206),
    { sizeBytes: 1258291200, isPartial: false }
  );
});

test('totalSizeFrom falls back to the range length when the total is unknown', () => {
  assert.deepEqual(
    totalSizeFrom('5242880', 'bytes 0-5242879/*', 206),
    { sizeBytes: 5242880, isPartial: true }
  );
});

test('totalSizeFrom marks a 206 with no Content-Range as partial', () => {
  assert.deepEqual(
    totalSizeFrom('5242880', null, 206),
    { sizeBytes: 5242880, isPartial: true }
  );
});

test('totalSizeFrom trusts content-length on a 200', () => {
  assert.deepEqual(
    totalSizeFrom('1258291200', null, 200),
    { sizeBytes: 1258291200, isPartial: false }
  );
});

test('totalSizeFrom handles a missing content-length', () => {
  assert.deepEqual(totalSizeFrom(null, null, 200), { sizeBytes: null, isPartial: false });
});

test('totalSizeFrom rejects a non-numeric content-length', () => {
  assert.deepEqual(totalSizeFrom('abc', null, 200), { sizeBytes: null, isPartial: false });
});

test('totalSizeFrom accepts a zero-length response', () => {
  assert.deepEqual(totalSizeFrom('0', null, 200), { sizeBytes: 0, isPartial: false });
});

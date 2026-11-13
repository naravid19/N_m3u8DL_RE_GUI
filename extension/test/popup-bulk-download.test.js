import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';

const popup = readFileSync(new URL('../popup/popup.js', import.meta.url), 'utf8');
const html = readFileSync(new URL('../popup/popup.html', import.meta.url), 'utf8');

function bulkDownloadSource() {
  const start = popup.indexOf("const btnBulkDownload");
  assert.notEqual(start, -1, 'bulk Download handler is missing');
  const end = popup.indexOf('\n  // Filter input handler', start);
  assert.notEqual(end, -1, 'bulk Download handler end marker is missing');
  return popup.slice(start, end);
}

test('the multi-select toolbar exposes direct bulk download', () => {
  assert.ok(html.includes('id="btn-bulk-download"'));
  assert.ok(html.includes('Download selected'));
});

test('each selected stream is sent with its own request headers', () => {
  const source = bulkDownloadSource();
  assert.match(source, /for \(let i = 0; i < selectedStreams\.length; i\+\+\)/);
  assert.match(source, /sendToGui\(await buildCurlFor\(item\)\)/);
  assert.doesNotMatch(source, /toBatchList/);
});

test('bulk download is single-flight and preserves failures for retry', () => {
  const source = bulkDownloadSource();
  assert.match(source, /if \(bulkDownloadBusy\) return/);
  assert.match(source, /sentUrls\.push\(item\.url\)/);
  assert.match(source, /for \(const url of sentUrls\) selectedUrls\.delete\(url\)/);
  assert.match(source, /renderStreams\(\)/);
});

test('a missing native host is reported without attempting every selected stream', () => {
  const source = bulkDownloadSource();
  assert.match(source, /guiAvailable = false/);
  assert.match(source, /await syncGuiBanner\(\)/);
  assert.match(source, /break;/);
});

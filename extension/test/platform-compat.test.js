import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync, readdirSync } from 'node:fs';
import { join } from 'node:path';
import { fileURLToPath } from 'node:url';

const extensionDir = fileURLToPath(new URL('..', import.meta.url));
const manifest = JSON.parse(readFileSync(new URL('../manifest.json', import.meta.url), 'utf8'));

/** Every .js the extension actually ships, excluding tests. */
function shippedScripts(dir = extensionDir, found = []) {
  for (const entry of readdirSync(dir, { withFileTypes: true })) {
    if (entry.name === 'node_modules' || entry.name === 'test') continue;
    const full = join(dir, entry.name);
    if (entry.isDirectory()) shippedScripts(full, found);
    else if (entry.name.endsWith('.js')) found.push(full);
  }
  return found;
}

test('the manifest declares the browser floor the code actually needs', () => {
  // Without this, Chrome installs the extension on a version that cannot parse
  // it and the whole popup dies at module-link time -- silently, with the
  // static placeholder left on screen.
  assert.ok(manifest.minimum_chrome_version, 'manifest.json must declare minimum_chrome_version');
  assert.ok(
    Number.parseInt(manifest.minimum_chrome_version, 10) >= 111,
    'color-mix() in popup.css needs Chrome 111 or newer'
  );
});

test('no shipped script uses import attributes', () => {
  // `with { type: 'json' }` is Chrome 123+. It raised the floor by 12 versions
  // for one JSON file that a plain ES module holds just as well, and nothing
  // in the manifest recorded the cost.
  const offenders = shippedScripts()
    .filter((file) => {
      const codeWithoutComments = readFileSync(file, 'utf8').replace(/\/\*[\s\S]*?\*\/|\/\/.*/g, '');
      return /\b(?:with|assert)\s*\{\s*type\s*:/.test(codeWithoutComments);
    })
    .map((file) => file.slice(extensionDir.length));

  assert.deepEqual(offenders, [], `import attributes raise the Chrome floor to 123: ${offenders.join(', ')}`);
});

test('incognito runs split so captured cookies do not cross back', () => {
  // MV3 defaults to "spanning": one instance and one chrome.storage.session
  // for both profiles. This extension stores captured Cookie headers, so a
  // stream sniffed in incognito would otherwise appear -- credentials and all
  // -- in the normal window's All Recent list.
  assert.equal(manifest.incognito, 'split');
});

test('manifest configures deep-detect.js in MAIN world for in-page interception', () => {
  assert.ok(Array.isArray(manifest.content_scripts), 'content_scripts must be an array');
  const mainScript = manifest.content_scripts.find((cs) => Array.isArray(cs.js) && cs.js.includes('deep-detect.js'));
  assert.ok(mainScript, 'deep-detect.js must be configured in content_scripts');
  assert.equal(mainScript.world, 'MAIN', 'deep-detect.js must execute in MAIN world');
});

test('deep-detect.js includes double-injection guard against SPA reinjection', () => {
  const deepDetectSrc = readFileSync(new URL('../deep-detect.js', import.meta.url), 'utf8');
  assert.ok(deepDetectSrc.includes('__NRE_DEEP_DETECTOR__'), 'deep-detect.js must check __NRE_DEEP_DETECTOR__');
});

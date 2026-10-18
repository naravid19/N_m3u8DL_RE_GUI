import { readFileSync, writeFileSync, readdirSync, mkdirSync, copyFileSync, rmSync, existsSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { join, resolve } from 'node:path';
import process from 'node:process';

export const FIREFOX_EXTENSION_ID = 'nre-stream-bridge@naravid19';

/**
 * Pure function to derive a Firefox Manifest V3 from Chrome manifest.
 * @param {object} chromeManifest
 * @returns {object}
 */
export function toFirefoxManifest(chromeManifest) {
  const result = structuredClone(chromeManifest);
  delete result.key;
  delete result.incognito;
  delete result.minimum_chrome_version;

  const serviceWorker = chromeManifest.background?.service_worker;
  result.background = {
    scripts: [serviceWorker],
    type: 'module'
  };

  result.browser_specific_settings = {
    gecko: {
      id: FIREFOX_EXTENSION_ID,
      strict_min_version: '128.0',
      data_collection_permissions: { required: ['none'] }
    }
  };

  return result;
}

/**
 * Build Firefox extension distribution in dist-firefox/
 * @param {string} [extensionDir]
 */
export function buildFirefoxDist(extensionDir = fileURLToPath(new URL('..', import.meta.url))) {
  const distDir = join(extensionDir, 'dist-firefox');
  if (existsSync(distDir)) {
    rmSync(distDir, { recursive: true, force: true });
  }
  mkdirSync(distDir, { recursive: true });

  const skipped = new Set([
    'test',
    'scripts',
    'node_modules',
    'dist-firefox',
    '.impeccable',
    'package.json',
    'package-lock.json'
  ]);

  function copyDir(src, dest) {
    const entries = readdirSync(src, { withFileTypes: true });
    for (const entry of entries) {
      if (src === extensionDir && skipped.has(entry.name)) continue;
      const srcPath = join(src, entry.name);
      const destPath = join(dest, entry.name);
      if (entry.isDirectory()) {
        mkdirSync(destPath, { recursive: true });
        copyDir(srcPath, destPath);
      } else {
        copyFileSync(srcPath, destPath);
      }
    }
  }

  copyDir(extensionDir, distDir);

  const chromeManifest = JSON.parse(readFileSync(join(extensionDir, 'manifest.json'), 'utf8'));
  const firefoxManifest = toFirefoxManifest(chromeManifest);
  writeFileSync(join(distDir, 'manifest.json'), JSON.stringify(firefoxManifest, null, 2) + '\n', 'utf8');
}

if (process.argv[1] && resolve(process.argv[1]) === resolve(fileURLToPath(import.meta.url))) {
  buildFirefoxDist();
}

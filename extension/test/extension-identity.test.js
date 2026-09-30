import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { createHash } from 'node:crypto';

/**
 * An unpacked extension's ID is derived from its install path unless the
 * manifest pins a public key, which would make a hardcoded allowed_origins
 * entry in the native host manifest correct only on the author's machine.
 * These tests keep the key present and keep the ID it produces equal to the
 * one the .NET side writes into allowed_origins.
 */
const EXPECTED_ID = 'ecdmbdclbopgpbbhdelncjhbnapnblgd';

function deriveExtensionId(base64Key) {
  // Chrome: SHA-256 the DER SubjectPublicKeyInfo, take the first 16 bytes,
  // and map each hex nibble 0-f onto a-p.
  const der = Buffer.from(base64Key, 'base64');
  const digest = createHash('sha256').update(der).digest('hex').slice(0, 32);
  return [...digest].map((c) => String.fromCharCode(97 + parseInt(c, 16))).join('');
}

test('manifest pins a public key so the extension ID is stable', () => {
  const manifest = JSON.parse(readFileSync(new URL('../manifest.json', import.meta.url), 'utf8'));
  assert.ok(manifest.key, 'manifest.json must carry a "key" field');
  assert.equal(deriveExtensionId(manifest.key), EXPECTED_ID);
});

test('manifest requests the nativeMessaging permission', () => {
  const manifest = JSON.parse(readFileSync(new URL('../manifest.json', import.meta.url), 'utf8'));
  assert.ok(manifest.permissions.includes('nativeMessaging'));
});

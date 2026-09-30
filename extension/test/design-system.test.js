import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';

const css = readFileSync(new URL('../popup/popup.css', import.meta.url), 'utf8');
const html = readFileSync(new URL('../popup/popup.html', import.meta.url), 'utf8');
const js = readFileSync(new URL('../popup/popup.js', import.meta.url), 'utf8');

/**
 * The :root block owns the literals; everything after it must use the names.
 *
 * Slices at the closing brace in column 0 rather than the first brace of any
 * kind, so a nested block inside :root cannot silently move the cut point and
 * leave half the stylesheet unchecked.
 */
function bodyAfterTokens(source) {
  const rootStart = source.indexOf(':root');
  const blockEnd = source.indexOf('\n}', rootStart);
  assert.ok(rootStart !== -1 && blockEnd !== -1, 'popup.css should open with a :root token block');

  // Comments are stripped first: they are prose, and prose explains values.
  // A comment reading "12px above the actions against 8px elsewhere" parsed
  // as a declaration and failed the spacing rule, and any comment naming a
  // hex would have failed the colour rule the same way.
  return source.slice(blockEnd + 2).replace(/\/\*[\s\S]*?\*\//g, '');
}

test('no colour literal escapes the token block', () => {
  const stray = [...bodyAfterTokens(css).matchAll(/#[0-9A-Fa-f]{3,8}\b/g)].map((m) => m[0]);
  assert.deepEqual(stray, [], `colours must come from tokens, found: ${stray.join(', ')}`);
});

test('no colour function smuggles a literal past the hex check', () => {
  // The hex rule alone missed 23 rgba() literals encoding nine hues, five of
  // which were never tokenised at all — an entire second palette the guard
  // reported clean. Alpha now comes from color-mix() over a named token.
  const stray = [...bodyAfterTokens(css).matchAll(/\b(?:rgba?|hsla?)\(/g)].map((m) => m[0]);
  assert.deepEqual(stray, [], `use color-mix() over a token instead of: ${stray.join(', ')}`);
});

test('spacing comes from the scale', () => {
  // Matches to a semicolon OR a closing brace: a declaration that is last in
  // its block carries no trailing semicolon, and requiring one let `gap: 5px }`
  // through while the test still reported green.
  // 0 and 1px are not spacing decisions; hairlines and resets stay literal.
  const offScale = [...bodyAfterTokens(css).matchAll(/(?:padding|margin|gap)[^:}]*:\s*([^;}]+)[;}]/g)]
    .flatMap((m) => m[1].match(/\b\d+(?:\.\d+)?px\b/g) ?? [])
    .filter((v) => v !== '0px' && v !== '1px');

  assert.deepEqual(offScale, [], `spacing must use var(--space-*), found: ${offScale.join(', ')}`);
});

test('no emoji is left doing an icon\'s job', () => {
  const emoji = /[\u{1F000}-\u{1FAFF}\u{2190}-\u{21FF}\u{2300}-\u{27BF}\u{2B00}-\u{2BFF}]/u;
  assert.doesNotMatch(html, emoji, 'popup.html should use data-icon, not emoji');
  assert.doesNotMatch(js, emoji, 'popup.js should use icon(), not emoji');
});

test('every token the stylesheet references is defined', () => {
  // A typo in a var() name fails silently in CSS — the property is simply
  // dropped and the element renders unstyled, which no other test would catch.
  const defined = new Set([...css.matchAll(/^\s*(--[a-z0-9-]+):/gm)].map((m) => m[1]));
  const used = new Set([...css.matchAll(/var\(\s*(--[a-z0-9-]+)/g)].map((m) => m[1]));
  const missing = [...used].filter((name) => !defined.has(name));

  assert.deepEqual(missing, [], `referenced but never defined: ${missing.join(', ')}`);
});

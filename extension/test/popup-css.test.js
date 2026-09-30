import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { join } from 'node:path';

const css = readFileSync(join(import.meta.dirname, '..', 'popup', 'popup.css'), 'utf8');
const html = readFileSync(join(import.meta.dirname, '..', 'popup', 'popup.html'), 'utf8');

const MIN_FONT_PX = 11;

function literalFontSizes(source) {
  return [...source.matchAll(/font-size:\s*([0-9.]+)px/g)].map((m) => Number(m[1]));
}

test('no font size is below the readable floor', () => {
  const tooSmall = literalFontSizes(css).filter((px) => px < MIN_FONT_PX);

  assert.deepEqual(tooSmall, [], `font sizes below ${MIN_FONT_PX}px: ${tooSmall.join(', ')}`);
});

test('font sizes come from the token scale, not literals', () => {
  // A literal here means someone reached past the scale. Add a token instead.
  const literals = literalFontSizes(css);

  assert.deepEqual(literals, [], `literal font-size values outside :root — ${literals.join(', ')}`);
});

test('the type scale defines exactly four steps', () => {
  const steps = [...css.matchAll(/--text-(xs|sm|md|lg):/g)].map((m) => m[1]);

  assert.deepEqual(steps.sort(), ['lg', 'md', 'sm', 'xs']);
});

test('the popup declares its colour scheme', () => {
  // Without this the browser paints checkboxes and inputs in light chrome.
  assert.match(css, /color-scheme:\s*dark/);
});

test('reduced motion is honoured', () => {
  assert.match(css, /@media\s*\(prefers-reduced-motion:\s*reduce\)/);
});

test('reduced motion neutralises duration rather than removing it', () => {
  // animation: none would stop transitionend/animationend handlers firing.
  const block = /@media\s*\(prefers-reduced-motion:\s*reduce\)\s*\{([\s\S]*?)\n\}/.exec(css);

  assert.ok(block, 'no reduced-motion block found');
  assert.match(block[1], /transition-duration:\s*0\.01ms/);
  assert.doesNotMatch(block[1], /transition:\s*none/);
});

test('the body does not scroll — only the list does', () => {
  const body = /body\s*\{([\s\S]*?)\}/.exec(css);

  assert.ok(body, 'no body rule found');
  assert.doesNotMatch(body[1], /overflow-y:\s*auto/);
  assert.match(css, /\.stream-list\s*\{[\s\S]*?overflow-y:\s*auto/);
});

test('the flex regions can shrink below their content', () => {
  // Omitting min-height: 0 is the classic way this layout silently fails.
  assert.match(css, /#main-content\s*\{[\s\S]*?min-height:\s*0/);
  assert.match(css, /\.stream-list\s*\{[\s\S]*?min-height:\s*0/);
});

test('no layout values are inlined in the markup', () => {
  const inline = [...html.matchAll(/style="[^"]*"/g)].map((m) => m[0]);

  assert.deepEqual(inline, [], `inline styles in popup.html: ${inline.join(' | ')}`);
});

test('a mono font token exists', () => {
  assert.match(css, /--font-mono:/);
});

test('filenames are set in the mono stack', () => {
  // The row-to-row discriminator is a bitrate inside the filename; a
  // proportional face never aligns the differing digits.
  const rule = /\.url-filename\s*\{([\s\S]*?)\}/.exec(css);

  assert.ok(rule, 'no .url-filename rule found');
  assert.match(rule[1], /font-family:\s*var\(--font-mono\)/);
});

test('filenames use tabular figures', () => {
  const rule = /\.url-filename\s*\{([\s\S]*?)\}/.exec(css);

  assert.match(rule[1], /font-variant-numeric:\s*tabular-nums/);
});

test('filenames break at boundaries before breaking mid-token', () => {
  // break-all shatters a name even where a boundary break would have fitted.
  const rule = /\.url-filename\s*\{([\s\S]*?)\}/.exec(css);

  assert.doesNotMatch(rule[1], /word-break:\s*break-all/);
  assert.match(rule[1], /overflow-wrap:\s*anywhere/);
});

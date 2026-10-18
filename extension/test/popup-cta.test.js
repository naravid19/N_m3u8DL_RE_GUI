import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';

const popup = readFileSync(new URL('../popup/popup.js', import.meta.url), 'utf8');
const html = readFileSync(new URL('../popup/popup.html', import.meta.url), 'utf8');
const css = readFileSync(new URL('../popup/popup.css', import.meta.url), 'utf8');

/**
 * popup.js needs a live DOM and the chrome.* APIs, and this extension ships with no
 * test DOM (package.json declares no dependencies), so these read the source the way
 * platform-compat.test.js and popup-css.test.js already do for code they cannot run.
 */

/** Source of one top-level function, from its signature to the closing brace in column 0. */
function functionSource(name) {
  const start = popup.indexOf(`function ${name}(`);
  assert.notEqual(start, -1, `${name}() is gone from popup.js`);
  const end = popup.indexOf('\n}', start);
  return popup.slice(start, end + 2);
}

test('the Primary CTA promotion is gated on a node the offline branch actually attaches', () => {
  // guiAvailable is null on every fresh popup open -- the popup document is recreated
  // each time -- so every card takes the offline-baseline branch. That branch calls
  // applyGuiAvailability(false), which appends only copyCurlBtn; downloadBtn is never
  // put in the DOM. Gating the later native-host result on downloadBtn.isConnected
  // therefore tests a node that is false by construction, and Download silently stays
  // demoted to "Copy as cURL" even while the GUI is running -- which is the whole
  // point of the dynamic-CTA feature.
  assert.doesNotMatch(
    popup,
    /downloadBtn\.isConnected/,
    'downloadBtn is not attached in the offline baseline branch, so .isConnected is always false there'
  );
});

test('the native-host probe still promotes the card when the GUI is reachable', () => {
  // Guards the fix from being "solved" by deleting the promotion outright.
  const promotion = popup.match(/checkGuiAvailable\(\)\.then\([\s\S]{0,900}?\}\);/);
  assert.ok(promotion, 'the deferred native-host probe is gone');
  assert.match(promotion[0], /applyGuiAvailability\(true\)/);
  assert.match(promotion[0], /\bcard\.isConnected\b/);
});

test('the desktop-app notice follows the native-host probe and can be dismissed', () => {
  // "unavailable" means the host was never registered, i.e. the GUI has not been
  // run once -- not that the GUI is closed. The notice must say "run once", must
  // only show when the probe says offline, and must stay dismissed afterwards.
  assert.match(html, /id="gui-banner"[^>]*\bhidden\b/, 'the notice starts hidden until the probe answers');
  assert.ok(html.includes('Run <strong>N_m3u8DL_RE_GUI.exe</strong> once'));
  assert.ok(html.includes('href="https://github.com/naravid19/N_m3u8DL_RE_GUI/releases/latest"'));

  const sync = functionSource('syncGuiBanner');
  assert.ok(sync.includes('guiAvailable !== false'));
  assert.ok(sync.includes('isGuiHintDismissed()'));
  assert.ok(popup.includes('dismissGuiHint()'));
});

test('the notice sits under the header, as designed, and the toast floats instead of resizing the footer', () => {
  const [beforeFooter, footer] = html.split('<footer');
  assert.ok(beforeFooter.includes('id="gui-banner"'));
  assert.ok(!footer.includes('id="gui-banner"'));
  assert.ok(beforeFooter.indexOf('</header>') < beforeFooter.indexOf('id="gui-banner"'));
  assert.match(css, /\.toast\s*\{[\s\S]*?position:\s*absolute/);
});

test('the scan line keeps moving under reduced motion', () => {
  const block = /@media\s*\(prefers-reduced-motion:\s*reduce\)\s*\{([\s\S]*?)\n\}/.exec(css);
  assert.match(block[1], /\.empty-scan i\s*\{[^}]*animation-iteration-count:\s*infinite/);
});

test('closing the notice sticks, and a found app clears the snooze', () => {
  const close = popup.match(/gui-banner-close'\)\.addEventListener\('click'[\s\S]*?\r?\n  \}\);/)[0];
  assert.ok(close.includes('guiHintClosedHere = true'), 'kept in memory in case storage fails');
  assert.ok(close.includes('await dismissGuiHint()'));
  assert.ok(functionSource('syncGuiBanner').includes('guiHintClosedHere'));
  assert.ok(popup.includes('if (available) clearGuiHintDismissal()'));
});

test('the missing-app toast appears once per popup open', () => {
  assert.ok(popup.includes('if (!missingToastShown)'));
});

test('"Check again" re-probes the host instead of making the user reopen the popup', () => {
  assert.ok(html.includes('id="gui-banner-recheck"'));

  const recheck = functionSource('recheckGui');
  // The probe is memoised per popup open; without clearing it, a second click
  // would just replay the first answer.
  assert.ok(recheck.includes('guiAvailablePromise = null'));
  assert.ok(recheck.includes('checkGuiAvailable()'));
  assert.ok(recheck.includes('renderStreams()'), 'a found host must promote Download on every card');
});

test('a failed Download is a neutral notice that cannot leak into later toasts', () => {
  assert.ok(popup.includes("tone: 'notice'"));
  // Reset on every call, not only when set: otherwise one notice greys every later toast.
  assert.ok(popup.includes("toast.classList.toggle('is-notice', options.tone === 'notice')"));
});

test('no copy claims the GUI "is not running" -- Chrome starts the host itself', () => {
  assert.doesNotMatch(popup, /not running/);
});

test('a successful Download gives visual feedback and debounce before reset', () => {
  assert.ok(popup.includes("setButtonLabel(downloadBtn, 'check', 'Sent to App')"));
  assert.ok(popup.includes("downloadBtn.classList.add('is-copied')"));
  assert.ok(popup.includes("downloadBtn.classList.remove('is-copied')"));
});


test('the native-host probe starts on popup open, so the header pill settles with no streams', () => {
  // Cards were the only caller, so an empty popup left the pill on "Checking app".
  assert.match(functionSource('init'), /checkGuiAvailable\(\)/);
});

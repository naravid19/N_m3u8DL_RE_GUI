import assert from 'node:assert/strict';

/**
 * The body of a named function in a source file, brace-matched. For tests that read
 * source because the file itself needs a DOM or the chrome.* runtime to run.
 */
export function functionBody(source, name) {
  const start = source.indexOf(`function ${name}(`);
  assert.ok(start >= 0, `${name} not found -- was it renamed?`);
  const open = source.indexOf(') {', start) + 2; // skip braces in the parameter list
  let depth = 0;
  for (let i = open; i < source.length; i++) {
    if (source[i] === '{') depth++;
    else if (source[i] === '}' && --depth === 0) return source.slice(open, i + 1);
  }
  throw new Error(`unbalanced braces in ${name}`);
}

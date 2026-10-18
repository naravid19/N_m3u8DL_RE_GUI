import { test } from 'node:test';
import assert from 'node:assert/strict';
import { addListenerWithExtraHeaders } from '../lib/web-request.js';

test('registers with extraHeaders when the event accepts it', () => {
  let recordedSpec = null;
  const fakeEvent = {
    addListener(_cb, _filter, extraInfoSpec) {
      recordedSpec = extraInfoSpec;
    }
  };

  const callback = () => {};
  const filter = { urls: ['<all_urls>'] };

  addListenerWithExtraHeaders(fakeEvent, callback, filter, ['requestHeaders']);
  assert.deepEqual(recordedSpec, ['requestHeaders', 'extraHeaders']);
});

test('falls back without extraHeaders when the event throws on it', () => {
  let recordedSpec = null;
  const fakeEvent = {
    addListener(_cb, _filter, extraInfoSpec) {
      if (extraInfoSpec.includes('extraHeaders')) {
        throw new TypeError("Invalid value for extraInfoSpec: 'extraHeaders'");
      }
      recordedSpec = extraInfoSpec;
    }
  };

  const callback = () => {};
  const filter = { urls: ['<all_urls>'] };

  assert.doesNotThrow(() => {
    addListenerWithExtraHeaders(fakeEvent, callback, filter, ['requestHeaders']);
  });
  assert.deepEqual(recordedSpec, ['requestHeaders']);
});

test('rethrows when the fallback also fails', () => {
  const fakeEvent = {
    addListener() {
      throw new Error('boom');
    }
  };

  assert.throws(
    () => {
      addListenerWithExtraHeaders(fakeEvent, () => {}, {}, ['requestHeaders']);
    },
    /boom/
  );
});

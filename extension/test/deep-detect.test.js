import test from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
import vm from 'node:vm';

const DEEP_DETECT_SRC = fs.readFileSync(new URL('../deep-detect.js', import.meta.url), 'utf8');

function createMockEnvironment() {
  const messages = [];
  const postMessages = [];

  const mockWindow = {
    location: {
      href: 'https://streaming.example.com/watch/12345',
      origin: 'https://streaming.example.com',
      protocol: 'https:'
    },
    postMessage: (msg, origin) => {
      postMessages.push(msg);
      if (msg && msg.source === 'NRE_DEEP_DETECTOR' && msg.payload) {
        messages.push(msg.payload);
      }
    }
  };

  const mockChrome = {
    runtime: {
      sendMessage: (msg) => {
        messages.push(msg);
      }
    }
  };

  class MockXHR {
    constructor() {
      this.listeners = {};
      this.readyState = 0;
      this.status = 0;
      this.responseType = '';
      this.response = null;
    }
    addEventListener(event, fn) {
      this.listeners[event] = this.listeners[event] || [];
      this.listeners[event].push(fn);
    }
    open(method, url) {
      this.method = method;
      this.url = url;
    }
    send(body) {
      // Simulate load
    }
    triggerLoad(status, responseType, response) {
      this.status = status;
      this.responseType = responseType;
      this.response = response;
      const fns = this.listeners['load'] || [];
      for (const fn of fns) fn.call(this);
    }
  }

  const mockBlobUrls = [];
  class MockBlob {
    constructor(parts, opts) {
      this.parts = parts;
      this.opts = opts;
    }
  }

  const mockURL = {
    createObjectURL: (blob) => {
      const url = `blob:https://streaming.example.com/${mockBlobUrls.length}`;
      mockBlobUrls.push({ url, blob });
      return url;
    }
  };

  const sandbox = {
    window: mockWindow,
    location: mockWindow.location,
    chrome: mockChrome,
    JSON: { parse: JSON.parse, stringify: JSON.stringify },
    XMLHttpRequest: MockXHR,
    Blob: MockBlob,
    URL: mockURL,
    ArrayBuffer: ArrayBuffer,
    Uint8Array: Uint8Array,
    Set: Set,
    Array: Array,
    Number: Number,
    Object: Object
  };

  return { sandbox, messages, postMessages, mockBlobUrls, MockXHR };
}

test('deep-detect hooks JSON.parse and discovers embedded manifest URLs', () => {
  const { sandbox, messages, postMessages } = createMockEnvironment();

  vm.runInNewContext(DEEP_DETECT_SRC, sandbox);

  const payload = {
    code: 200,
    data: {
      title: 'Episode 1',
      streams: {
        hls: 'https://cdn.example.com/hls/master.m3u8',
        dash: 'https://cdn.example.com/dash/manifest.mpd'
      }
    }
  };

  sandbox.JSON.parse(JSON.stringify(payload));

  const manifestMessages = messages.filter((m) => m.type === 'DEEP_MANIFEST_DETECTED');
  assert.equal(manifestMessages.length, 2);
  assert.equal(manifestMessages[0].url, 'https://cdn.example.com/hls/master.m3u8');
  assert.equal(manifestMessages[0].ext, 'm3u8');
  assert.equal(manifestMessages[1].url, 'https://cdn.example.com/dash/manifest.mpd');
  assert.equal(manifestMessages[1].ext, 'mpd');

  // Also verify window.postMessage relay payload
  const relayMessages = postMessages.filter((m) => m.source === 'NRE_DEEP_DETECTOR' && m.payload.type === 'DEEP_MANIFEST_DETECTED');
  assert.equal(relayMessages.length, 2);
});

test('an inline #EXTM3U playlist is not reported as a blob URL', () => {
  // background.js runs every detection through classify(), which rejects any
  // scheme that is not http/https -- so a blob: URL was discarded 100% of the
  // time while still leaking the Blob and the whole playlist text. Carrying
  // inline manifests to the GUI needs the native bridge, not a blob URL.
  const { sandbox, messages, mockBlobUrls } = createMockEnvironment();

  vm.runInNewContext(DEEP_DETECT_SRC, sandbox);

  const payload = {
    playlist: '#EXTM3U\n#EXT-X-VERSION:3\n#EXTINF:10.0,\nsegment1.ts'
  };

  sandbox.JSON.parse(JSON.stringify(payload));

  assert.equal(messages.filter((m) => m.type === 'DEEP_MANIFEST_DETECTED').length, 0);
  assert.equal(mockBlobUrls.length, 0, 'no Blob URL should be created');
});

test('ordinary sixteen-number arrays are not reported as anything', () => {
  // A 16-bucket histogram, sixteen small ids, flattened RGBA. Nothing in the
  // detector treats a number array as a signal any more.
  const { sandbox, messages } = createMockEnvironment();

  vm.runInNewContext(DEEP_DETECT_SRC, sandbox);

  const payload = {
    histogram: [4, 9, 12, 40, 33, 21, 8, 2, 0, 1, 5, 17, 29, 31, 14, 6]
  };

  sandbox.JSON.parse(JSON.stringify(payload));

  assert.equal(messages.length, 0);
});

test('the page XMLHttpRequest is left untouched', () => {
  // The only XHR consumer was key sniffing. Nothing consumed the keys, so the
  // hook was page-visible cost with no product value and has been removed.
  const { sandbox, MockXHR } = createMockEnvironment();
  const originalOpen = MockXHR.prototype.open;

  vm.runInNewContext(DEEP_DETECT_SRC, sandbox);

  assert.equal(MockXHR.prototype.open, originalOpen);
  const xhr = new MockXHR();
  xhr.open('GET', 'https://cdn.example/key.bin');
  xhr.triggerLoad(200, 'arraybuffer', new ArrayBuffer(16));
  assert.equal(xhr.listeners.load, undefined);
});

test('deep-detect installs only once preventing double-hooking', () => {
  const { sandbox } = createMockEnvironment();

  vm.runInNewContext(DEEP_DETECT_SRC, sandbox);
  assert.equal(sandbox.window.__NRE_DEEP_DETECTOR__, true);

  const originalParse = sandbox.JSON.parse;
  // Run a second time
  vm.runInNewContext(DEEP_DETECT_SRC, sandbox);
  assert.equal(sandbox.JSON.parse, originalParse);
});

test('a huge array does not get walked in full', () => {
  // Asserted behaviourally, not on wall-clock: walking 100k strings takes
  // ~30ms in Node even with no budget at all, so a timing threshold passes
  // whether or not the budget exists. The budget's whole point is that the
  // scan STOPS, so the observable proof is that a manifest sitting past the
  // cut-off is never reported.
  const { sandbox, messages } = createMockEnvironment();
  vm.runInNewContext(DEEP_DETECT_SRC, sandbox);

  const wide = Array.from({ length: 100000 }, (_, i) => `https://cdn.example/not-a-manifest-${i}.txt`);
  wide.push('https://cdn.example/past-the-budget.m3u8');

  sandbox.JSON.parse(JSON.stringify({ wide }));

  assert.deepEqual(
    messages.filter((m) => m.type === 'DEEP_MANIFEST_DETECTED').map((m) => m.url),
    [],
    'the scan must stop at its node budget, leaving anything past it undetected'
  );
});

test('a manifest within the node budget is still detected', () => {
  // The other half of the trade: stopping early must not mean stopping at
  // zero. Without this, a budget of 0 would satisfy the test above.
  const { sandbox, messages } = createMockEnvironment();
  vm.runInNewContext(DEEP_DETECT_SRC, sandbox);

  const narrow = Array.from({ length: 10 }, (_, i) => `https://cdn.example/not-a-manifest-${i}.txt`);
  narrow.push('https://cdn.example/within-budget.m3u8');

  sandbox.JSON.parse(JSON.stringify({ narrow }));

  assert.deepEqual(
    messages.filter((m) => m.type === 'DEEP_MANIFEST_DETECTED').map((m) => m.url),
    ['https://cdn.example/within-budget.m3u8']
  );
});

test('the seen-url cache does not grow without bound', () => {
  const { sandbox } = createMockEnvironment();
  vm.runInNewContext(DEEP_DETECT_SRC, sandbox);

  for (let i = 0; i < 5000; i++) {
    sandbox.JSON.parse(JSON.stringify({ u: `https://cdn.example/v${i}.m3u8` }));
  }

  assert.ok(sandbox.window.__NRE_DEEP_DETECTOR_STATE__, 'state hook must exist');
  assert.ok(sandbox.window.__NRE_DEEP_DETECTOR_STATE__.seenSize() <= 512, `seen cache grew to ${sandbox.window.__NRE_DEEP_DETECTOR_STATE__?.seenSize()}`);
});

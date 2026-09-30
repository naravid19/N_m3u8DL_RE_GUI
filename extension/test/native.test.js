import { test } from 'node:test';
import assert from 'node:assert/strict';
import { sendToGui, isGuiAvailable, NATIVE_HOST } from '../lib/native.js';

function fakeRuntime({ response, lastError, throws } = {}) {
  return {
    lastError,
    sendNativeMessage(host, message, callback) {
      if (throws) throw new Error('no such host');
      this.lastHost = host;
      this.lastMessage = message;
      callback(response);
    }
  };
}

test('sends the payload to the pinned host name', async () => {
  const runtime = fakeRuntime({ response: { ok: true } });
  await sendToGui('curl https://x/y.m3u8', runtime);

  assert.equal(runtime.lastHost, NATIVE_HOST);
  assert.equal(runtime.lastMessage.type, 'capture');
  assert.equal(runtime.lastMessage.payload, 'curl https://x/y.m3u8');
});

test('reports success when the host acknowledges', async () => {
  const result = await sendToGui('payload', fakeRuntime({ response: { ok: true } }));
  assert.deepEqual(result, { ok: true });
});

test('a missing host is unavailable, not an error', async () => {
  // The GUI has never been run, so nothing registered the host. The popup
  // must fall back to Copy as cURL rather than showing a failure.
  const result = await sendToGui('payload', fakeRuntime({ lastError: { message: 'not found' } }));
  assert.equal(result.ok, false);
  assert.equal(result.reason, 'unavailable');
});

test('a throwing sendNativeMessage is unavailable too', async () => {
  const result = await sendToGui('payload', fakeRuntime({ throws: true }));
  assert.equal(result.ok, false);
  assert.equal(result.reason, 'unavailable');
});

test('surfaces an error the host reports', async () => {
  const result = await sendToGui('payload', fakeRuntime({ response: { ok: false, error: 'empty payload' } }));
  assert.equal(result.ok, false);
  assert.equal(result.reason, 'empty payload');
});

test('a response-less host is rejected rather than treated as success', async () => {
  const result = await sendToGui('payload', fakeRuntime({ response: undefined }));
  assert.equal(result.ok, false);
});

test('isGuiAvailable is true only when the host answers', async () => {
  assert.equal(await isGuiAvailable(fakeRuntime({ response: { ok: true } })), true);
  assert.equal(await isGuiAvailable(fakeRuntime({ lastError: { message: 'x' } })), false);
});

test('the availability probe carries no payload to apply', async () => {
  // Probing must not queue a download as a side effect.
  const runtime = fakeRuntime({ response: { ok: true } });
  await isGuiAvailable(runtime);

  assert.equal(runtime.lastMessage.type, 'ping');
  assert.equal(runtime.lastMessage.payload, undefined);
});

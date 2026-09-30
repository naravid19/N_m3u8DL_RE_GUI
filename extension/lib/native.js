/**
 * The only module that talks to the desktop GUI.
 *
 * The payload is exactly the text `📋 Copy as cURL` puts on the clipboard, so
 * the GUI parses it with the same code either way and there is no second wire
 * format to keep in sync.
 */

export const NATIVE_HOST = 'com.nm3u8dlre.gui';

function send(message, runtime) {
  return new Promise((resolve) => {
    try {
      runtime.sendNativeMessage(NATIVE_HOST, message, (response) => {
        // A host that was never registered — the usual case before the GUI
        // has been run once — surfaces here, not as a thrown error.
        if (runtime.lastError) {
          resolve({ ok: false, reason: 'unavailable' });
          return;
        }
        if (response && response.ok) {
          resolve({ ok: true });
          return;
        }
        resolve({ ok: false, reason: (response && response.error) || 'rejected' });
      });
    } catch {
      resolve({ ok: false, reason: 'unavailable' });
    }
  });
}

/** Hands one captured stream to the GUI, which queues and downloads it. */
export function sendToGui(payload, runtime = chrome.runtime) {
  return send({ type: 'capture', payload }, runtime);
}

/**
 * Whether the GUI is installed and registered. Sends no payload, so probing
 * can never queue a download by accident.
 */
export async function isGuiAvailable(runtime = chrome.runtime) {
  const result = await send({ type: 'ping' }, runtime);
  return result.ok;
}

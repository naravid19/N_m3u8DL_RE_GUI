/** Minimal in-memory stand-in for chrome runtime, storage, and local extension resources. No mocking library. */
export function installFakeChrome() {
  let store = {};
  const resources = new Map();
  const origFetch = globalThis.fetch;

  const area = {
    get: async (keys) => {
      if (keys === null || keys === undefined) return { ...store };
      const wanted = Array.isArray(keys) ? keys : [keys];
      const out = {};
      for (const key of wanted) {
        if (key in store) out[key] = store[key];
      }
      return out;
    },
    set: async (patch) => {
      store = { ...store, ...patch };
    },
    remove: async (keys) => {
      for (const key of Array.isArray(keys) ? keys : [keys]) delete store[key];
    }
  };

  const runtime = {
    getURL: (path) => `chrome-extension://fakeid/${String(path).replace(/^\//, '')}`,
    getManifest: () => ({ version: '1.3.0' })
  };

  globalThis.chrome = {
    storage: { session: area, local: area },
    runtime
  };

  globalThis.fetch = async (input, init) => {
    const urlStr = typeof input === 'string' ? input : input?.url || '';
    if (urlStr.startsWith('chrome-extension://fakeid/')) {
      const path = urlStr.replace('chrome-extension://fakeid/', '');
      if (resources.has(path)) {
        const content = resources.get(path);
        if (content === null) {
          return { ok: false, status: 404, text: async () => '' };
        }
        return { ok: true, status: 200, text: async () => content };
      }
      return { ok: false, status: 404, text: async () => '' };
    }
    if (typeof origFetch === 'function') {
      return origFetch(input, init);
    }
    throw new Error(`Unhandled fetch: ${urlStr}`);
  };

  return {
    area,
    reset: () => {
      store = {};
      resources.clear();
    },
    snapshot: () => ({ ...store }),
    setResourceText: (path, text) => {
      resources.set(String(path).replace(/^\//, ''), text);
    },
    setResourceMissing: (path) => {
      resources.set(String(path).replace(/^\//, ''), null);
    }
  };
}

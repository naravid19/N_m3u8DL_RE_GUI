/** Minimal in-memory stand-in for a chrome.storage area. No mocking library. */
export function installFakeChrome() {
  let store = {};

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

  globalThis.chrome = { storage: { session: area, local: area } };

  return {
    area,
    reset: () => { store = {}; },
    snapshot: () => ({ ...store })
  };
}

/**
 * The only module that touches chrome.storage.
 *
 * Uses storage.session, not storage.local: session is memory-backed and
 * survives service-worker restarts, which is the only durability this data
 * needs. Captured requests carry Cookie headers, and storage.local would
 * persist them unencrypted on disk across browser restarts indefinitely.
 */

const RECENT_KEY = 'recent_streams';
const VARIANTS_CACHE_PREFIX = 'variants_';
const DISMISSED_PREFIX = 'dismissed_';
const FAILURE_TTL_MS = 30 * 1000; // 30 seconds for failed probes
const SUCCESS_TTL_MS = 60 * 60 * 1000; // 1 hour for successful probes
const MAX_PER_TAB = 25;
const MAX_RECENT = 30;
const MAX_DISMISSED_PER_TAB = 200;
/**
 * A dismissal suppresses a URL for this long, not forever.
 *
 * The trash button dismisses everything it clears, so that a page still
 * fetching cannot refill the list milliseconds later. Storing that with no
 * expiry made the cure permanent: clearTabView never removes a dismissed key
 * (only clearTab does, on tab close or an origin change), so the stream the
 * user cleared could never come back for that tab -- reloading the page did
 * not help, because the URL and the tab id were unchanged.
 */
// Thirty seconds, as both clear specs promise: playback may continue that long
// with the cleared list staying empty.
const DISMISS_TTL_MS = 30 * 1000;

/**
 * The still-suppressed URLs from a stored dismissed list.
 *
 * Entries written before this had a TTL are bare strings with no timestamp.
 * They are treated as expired, which is both the safe default and what
 * releases anyone currently stuck behind a permanent dismissal.
 */
function activeDismissedUrls(raw, now = Date.now()) {
  const active = new Set();
  for (const entry of raw || []) {
    if (entry && typeof entry === 'object' && typeof entry.u === 'string') {
      if (now - (entry.at || 0) <= DISMISS_TTL_MS) active.add(entry.u);
    }
  }
  return active;
}

const tabKeyFor = (tabId) => `tab_${tabId}`;
const dismissedKeyFor = (tabId) => (tabId && tabId > 0 ? `${DISMISSED_PREFIX}${tabId}` : `${DISMISSED_PREFIX}none`);

/** Kinds that are a download target in their own right. A tab holding one of
 *  these is watching a stream, so anything media-shaped alongside it is one of
 *  that stream's segments — not a separate video. Abyss is excluded: it is a
 *  player page, which says nothing about what else on the tab is a segment. */
const MANIFEST_KINDS = new Set(['HLS', 'DASH', 'MSS']);

const isManifest = (item) => item && MANIFEST_KINDS.has(item.kind);

// Every mutation runs through this chain, so concurrent detections cannot
// read-modify-write over each other. A rejection must not poison the chain.
let writeChain = Promise.resolve();

function serialize(task) {
  const result = writeChain.then(task, task);
  writeChain = result.then(
    () => undefined,
    () => undefined
  );
  return result;
}

async function safeStorageSet(patch) {
  if (!patch || Object.keys(patch).length === 0) return;
  try {
    await chrome.storage.session.set(patch);
  } catch (err) {
    console.warn('chrome.storage.session.set failed:', err);
  }
}

/**
 * Returns the set of dismissed URLs for a tab (or for orphan/no-tab streams when null).
 */
export async function getDismissed(tabId) {
  const key = dismissedKeyFor(tabId);
  const data = await chrome.storage.session.get([key]);
  return activeDismissedUrls(data[key]);
}

/**
 * Dismisses a list of items against their source tab IDs (or dismissed_none if tabId is null).
 * Also removes the dismissed streams from the corresponding tab list and recent list.
 */
export function dismissMany(items) {
  if (!Array.isArray(items) || items.length === 0) {
    return Promise.resolve();
  }
  return serialize(async () => {
    const byTab = new Map();
    for (const item of items) {
      if (!item || !item.url) continue;
      const tid = item.tabId && item.tabId > 0 ? item.tabId : null;
      if (!byTab.has(tid)) byTab.set(tid, []);
      byTab.get(tid).push(item.url);
    }

    const allKeys = [RECENT_KEY];
    for (const tid of byTab.keys()) {
      allKeys.push(dismissedKeyFor(tid));
      if (tid) allKeys.push(tabKeyFor(tid));
    }

    const data = await chrome.storage.session.get(allKeys);
    const patch = {};
    let currentRecent = data[RECENT_KEY] || [];

    for (const [tid, urls] of byTab) {
      const dKey = dismissedKeyFor(tid);
      const now = Date.now();
      const urlSet = new Set();
      const byUrl = new Map();
      for (const entry of data[dKey] || []) {
        if (entry && typeof entry === 'object' && typeof entry.u === 'string') {
          if (now - (entry.at || 0) <= DISMISS_TTL_MS) byUrl.set(entry.u, entry);
        }
      }
      for (const u of urls) {
        if (u) {
          byUrl.set(u, { u, at: now });
          urlSet.add(u);
        }
      }
      let array = Array.from(byUrl.values());
      if (array.length > MAX_DISMISSED_PER_TAB) {
        array = array.slice(-MAX_DISMISSED_PER_TAB);
      }
      patch[dKey] = array;

      if (tid) {
        const tKey = tabKeyFor(tid);
        const currentTabList = data[tKey] || [];
        patch[tKey] = currentTabList.filter((s) => !urlSet.has(s.url));
      }

      currentRecent = currentRecent.filter((s) => {
        if (tid) {
          return !(s.tabId === tid && urlSet.has(s.url));
        }
        return !((!s.tabId || s.tabId <= 0) && urlSet.has(s.url));
      });
    }

    patch[RECENT_KEY] = currentRecent;
    await safeStorageSet(patch);
  });
}

/**
 * Dismisses a set of URLs for a specific tab so they do not immediately
 * reappear on subsequent requests while playback continues.
 */
export function dismissStreams(tabId, urls) {
  return dismissMany((urls || []).map((u) => ({ url: u, tabId })));
}

/**
 * Undismisses a list of items by removing them from the respective dismissed sets.
 */
export function undismissMany(items) {
  if (!Array.isArray(items) || items.length === 0) {
    return Promise.resolve();
  }
  return serialize(async () => {
    const byTab = new Map();
    for (const item of items) {
      if (!item || !item.url) continue;
      const tid = item.tabId && item.tabId > 0 ? item.tabId : null;
      if (!byTab.has(tid)) byTab.set(tid, []);
      byTab.get(tid).push(item.url);
    }

    const allKeys = Array.from(byTab.keys()).map((tid) => dismissedKeyFor(tid));
    const data = await chrome.storage.session.get(allKeys);
    const patch = {};

    for (const [tid, urls] of byTab) {
      const dKey = dismissedKeyFor(tid);
      const current = data[dKey] || [];
      const urlSet = new Set(urls);
      patch[dKey] = current.filter((entry) => {
        const url = entry && typeof entry === 'object' ? entry.u : entry;
        return !urlSet.has(url);
      });
    }

    if (Object.keys(patch).length > 0) {
      await safeStorageSet(patch);
    }
  });
}

/**
 * Records a stream against its tab and in the global recent list.
 * Returns the new length of the tab's list, for the badge.
 */
export function addStream(tabId, item) {
  return serialize(async () => {
    const effectiveTabId = tabId && tabId > 0 ? tabId : null;
    const dismissedKey = dismissedKeyFor(effectiveTabId);
    const keys = effectiveTabId
      ? [tabKeyFor(effectiveTabId), RECENT_KEY, dismissedKey]
      : [RECENT_KEY, dismissedKey];

    // Scoped read: pulling the whole area back on every detection is O(all tabs).
    const data = await chrome.storage.session.get(keys);
    const patch = {};
    let tabCount = 0;

    const dismissed = activeDismissedUrls(data[dismissedKey]);
    if (dismissed.has(item.url)) {
      if (effectiveTabId) {
        const currentList = data[tabKeyFor(effectiveTabId)] || [];
        return currentList.length;
      }
      return 0;
    }

    if (effectiveTabId) {
      const key = tabKeyFor(effectiveTabId);
      let list = data[key] || [];

      const incomingIsManifest = isManifest(item);
      const listHasManifest = list.some(isManifest);

      if (!incomingIsManifest && listHasManifest) {
        // A manifest is already the download target for this tab; this is one
        // of its segments. Dropping it is what keeps the manifest visible.
        return list.length;
      }

      if (incomingIsManifest && !listHasManifest) {
        // First manifest for the tab — evict segments captured before it.
        list = list.filter((s) => s.kind !== 'Media' && s.kind !== 'Audio');
      }

      if (!list.some((s) => s.url === item.url)) {
        list.unshift(item);
        if (list.length > MAX_PER_TAB) list.length = MAX_PER_TAB;
      }

      patch[key] = list;
      tabCount = list.length;
    }

    let recent = data[RECENT_KEY] || [];
    const incomingIsManifest = isManifest(item);

    // M3: When a manifest arrives on a tab, purge earlier media segments from that tab in recent_streams
    if (incomingIsManifest && effectiveTabId) {
      recent = recent.filter((s) => !(s.tabId === effectiveTabId && (s.kind === 'Media' || s.kind === 'Audio')));
    }

    if (!recent.some((s) => s.url === item.url)) {
      recent.unshift(item);
      if (recent.length > MAX_RECENT) recent.length = MAX_RECENT;
    }

    patch[RECENT_KEY] = recent;

    if (Object.keys(patch).length > 0) {
      await safeStorageSet(patch);
    }

    return tabCount;
  });
}

export async function getTabStreams(tabId) {
  if (!tabId || tabId <= 0) return [];
  const key = tabKeyFor(tabId);
  const data = await chrome.storage.session.get([key]);
  return data[key] || [];
}

export async function getRecentStreams() {
  const data = await chrome.storage.session.get([RECENT_KEY]);
  return data[RECENT_KEY] || [];
}

export function clearTab(tabId) {
  if (!tabId || tabId <= 0) return Promise.resolve();
  return serialize(() => chrome.storage.session.remove([tabKeyFor(tabId), dismissedKeyFor(tabId)]));
}

/**
 * Clears streams for a specific tab view and prunes only that tab's streams from recent_streams.
 */
export function clearTabView(tabId) {
  if (!tabId || tabId <= 0) return Promise.resolve();
  return serialize(async () => {
    const tabKey = tabKeyFor(tabId);
    const data = await chrome.storage.session.get([RECENT_KEY]);
    const currentRecent = data[RECENT_KEY] || [];
    const filteredRecent = currentRecent.filter((s) => s.tabId !== tabId);

    await chrome.storage.session.remove([tabKey]);
    await safeStorageSet({ [RECENT_KEY]: filteredRecent });
  });
}

/**
 * Retrieves cached probe variants for a manifest URL, respecting TTL.
 */
export async function getCachedVariants(url, now = Date.now()) {
  if (!url) return null;
  const key = `${VARIANTS_CACHE_PREFIX}${url}`;
  const data = await chrome.storage.session.get([key]);
  const entry = data[key];
  if (!entry) return null;

  const ttl = entry.error ? FAILURE_TTL_MS : SUCCESS_TTL_MS;
  if (now - (entry.timestamp || 0) > ttl) {
    return null;
  }

  return { variants: entry.variants || [], error: entry.error || null };
}

/**
 * Caches probe variants for a manifest URL in session storage.
 */
export function setCachedVariants(url, result, now = Date.now()) {
  if (!url || !result) return Promise.resolve();
  const key = `${VARIANTS_CACHE_PREFIX}${url}`;
  const entry = {
    variants: result.variants || [],
    error: result.error || null,
    timestamp: now
  };
  return serialize(async () => {
    await safeStorageSet({ [key]: entry });
  });
}

/**
 * Clears all tab streams, recent streams list, and variant caches in a serialized transaction.
 * Deliberately preserves dismissed sets so still-playing tabs do not refill.
 */
export function clearAll() {
  return serialize(async () => {
    const all = await chrome.storage.session.get(null);
    const keys = Object.keys(all).filter(
      (k) =>
        k.startsWith('tab_') ||
        k.startsWith(VARIANTS_CACHE_PREFIX) ||
        k === RECENT_KEY
    );
    if (keys.length > 0) {
      await chrome.storage.session.remove(keys);
    }
  });
}

/**
 * Drops per-tab lists and dismissed sets whose tab no longer exists.
 * onRemoved only fires while the service worker is awake, so tabs closed
 * during an idle period leak keys.
 * Called from the popup, which is the one moment the full key list matters.
 */
export function sweepOrphanTabs(liveTabIds) {
  return serialize(async () => {
    const liveTabKeys = new Set(liveTabIds.map((id) => tabKeyFor(id)));
    const liveDismissedKeys = new Set(liveTabIds.map((id) => dismissedKeyFor(id)));
    const all = await chrome.storage.session.get(null);
    const stale = Object.keys(all).filter(
      (key) =>
        (key.startsWith('tab_') && !liveTabKeys.has(key)) ||
        (key.startsWith(DISMISSED_PREFIX) && key !== `${DISMISSED_PREFIX}none` && !liveDismissedKeys.has(key))
    );
    if (stale.length > 0) {
      await chrome.storage.session.remove(stale);
    }
    return stale.length;
  });
}

const UPDATE_CHECK_CACHE_KEY = 'suite_update_cache';
const UPDATE_SUCCESS_TTL_MS = 24 * 60 * 60 * 1000; // 24 hours
const UPDATE_FAILURE_TTL_MS = 5 * 60 * 1000; // 5 minutes

/**
 * Gets cached suite update result from chrome.storage.local if still within TTL.
 * Uses chrome.storage.local (survives browser restarts) because update check results
 * contain no private user/cookie data — only a public version string and URL.
 */
export async function getCachedUpdateResult(now = Date.now()) {
  try {
    const data = await chrome.storage.local.get([UPDATE_CHECK_CACHE_KEY]);
    const entry = data[UPDATE_CHECK_CACHE_KEY];
    if (!entry || typeof entry !== 'object') return null;

    const timestamp = entry.timestamp || 0;
    const ttl = (entry.status === 'check-failed' || entry.status === 'unknown-version')
      ? UPDATE_FAILURE_TTL_MS
      : UPDATE_SUCCESS_TTL_MS;

    if (now - timestamp > ttl) {
      return null;
    }

    return entry.result || null;
  } catch {
    return null;
  }
}

/**
 * Sets cached suite update result in chrome.storage.local.
 */
export async function setCachedUpdateResult(result, now = Date.now()) {
  if (!result) return;
  try {
    const entry = {
      status: result.status,
      result,
      timestamp: now
    };
    await chrome.storage.local.set({ [UPDATE_CHECK_CACHE_KEY]: entry });
  } catch (err) {
    console.debug('Failed to cache update result', err);
  }
}

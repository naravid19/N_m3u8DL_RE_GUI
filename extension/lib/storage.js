/**
 * The only module that touches chrome.storage.
 *
 * Uses storage.session, not storage.local: session is memory-backed and
 * survives service-worker restarts, which is the only durability this data
 * needs. Captured requests carry Cookie headers, and storage.local would
 * persist them unencrypted on disk across browser restarts indefinitely.
 */

const RECENT_KEY = 'recent_streams';
const MAX_PER_TAB = 25;
const MAX_RECENT = 30;

const tabKeyFor = (tabId) => `tab_${tabId}`;

/** Kinds that are a download target in their own right. A tab holding one of
 *  these is watching a stream, so anything media-shaped alongside it is one of
 *  that stream's segments — not a separate video. Abyss is excluded: it is a
 *  player page, which says nothing about what else on the tab is a segment. */
const MANIFEST_KINDS = new Set(['HLS', 'DASH', 'MSS']);

const isManifest = (item) => MANIFEST_KINDS.has(item.kind);

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

/**
 * Records a stream against its tab and in the global recent list.
 * Returns the new length of the tab's list, for the badge.
 */
export function addStream(tabId, item) {
  return serialize(async () => {
    const effectiveTabId = tabId && tabId > 0 ? tabId : null;
    const keys = effectiveTabId ? [tabKeyFor(effectiveTabId), RECENT_KEY] : [RECENT_KEY];

    // Scoped read: pulling the whole area back on every detection is O(all tabs).
    const data = await chrome.storage.session.get(keys);
    const patch = {};
    let tabCount = 0;

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
        list = list.filter((s) => s.kind !== 'Media');
      }

      if (!list.some((s) => s.url === item.url)) {
        list.unshift(item);
        if (list.length > MAX_PER_TAB) list.length = MAX_PER_TAB;
      }

      patch[key] = list;
      tabCount = list.length;
    }

    const recent = data[RECENT_KEY] || [];
    if (!recent.some((s) => s.url === item.url)) {
      recent.unshift(item);
      if (recent.length > MAX_RECENT) recent.length = MAX_RECENT;
      patch[RECENT_KEY] = recent;
    }

    if (Object.keys(patch).length > 0) {
      await chrome.storage.session.set(patch);
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
  return serialize(() => chrome.storage.session.remove(tabKeyFor(tabId)));
}

/**
 * Drops per-tab lists whose tab no longer exists. onRemoved only fires while
 * the service worker is awake, so tabs closed during an idle period leak keys.
 * Called from the popup, which is the one moment the full key list matters.
 */
export function sweepOrphanTabs(liveTabIds) {
  return serialize(async () => {
    const live = new Set(liveTabIds.map((id) => tabKeyFor(id)));
    const all = await chrome.storage.session.get(null);
    const stale = Object.keys(all).filter((key) => key.startsWith('tab_') && !live.has(key));
    if (stale.length > 0) {
      await chrome.storage.session.remove(stale);
    }
    return stale.length;
  });
}

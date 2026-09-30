/**
 * Pure policy functions for stream ranking, grouping, selection, and filtering.
 */

const KIND_RANK = { HLS: 0, DASH: 0, MSS: 0, Abyss: 1, Media: 2, Audio: 2 };

/**
 * Ranks streams: manifests first (HLS, DASH, MSS), then Abyss, then Media/Audio,
 * high confidence before low, newest timestamp first.
 * Does not mutate the input array.
 */
export function rankStreams(streams) {
  if (!Array.isArray(streams)) return [];
  return [...streams].sort((a, b) =>
    (KIND_RANK[a.kind] ?? 3) - (KIND_RANK[b.kind] ?? 3) ||
    (a.confidence === 'low') - (b.confidence === 'low') ||
    (b.timestamp || 0) - (a.timestamp || 0)
  );
}

/**
 * Groups streams by referer origin, with null referer streams placed in a trailing bucket.
 * Returns Array<{ origin: string|null, items: Array }>.
 */
export function groupByOrigin(streams) {
  if (!Array.isArray(streams) || streams.length === 0) return [];

  const map = new Map();
  const noRefererItems = [];

  for (const item of streams) {
    if (!item.referer) {
      noRefererItems.push(item);
    } else {
      let origin;
      try {
        origin = new URL(item.referer).origin;
      } catch {
        origin = item.referer;
      }
      if (!map.has(origin)) {
        map.set(origin, { origin, items: [] });
      }
      map.get(origin).items.push(item);
    }
  }

  const groups = Array.from(map.values());
  if (noRefererItems.length > 0) {
    groups.push({ origin: null, items: noRefererItems });
  }

  return groups;
}

/**
 * Returns a new Set of selected URLs that are still present in streams.
 * Does not mutate the input selectedUrls Set.
 */
export function reconcileSelection(selectedUrls, streams) {
  const result = new Set();
  if (!selectedUrls || !streams || selectedUrls.size === 0) return result;

  const streamUrls = new Set();
  for (const s of streams) {
    if (s && s.url) streamUrls.add(s.url);
  }

  for (const url of selectedUrls) {
    if (streamUrls.has(url)) {
      result.add(url);
    }
  }
  return result;
}

/**
 * Tests whether a stream matches a case-insensitive search query against url or kind.
 */
export function matchesFilter(stream, query) {
  if (!query || typeof query !== 'string' || !query.trim()) return true;
  if (!stream) return false;

  const q = query.trim().toLowerCase();
  const urlMatch = Boolean(stream.url && stream.url.toLowerCase().includes(q));
  const kindMatch = Boolean(stream.kind && stream.kind.toLowerCase().includes(q));

  return urlMatch || kindMatch;
}

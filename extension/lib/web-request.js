/**
 * Register a webRequest listener attempting extraHeaders first (Chrome/Edge/Brave),
 * falling back to extraInfoSpec without extraHeaders if the browser throws (Firefox).
 */
export function addListenerWithExtraHeaders(event, callback, filter, extraInfoSpec = []) {
  try {
    event.addListener(callback, filter, [...extraInfoSpec, 'extraHeaders']);
  } catch {
    event.addListener(callback, filter, extraInfoSpec);
  }
}

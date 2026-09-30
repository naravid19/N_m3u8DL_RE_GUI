/**
 * Pure predicate for media URL filtering used by content scripts.
 * Runs as a classic script before content.js in MV3.
 */
(function () {
  function shouldReportUrl(url) {
    if (!url || typeof url !== 'string') return false;
    if (url.startsWith('blob:') || url.startsWith('data:') || url.startsWith('javascript:')) return false;
    if (!url.startsWith('http://') && !url.startsWith('https://')) return false;
    // Filter out synthetic player URLs using hash fragments for internal routing
    if (url.includes('#mp4/') || url.includes('#hls/') || url.includes('#chunk') || url.includes('maxChunkSize=')) return false;
    return true;
  }

  globalThis.NRE_MEDIA_URL = {
    shouldReportUrl
  };
})();

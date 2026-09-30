/**
 * Single source of truth for the extension version.
 * Reads directly from manifest.json via chrome.runtime.getManifest().
 */
export function getExtensionVersion() {
  if (typeof chrome !== "undefined" && chrome?.runtime?.getManifest) {
    const manifest = chrome.runtime.getManifest();
    if (manifest && manifest.version) {
      return manifest.version;
    }
  }
  return "";
}


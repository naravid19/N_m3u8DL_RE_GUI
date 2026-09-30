/**
 * Pure policy and transport functions for suite update checking.
 */

export function compareVersions(a, b) {
  const partsA = (a || "").replace(/^v/i, "").split(".").map(Number);
  const partsB = (b || "").replace(/^v/i, "").split(".").map(Number);
  for (let i = 0; i < Math.max(partsA.length, partsB.length); i++) {
    const numA = Number.isFinite(partsA[i]) ? partsA[i] : 0;
    const numB = Number.isFinite(partsB[i]) ? partsB[i] : 0;
    if (numA > numB) return 1;
    if (numA < numB) return -1;
  }
  return 0;
}

const SUITE_RELEASES_URL = "https://github.com/naravid19/N_m3u8DL_RE_GUI/releases/latest";

/**
 * Checks for suite updates against GitHub releases.
 * @param {string|null} suiteVersion The suite version shipped with the extension
 * @param {object} [options]
 * @param {Function} [options.fetchFn] Custom fetch function for testability
 * @returns {Promise<{ status: 'update-available' | 'up-to-date' | 'check-failed' | 'unknown-version', suiteVersion: string|null, latestVersion: string, releaseUrl: string }>}
 */
export async function checkSuiteUpdate(suiteVersion, options = {}) {
  if (!suiteVersion || typeof suiteVersion !== "string" || !suiteVersion.trim()) {
    return {
      status: "unknown-version",
      suiteVersion: null,
      latestVersion: "",
      releaseUrl: ""
    };
  }

  const cleanSuiteVersion = suiteVersion.trim();
  const fetchFn = options.fetchFn || (typeof fetch !== "undefined" ? fetch : null);

  if (!fetchFn) {
    return {
      status: "check-failed",
      suiteVersion: cleanSuiteVersion,
      latestVersion: "",
      releaseUrl: ""
    };
  }

  try {
    let response;
    try {
      response = await fetchFn(SUITE_RELEASES_URL, {
        method: "HEAD",
        redirect: "follow"
      });
    } catch (headErr) {
      console.debug("HEAD request failed, trying default fetch", headErr);
      response = await fetchFn(SUITE_RELEASES_URL, {
        redirect: "follow"
      });
    }

    if (!response || !response.url) {
      return {
        status: "check-failed",
        suiteVersion: cleanSuiteVersion,
        latestVersion: "",
        releaseUrl: ""
      };
    }

    const finalUrl = response.url;
    // Strict 3-component semantic version tag match
    const match = finalUrl.match(/\/tag\/v?([0-9]+\.[0-9]+\.[0-9]+)(?:[/?#]|$)/);
    if (!match) {
      return {
        status: "check-failed",
        suiteVersion: cleanSuiteVersion,
        latestVersion: "",
        releaseUrl: ""
      };
    }

    const latestTag = match[1];
    const isNewer = compareVersions(latestTag, cleanSuiteVersion) > 0;

    return {
      status: isNewer ? "update-available" : "up-to-date",
      suiteVersion: cleanSuiteVersion,
      latestVersion: `v${latestTag}`,
      releaseUrl: finalUrl
    };
  } catch (err) {
    console.debug("Suite update check failed:", err);
    return {
      status: "check-failed",
      suiteVersion: cleanSuiteVersion,
      latestVersion: "",
      releaseUrl: ""
    };
  }
}


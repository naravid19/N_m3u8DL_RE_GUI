/**
 * The suite release this extension shipped with, generated at build time from
 * Directory.Build.props. Null when unavailable — a development load from a
 * tree that has never been built, or a packaging mistake.
 */
export async function getSuiteVersion() {
  try {
    const url = chrome.runtime.getURL("suite-version.json");
    const response = await fetch(url);
    if (!response.ok) return null;

    const parsed = JSON.parse(await response.text());
    const value = typeof parsed?.suiteVersion === "string" ? parsed.suiteVersion.trim() : "";
    return value.length > 0 ? value : null;
  } catch {
    return null;
  }
}


/**
 * The only module that knows what an icon looks like.
 *
 * These replace the ten emoji the popup used to draw with. Emoji come from
 * the platform font, so they carry their own colour, weight and metrics —
 * a flat boxed wastebasket next to a full-colour refresh arrow never reads
 * as one set, because it is not one set. These share a 24×24 grid, a 2px
 * stroke, and currentColor, so every icon inherits the colour of the text
 * beside it, including hover and confirmation states.
 */

const SVG_NS = 'http://www.w3.org/2000/svg';

const ICON_PATHS = {
  download:    ['M12 3v11', 'M8 11l4 4 4-4', 'M4 17v2a2 2 0 002 2h12a2 2 0 002-2v-2'],
  clipboard:   ['M9 4h6v3H9z', 'M9 5.5H7a1 1 0 00-1 1V20a1 1 0 001 1h10a1 1 0 001-1V6.5a1 1 0 00-1-1h-2'],
  link:        ['M10 13a5 5 0 007.5.5l3-3a5 5 0 00-7-7l-1.5 1.5', 'M14 11a5 5 0 00-7.5-.5l-3 3a5 5 0 007 7l1.5-1.5'],
  refresh:     ['M20 12a8 8 0 11-2.6-5.9', 'M20 4v5h-5'],
  trash:       ['M4 7h16', 'M10 11v6', 'M14 11v6', 'M6 7l1 12a2 2 0 002 2h6a2 2 0 002-2l1-12', 'M9 7V4h6v3'],
  chevronRight:['M9 5l7 7-7 7'],
  chevronDown: ['M5 9l7 7 7-7'],
  check:       ['M20 6L9 17l-5-5'],
  signal:      ['M5 18a9 9 0 010-12', 'M19 6a9 9 0 010 12', 'M8.5 15a5 5 0 010-6', 'M15.5 9a5 5 0 010 6', 'M12 13a1 1 0 100-2 1 1 0 000 2z'],
  lightbulb:   ['M9 18h6', 'M10 21h4', 'M12 3a6 6 0 00-3.5 10.9c.3.3.5.7.5 1.1h6c0-.4.2-.8.5-1.1A6 6 0 0012 3z'],
  external:    ['M14 4h6v6', 'M11 13L20 4', 'M18 14v5a2 2 0 01-2 2H5a2 2 0 01-2-2V8a2 2 0 012-2h5']
};

export const ICON_NAMES = Object.freeze(Object.keys(ICON_PATHS));

export function hasIcon(name) {
  return Object.prototype.hasOwnProperty.call(ICON_PATHS, name);
}

/**
 * Builds one icon element. Returns null for an unknown name rather than
 * throwing: a missing icon must never take the whole popup down, which is
 * the failure mode a module-level throw would create.
 */
export function icon(name, size = 14) {
  if (!hasIcon(name)) return null;

  const svg = document.createElementNS(SVG_NS, 'svg');
  svg.setAttribute('viewBox', '0 0 24 24');
  svg.setAttribute('width', String(size));
  svg.setAttribute('height', String(size));
  svg.setAttribute('fill', 'none');
  svg.setAttribute('stroke', 'currentColor');
  svg.setAttribute('stroke-width', '2');
  svg.setAttribute('stroke-linecap', 'round');
  svg.setAttribute('stroke-linejoin', 'round');
  svg.setAttribute('aria-hidden', 'true');
  svg.classList.add('icon');

  for (const d of ICON_PATHS[name]) {
    const path = document.createElementNS(SVG_NS, 'path');
    path.setAttribute('d', d);
    svg.appendChild(path);
  }
  return svg;
}

/**
 * Replaces every [data-icon] placeholder in a root with its icon. Lets the
 * static markup in popup.html declare which icon it wants without popup.js
 * having to know the DOM shape around it.
 */
export function hydrateIcons(root = document) {
  for (const host of root.querySelectorAll('[data-icon]')) {
    const svg = icon(host.dataset.icon, Number(host.dataset.iconSize) || 14);
    if (!svg) continue;
    host.replaceChildren(svg);
  }
}

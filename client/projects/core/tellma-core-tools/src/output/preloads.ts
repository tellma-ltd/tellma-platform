// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { TM_BOOT_SCRIPT_FILE } from '../build/index-html-rules.js';
import { tmAttribute, tmElements, tmParseDocument } from '../common/html.js';

// The preloads (spec 0024 §2.6, step 3), which let the browser fetch during
// the boot script and the first parse what the first render needs: the fonts
// of Latin and the offered scripts, the Core chunk and the default language's
// string pack (fetched by the metadata loaders, hence `as="fetch"` with
// `crossorigin` to match their same-origin, CORS-mode request), and the
// /shell and /attention chunks with their static imports.

/** The files the built `index.html` loads before the app runs. */
export interface TmInitialFiles {
  /** The module and classic scripts, `tm-boot.js` excepted. */
  readonly scripts: readonly string[];
  /** The modules it preloads. */
  readonly modulePreloads: readonly string[];
  /** The stylesheets it links. */
  readonly stylesheets: readonly string[];
}

/** A local URL as a path relative to the output root, or null for another origin. */
function local(url: string | null): string | null {
  if (url === null || url === '' || /^[a-z][a-z0-9+.-]*:|^\/\//i.test(url)) {
    return null;
  }
  return url.replace(/^\.?\//, '').split(/[?#]/)[0]!;
}

/**
 * Reads the files the built `index.html` loads.
 *
 * @param html - The built file's text.
 * @returns Its scripts, module preloads and stylesheets.
 */
export function tmReadInitialFiles(html: string): TmInitialFiles {
  const scripts: string[] = [];
  const modulePreloads: string[] = [];
  const stylesheets: string[] = [];
  for (const element of tmElements(tmParseDocument(html))) {
    const rel = (tmAttribute(element, 'rel') ?? '').toLowerCase().split(/\s+/);
    if (element.tagName === 'script') {
      const src = local(tmAttribute(element, 'src'));
      if (src !== null && src !== TM_BOOT_SCRIPT_FILE) {
        scripts.push(src);
      }
    } else if (element.tagName === 'link' && rel.includes('modulepreload')) {
      const href = local(tmAttribute(element, 'href'));
      if (href !== null) {
        modulePreloads.push(href);
      }
    } else if (element.tagName === 'link' && rel.includes('stylesheet')) {
      const href = local(tmAttribute(element, 'href'));
      if (href !== null) {
        stylesheets.push(href);
      }
    }
  }
  return { scripts, modulePreloads, stylesheets };
}

/** The preloads the build adds. */
export interface TmPreloads {
  /** Font files, preloaded `as="font"`. */
  readonly fonts: readonly string[];
  /** Files the app fetches, preloaded `as="fetch"`. */
  readonly fetches: readonly string[];
  /** Module chunks, preloaded `rel="modulepreload"`. */
  readonly modules: readonly string[];
}

/** Escapes a URL for a double-quoted attribute. */
function attribute(value: string): string {
  return value.replace(/&/g, '&amp;').replace(/"/g, '&quot;');
}

/**
 * Adds the preloads to the built `index.html`, before `</head>`.
 *
 * @param html - The built file's text.
 * @param preloads - The files to preload.
 * @returns The new text.
 */
export function tmAddPreloads(html: string, preloads: TmPreloads): string {
  const links = [
    ...preloads.fonts.map((url) => `<link rel="preload" href="${attribute(url)}" as="font" type="font/woff2" crossorigin>`),
    ...preloads.fetches.map((url) => `<link rel="preload" href="${attribute(url)}" as="fetch" crossorigin>`),
    ...preloads.modules.map((url) => `<link rel="modulepreload" href="${attribute(url)}">`),
  ].join('');
  return html.replace(/<\/head>/i, `${links}</head>`);
}

// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { TM_BOOT_SCRIPT_FILE } from '../build/index-html-rules.js';
import { tmAttribute, tmElements, tmParseDocument } from '../common/html.js';
import { tmEntryPointChunk, tmEntryPointRoot, type TmBuildGraph } from './stats.js';

// The preloads (spec 0024 §2.6, step 3), which let the browser fetch during
// the boot script and the first parse what the first render needs: the fonts
// of Latin and the offered scripts, the Core chunk and the default language's
// string pack (fetched by the metadata loaders, hence `as="fetch"` with
// `crossorigin` to match their same-origin, CORS-mode request), and the
// /shell and /attention chunks with their static imports. The tenantless
// pages, a lazy chunk of /shell, are prefetched instead: a launch to the
// picker or the signed-out page fetches them beside /shell rather than after
// it (spec 0027 §1, Decision 1), while a tenant's first frame never waits on
// them.

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
  /** Module chunks a likely next route loads, fetched at low priority with `rel="prefetch"`. */
  readonly prefetches: readonly string[];
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
    ...preloads.prefetches.map((url) => `<link rel="prefetch" href="${attribute(url)}">`),
  ].join('');
  return html.replace(/<\/head>/i, `${links}</head>`);
}

/** The `/shell` and `/attention` chunks the build preloads, and the initial JavaScript with them. */
export interface TmShellModulePreloads {
  /** The chunks to modulepreload: those holding `/shell` or `/attention`, with their static imports, beyond what `index.html` already loads; in ordinal order. */
  readonly modules: readonly string[];
  /** The initial JavaScript: the scripts, their static imports, `index.html`'s own module preloads and the chunks above. */
  readonly initialJs: ReadonlySet<string>;
  /** The chunks to prefetch: the tenantless pages' lazy chunk of `/shell`, with its static imports beyond the initial JavaScript; in ordinal order. */
  readonly prefetches: readonly string[];
}

/**
 * Finds the `/shell` and `/attention` chunks to modulepreload: the chunks
 * holding each entry point's starting module, and their static imports. A
 * chunk they import dynamically, their own lazy chunks included, loads when
 * used; only the tenantless pages' chunk is prefetched, at low priority and
 * outside the initial JavaScript, so a launch to `/tenants` or `/signed-out`
 * does not fetch it only after `/shell`.
 *
 * @param graph - The build's chunk graph.
 * @param initial - The files the built `index.html` loads.
 * @returns The chunks to preload, the initial JavaScript they join, and the chunks to prefetch.
 */
export function tmShellModulePreloads(graph: TmBuildGraph, initial: TmInitialFiles): TmShellModulePreloads {
  const loaded = graph.staticClosure([...initial.scripts, ...initial.modulePreloads]);
  const shell = tmEntryPointRoot('tellma-core', 'shell');
  const attention = tmEntryPointRoot('tellma-core', 'attention');
  const roots = graph.outputsHolding((input) => shell(input) || attention(input));
  const modules = [...graph.staticClosure(roots)].filter((file) => !loaded.has(file)).sort();
  const initialJs = new Set([...loaded, ...modules]);
  const tenantless = graph.outputsHolding(tmEntryPointChunk('tellma-core', 'shell', 'tm-tenantless'));
  const prefetches = [...graph.staticClosure(tenantless)].filter((file) => !initialJs.has(file)).sort();
  return { modules, initialJs, prefetches };
}

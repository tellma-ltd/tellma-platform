// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { existsSync, readFileSync } from 'node:fs';
import { join } from 'node:path';

import type { TmBuildProblem } from '../common/problems.js';
import type { TmEmittedMetadata } from '../emit/emitted-metadata.js';
import { tmPngSize } from './png.js';

// The web app manifest (spec 0024 §5.1). The source check reads
// public/manifest.webmanifest and the icons it names; the build later adds
// the localized names from the string packs, so their lengths are checked
// here too, before anything is built. Every failure is `manifest-invalid`.

/** The build problem code of every failed manifest check. */
export const TM_MANIFEST_PROBLEM = 'manifest-invalid';

/** The manifest's file name, in public/ and at the output root. */
export const TM_MANIFEST_FILE = 'manifest.webmanifest';

/** The members the manifest must set. */
const REQUIRED_MEMBERS: readonly string[] = [
  'id',
  'start_url',
  'scope',
  'name',
  'short_name',
  'display',
  'dir',
  'lang',
  'background_color',
  'theme_color',
  'icons',
];

/** The most characters a short name may have, so a home screen shows it whole. */
const SHORT_NAME_MAX = 12;

/** Counts the characters a reader sees: grapheme clusters, not UTF-16 units. */
function graphemes(text: string): number {
  return [...new Intl.Segmenter('en', { granularity: 'grapheme' }).segment(text)].length;
}

/** The localized names the build adds to the output manifest. */
export interface TmLocalizedNames {
  /** `name_localized`: each offered language's `Distribution_DisplayName`. */
  readonly name_localized: Readonly<Record<string, string>>;
  /** `short_name_localized`: each offered language's `Distribution_ShortName`. */
  readonly short_name_localized: Readonly<Record<string, string>>;
}

/**
 * Computes the localized names from the string packs of the offered
 * languages; a language without a key gets no entry.
 *
 * @param metadata - The emitted metadata.
 * @returns The two maps, keyed by language code.
 */
export function tmLocalizedNames(metadata: TmEmittedMetadata): TmLocalizedNames {
  const names: Record<string, string> = {};
  const shortNames: Record<string, string> = {};
  for (const { code } of metadata.core.value.languages) {
    const pack = metadata.strings.get(code)?.value ?? {};
    if (typeof pack['Distribution_DisplayName'] === 'string') {
      names[code] = pack['Distribution_DisplayName'];
    }
    if (typeof pack['Distribution_ShortName'] === 'string') {
      shortNames[code] = pack['Distribution_ShortName'];
    }
  }
  return { name_localized: names, short_name_localized: shortNames };
}

/** The checks of one icon entry, against its file in public/. */
function checkIcon(icon: unknown, index: number, publicDir: string, fail: (message: string) => void): void {
  const entry = icon as { readonly src?: unknown; readonly sizes?: unknown; readonly type?: unknown } | null;
  if (typeof entry?.src !== 'string' || typeof entry.sizes !== 'string') {
    fail(`icons[${index}] needs a src and sizes.`);
    return;
  }
  const isPng = entry.type === 'image/png' || /\.png$/i.test(entry.src);
  if (!isPng) {
    return;
  }
  // The icon's file, by its URL relative to the manifest at the output root.
  const file = join(publicDir, ...entry.src.replace(/^\.?\//, '').split('/'));
  if (!existsSync(file)) {
    fail(`icons[${index}]: ${entry.src} does not exist in public/.`);
    return;
  }
  const size = tmPngSize(readFileSync(file));
  if (size === null) {
    fail(`icons[${index}]: ${entry.src} is not a PNG.`);
    return;
  }
  const stated = entry.sizes.trim().toLowerCase().split(/\s+/);
  if (!stated.includes(`${size.width}x${size.height}`)) {
    fail(`icons[${index}]: ${entry.src} is ${size.width}x${size.height}, but its sizes say '${entry.sizes}'.`);
  }
}

/**
 * Checks the source manifest, its PNG icons and the localized short names
 * the build will add.
 *
 * @param text - The manifest's text, or null when the file is missing.
 * @param publicDir - The folder the icons are in.
 * @param names - The localized names the build will add.
 * @param key - The manifest's path as the problem names it.
 * @param feature - The distribution's slug, which owns the file.
 * @returns One problem per failed check.
 */
export function tmCheckManifest(
  text: string | null,
  publicDir: string,
  names: TmLocalizedNames,
  key: string,
  feature: string,
): TmBuildProblem[] {
  const problems: TmBuildProblem[] = [];
  const fail = (message: string): void => {
    problems.push({ code: TM_MANIFEST_PROBLEM, severity: 'error', key, feature, message });
  };
  if (text === null) {
    fail(`The web app manifest public/${TM_MANIFEST_FILE} is missing.`);
    return problems;
  }
  let manifest: Record<string, unknown>;
  try {
    const parsed = JSON.parse(text) as unknown;
    if (typeof parsed !== 'object' || parsed === null || Array.isArray(parsed)) {
      throw new TypeError('not an object');
    }
    manifest = parsed as Record<string, unknown>;
  } catch {
    fail('The manifest is not a JSON object.');
    return problems;
  }

  // --- Members, names and related applications ---
  for (const name of REQUIRED_MEMBERS) {
    const value = manifest[name];
    if (value === undefined || value === null || value === '') {
      fail(`The member '${name}' is missing.`);
    }
  }
  if (typeof manifest['short_name'] === 'string' && graphemes(manifest['short_name']) > SHORT_NAME_MAX) {
    fail(`short_name '${manifest['short_name']}' is longer than ${SHORT_NAME_MAX} characters.`);
  }
  for (const [language, shortName] of Object.entries(names.short_name_localized)) {
    if (graphemes(shortName) > SHORT_NAME_MAX) {
      fail(`The ${language} short name '${shortName}' (Distribution_ShortName) is longer than ${SHORT_NAME_MAX} characters.`);
    }
  }
  if (manifest['prefer_related_applications'] === true) {
    fail('prefer_related_applications is true, which stops browsers offering to install the app.');
  }

  // --- Icons ---
  const icons = manifest['icons'];
  if (icons !== undefined && !Array.isArray(icons)) {
    fail("The member 'icons' is not an array.");
  } else if (Array.isArray(icons)) {
    icons.forEach((icon, index) => checkIcon(icon, index, publicDir, fail));
  }
  return problems;
}

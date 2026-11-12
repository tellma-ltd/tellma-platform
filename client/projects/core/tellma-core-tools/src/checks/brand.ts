// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { existsSync, readFileSync } from 'node:fs';
import { join } from 'node:path';

import { tmAttribute, tmElements, tmParseFragment } from '../common/html.js';
import type { TmBuildProblem } from '../common/problems.js';

// The brand files (spec 0024 §7.6): four SVGs the app renders inline, so
// each must scale (a viewBox) and carry no script. A distribution's
// public/brand/<name> replaces @tellma/core's brand/<name>. A file that fails
// is `brand-file-invalid`, as is a name neither folder provides.

/** The build problem code of every failed brand check. */
export const TM_BRAND_PROBLEM = 'brand-file-invalid';

/** The brand files' names, under `brand/` in the output. */
export const TM_BRAND_FILES: readonly string[] = Object.freeze([
  'wordmark-on-dark.svg',
  'wordmark-on-light.svg',
  'mark-on-dark.svg',
  'mark-on-light.svg',
]);

/** Where one brand file comes from. */
export interface TmBrandFile {
  /** The file's name, such as `mark-on-dark.svg`. */
  readonly name: string;
  /** The file it comes from, absolute; null when neither folder has it. */
  readonly source: string | null;
  /** Whether the distribution's public/brand/ provides it. */
  readonly override: boolean;
}

/**
 * Resolves each brand file: the distribution's override, else the default.
 *
 * @param publicDir - The distribution's public/ folder.
 * @param defaultsDir - `@tellma/core`'s brand/ folder, or null when the package has none.
 * @returns The four files, in the order of TM_BRAND_FILES.
 */
export function tmResolveBrandFiles(publicDir: string, defaultsDir: string | null): TmBrandFile[] {
  return TM_BRAND_FILES.map((name) => {
    const override = join(publicDir, 'brand', name);
    if (existsSync(override)) {
      return { name, source: override, override: true };
    }
    const fallback = defaultsDir === null ? null : join(defaultsDir, name);
    return { name, source: fallback !== null && existsSync(fallback) ? fallback : null, override: false };
  });
}

/** A viewBox: four numbers, the last two positive. */
const VIEW_BOX = /^\s*(-?[\d.]+(?:e-?\d+)?)[\s,]+(-?[\d.]+(?:e-?\d+)?)[\s,]+([\d.]+(?:e-?\d+)?)[\s,]+([\d.]+(?:e-?\d+)?)\s*$/i;

/**
 * Checks one brand SVG's text.
 *
 * @param svg - The file's text.
 * @returns What is wrong with it, or an empty list.
 */
export function tmBrandSvgFaults(svg: string): string[] {
  const faults: string[] = [];
  const elements = tmElements(tmParseFragment(svg));
  const root = elements.find((element) => element.tagName === 'svg');
  if (root === undefined) {
    return ['It holds no <svg> element.'];
  }
  const viewBox = VIEW_BOX.exec(tmAttribute(root, 'viewBox') ?? '');
  if (viewBox === null || Number(viewBox[3]) <= 0 || Number(viewBox[4]) <= 0) {
    faults.push('Its <svg> element has no valid viewBox, so it cannot scale.');
  }
  // Every element counts, including any outside the root, since the file renders inline.
  if (elements.some((element) => element.tagName === 'script')) {
    faults.push('It holds a <script> element.');
  }
  const handler = elements.flatMap((element) => element.attrs).find((attribute) => /^on/i.test(attribute.name));
  if (handler !== undefined) {
    faults.push(`It carries the event-handler attribute '${handler.name}'.`);
  }
  // A javascript: URL is a script by another door.
  const scriptUrl = elements
    .flatMap((element) => element.attrs)
    .find((attribute) => /href$/i.test(attribute.name) && /^\s*javascript:/i.test(attribute.value));
  if (scriptUrl !== undefined) {
    faults.push(`Its '${scriptUrl.name}' is a javascript: URL.`);
  }
  return faults;
}

/**
 * Checks the four brand files.
 *
 * @param files - The resolved files.
 * @param keyOf - The key a problem names for a file.
 * @param feature - The distribution's slug.
 * @returns One problem per fault.
 */
export function tmCheckBrandFiles(
  files: readonly TmBrandFile[],
  keyOf: (file: TmBrandFile) => string,
  feature: string,
): TmBuildProblem[] {
  const problems: TmBuildProblem[] = [];
  for (const file of files) {
    const key = keyOf(file);
    if (file.source === null) {
      problems.push({
        code: TM_BRAND_PROBLEM,
        severity: 'error',
        key,
        feature,
        message: `Neither public/brand/ nor @tellma/core's brand/ provides ${file.name}.`,
      });
      continue;
    }
    for (const message of tmBrandSvgFaults(readFileSync(file.source, 'utf8'))) {
      problems.push({ code: TM_BRAND_PROBLEM, severity: 'error', key, feature, message });
    }
  }
  return problems;
}

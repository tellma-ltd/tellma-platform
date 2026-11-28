// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { existsSync, readFileSync } from 'node:fs';
import { join } from 'node:path';

import { tmAttribute, tmElements, tmParseFragment, type TmHtmlElement } from '../common/html.js';
import type { TmBuildProblem } from '../common/problems.js';

// The brand files (spec 0024 §7.6): four SVGs the app renders inline, so
// each must scale (a viewBox) and carry no script. A distribution's
// public/brand/<name> replaces @tellma/core's brand/<name>. A file that fails
// is `brand-file-invalid`, as is a name neither folder provides. Each file's
// viewBox also gives its aspect ratio, its width over its height, which
// `build` and `serve` define as the constant TM_BRAND_RATIOS in the app's
// code, so the shells give each image its final box before its file loads.

/** The global constant the Angular build replaces with the brand files' ratios, which @tellma/core/shell reads. */
export const TM_BRAND_RATIOS_CONSTANT = 'TM_BRAND_RATIOS';

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

/** One number of SVG's number grammar: an optional sign, digits with an optional point (or a point and digits), an optional exponent. */
const NUMBER = /[+-]?(?:\d+\.?\d*|\.\d+)(?:e[+-]?\d+)?/gi;

/** The width and height of an `<svg>` root's viewBox, or null when it has no valid one. */
function viewBoxSize(root: TmHtmlElement): { readonly width: number; readonly height: number } | null {
  const value = tmAttribute(root, 'viewBox') ?? '';
  // Four numbers, apart from which only commas and whitespace occur: a sign
  // or a second point may also start the next number, as in `-.5-.5`.
  const numbers = value.match(NUMBER) ?? [];
  if (numbers.length !== 4 || !/^[\s,]*$/.test(value.replace(NUMBER, ''))) {
    return null;
  }
  const width = Number(numbers[2]);
  const height = Number(numbers[3]);
  return width > 0 && height > 0 && Number.isFinite(width) && Number.isFinite(height) ? { width, height } : null;
}

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
  if (viewBoxSize(root) === null) {
    faults.push('Its <svg> element has no valid viewBox, so it cannot scale.');
  }
  // Every element counts, including any outside the root, since the file
  // renders inline. Names are compared without their prefix: opened on its
  // own, the file is parsed as XML, where <s:script> in the SVG namespace is
  // a script, and the HTML parser reports it as 's:script'.
  if (elements.some((element) => localName(element.tagName) === 'script')) {
    faults.push('It holds a <script> element.');
  }
  const handler = elements.flatMap((element) => element.attrs).find((attribute) => localName(attribute.name).startsWith('on'));
  if (handler !== undefined) {
    faults.push(`It carries the event-handler attribute '${handler.name}'.`);
  }
  // A javascript: URL is a script by another door: on a link itself, or set
  // on one by an animation, which no href attribute shows.
  const scriptUrl = elements
    .flatMap((element) => element.attrs)
    .find((attribute) => localName(attribute.name) === 'href' && isScriptUrl(attribute.value));
  if (scriptUrl !== undefined) {
    faults.push(`Its '${scriptUrl.name}' is a javascript: URL.`);
  }
  const animation = elements.find(
    (element) =>
      ANIMATIONS.has(localName(element.tagName)) &&
      element.attrs.some((attribute) => localName(attribute.name) === 'attributename' && localName(attribute.value.trim()) === 'href'),
  );
  if (animation !== undefined) {
    faults.push(`Its <${animation.tagName}> element animates a link's href.`);
  }
  return faults;
}

/**
 * The aspect ratio of each brand file's viewBox, its width over its height,
 * from which the shells size each image of the file, so it has its final box
 * before the file loads. A file
 * whose box fails the brand check, or that neither folder provides, is left
 * out: the build fails on it.
 *
 * @param files - The resolved files.
 * @returns By file name without `.svg`, the ratio, such as 3.5384615384615383 for a 736 by 208 viewBox.
 */
export function tmBrandRatios(files: readonly TmBrandFile[]): Record<string, number> {
  const ratios: Record<string, number> = {};
  for (const file of files) {
    if (file.source === null) {
      continue;
    }
    const root = tmElements(tmParseFragment(readFileSync(file.source, 'utf8'))).find((element) => element.tagName === 'svg');
    const size = root === undefined ? null : viewBoxSize(root);
    if (size !== null) {
      ratios[file.name.replace(/\.svg$/, '')] = size.width / size.height;
    }
  }
  return ratios;
}

/**
 * The `--define` argument of `ng build` and `ng serve` that gives the app's
 * code the brand files' ratios as the constant TM_BRAND_RATIOS.
 *
 * @param files - The resolved files.
 * @returns The argument's value, such as `TM_BRAND_RATIOS={"wordmark-on-dark":3.5384615384615383}`.
 */
export function tmBrandDefine(files: readonly TmBrandFile[]): string {
  return `${TM_BRAND_RATIOS_CONSTANT}=${JSON.stringify(tmBrandRatios(files))}`;
}

/** The animation elements that can set another element's attribute. */
const ANIMATIONS: ReadonlySet<string> = new Set(['set', 'animate']);

/** A name without its namespace prefix, in lower case. */
function localName(name: string): string {
  return name.slice(name.lastIndexOf(':') + 1).toLowerCase();
}

/**
 * Whether a URL is a javascript: URL as the URL parser reads it, which drops
 * leading C0 controls and spaces, and every tab and line break.
 */
function isScriptUrl(url: string): boolean {
  // eslint-disable-next-line no-control-regex -- the URL parser's own character classes
  return /^javascript:/i.test(url.replace(/[\t\n\r]/g, '').replace(/^[\u0000- ]+/, ''));
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

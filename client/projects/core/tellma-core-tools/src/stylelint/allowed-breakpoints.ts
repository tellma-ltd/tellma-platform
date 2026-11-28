// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { readFileSync } from 'node:fs';
import { createRequire } from 'node:module';
import { join } from 'node:path';
import { fileURLToPath } from 'node:url';

import stylelint, { type Rule } from 'stylelint';

// tm/allowed-breakpoints: every width condition in a `@media` or
// `@container` prelude uses the platform's one spelling — range syntax on
// `width` or `inline-size`, a lower bound with `>=` and an upper bound with
// `<`, at a threshold from the closed breakpoint set: `(width >= 768px)`,
// `(width < 576px)`, `(576px <= width < 992px)`.
//
// Refused: `min-width`/`max-width`/`min-inline-size`/`max-inline-size`/
// `device-width` (and the other legacy width features), `width <= N`,
// `width > N` and every other operator arrangement, any unit but `px`, and any
// value outside the set. Each message names the nearest threshold and the
// canonical form. Other features (`height`, `orientation`, `pointer`,
// `hover`, `prefers-*`, `forced-colors`, …) are unrestricted.
//
// The thresholds are read from `@tellma/core-ui/breakpoints.json`, the same
// file `TM_BREAKPOINTS` imports, so stylesheets and script never disagree on
// a value. The breakpoint contract this rule enforces is spec 0010 §B.1.

/** The rule's name in a stylelint configuration. */
const ruleName = 'tm/allowed-breakpoints';

/** The shape of the `exports` entry this rule follows inside the platform's own workspace. */
interface TmLibraryManifest {
  readonly exports: Readonly<Record<string, string | { readonly default: string }>>;
}

/** The breakpoint file's package specifier: `@tellma/core-ui`'s export. */
const BREAKPOINTS_SPECIFIER = '@tellma/core-ui/breakpoints.json';

/** Resolves `specifier` as a module next to `from` (a file or package.json path), or `null`. */
function tryResolve(from: string, specifier: string): string | null {
  try {
    return createRequire(from).resolve(specifier);
  } catch {
    return null;
  }
}

/**
 * Inside the platform's own workspace the library is source rather than an
 * installed package, so the export is resolved through the library's manifest
 * instead — the same `exports` entry either way. `null` outside that workspace.
 */
function resolveFromSourceTree(): string | null {
  // src/stylelint and dist/stylelint both sit three levels inside client/projects/core.
  const libraryDir = fileURLToPath(new URL('../../../tellma-core-ui/', import.meta.url));
  let manifest: TmLibraryManifest;
  try {
    manifest = JSON.parse(readFileSync(join(libraryDir, 'package.json'), 'utf8')) as TmLibraryManifest;
  } catch {
    return null;
  }
  const target = manifest.exports['./breakpoints.json'];
  return target === undefined ? null : join(libraryDir, typeof target === 'string' ? target : target.default);
}

/**
 * Finds the breakpoint file of `@tellma/core-ui`, in order: from this
 * package's own location; from the project being linted (the current
 * directory); next to `@tellma/core`, whose peer it is, so a project that
 * depends on `@tellma/core` alone still finds it under a strict layout; and
 * in the platform's source tree.
 */
function breakpointsFile(): string {
  const project = join(process.cwd(), 'package.json');
  const core = tryResolve(project, '@tellma/core/package.json');
  const file =
    tryResolve(import.meta.url, BREAKPOINTS_SPECIFIER) ??
    tryResolve(project, BREAKPOINTS_SPECIFIER) ??
    (core === null ? null : tryResolve(core, BREAKPOINTS_SPECIFIER)) ??
    resolveFromSourceTree();
  if (file === null) {
    throw new Error(
      `tm/allowed-breakpoints could not find ${BREAKPOINTS_SPECIFIER}: add @tellma/core-ui to the project's dependencies.`,
    );
  }
  return file;
}

/** Reads the breakpoint thresholds from the library's breakpoint file. */
function loadBreakpoints(): number[] {
  const table = JSON.parse(readFileSync(breakpointsFile(), 'utf8')) as Readonly<Record<string, unknown>>;
  return Object.values(table)
    .map(Number)
    .sort((a, b) => a - b);
}

/**
 * The breakpoint thresholds in pixels, ascending. Exported for this package's
 * own tests only: the module is not one of the package's entry points.
 */
export const THRESHOLDS: readonly number[] = loadBreakpoints();

/** The thresholds as the messages list them. */
const SET_TEXT = THRESHOLDS.map((value) => `${value}px`).join(', ');

/** The features a width condition may name in range syntax. */
const RANGE_FEATURES: ReadonlySet<string> = new Set(['width', 'inline-size']);

/** Every width feature, legacy or not; anything else is unrestricted. */
const WIDTH_FEATURES: ReadonlySet<string> = new Set([
  'width',
  'min-width',
  'max-width',
  'inline-size',
  'min-inline-size',
  'max-inline-size',
  'device-width',
  'min-device-width',
  'max-device-width',
]);

/** The rule's messages. */
const messages = stylelint.utils.ruleMessages(ruleName, {
  rejected: (condition: string, suggestion: string) =>
    `Width condition "${condition}" is not a platform breakpoint query; write ${suggestion}. ` +
    `Width queries use range syntax on width or inline-size — a lower bound with >= and an ` +
    `upper bound with < — at a breakpoint: ${SET_TEXT}.`,
});

/** A length in CSS pixels, or null when it is not a plain length. */
function toPixels(text: string): number | null {
  const match = /^(-?\d*\.?\d+)(px|rem|em)?$/i.exec(text);
  if (!match) {
    return null;
  }
  const number = Number(match[1]);
  const unit = (match[2] ?? '').toLowerCase();
  return unit === 'px' || (unit === '' && number === 0) ? number : unit === '' ? null : number * 16;
}

/** The threshold nearest to a pixel length (the lower one on a tie). */
function nearest(pixels: number | null): number {
  const [first = 0] = THRESHOLDS;
  if (pixels === null) {
    return first;
  }
  return THRESHOLDS.reduce(
    (best, value) => (Math.abs(value - pixels) < Math.abs(best - pixels) ? value : best),
    first,
  );
}

/** Whether `text` is an exact threshold written in px. */
function isThreshold(text: string): boolean {
  const match = /^(\d+)px$/.exec(text);
  return match !== null && THRESHOLDS.includes(Number(match[1]));
}

/** The canonical feature name for a width feature (`max-inline-size` → `inline-size`). */
function canonicalFeature(name: string): string {
  return name.includes('inline-size') ? 'inline-size' : 'width';
}

/** The canonical lower bound `(feature >= Npx)` nearest a length. */
function lowerBound(feature: string, pixels: number | null): string {
  return `"(${feature} >= ${nearest(pixels)}px)"`;
}

/** The canonical upper bound `(feature < Npx)` nearest a length. */
function upperBound(feature: string, pixels: number | null): string {
  return `"(${feature} < ${nearest(pixels)}px)"`;
}

/**
 * Checks one innermost parenthesized condition. Returns a suggestion when the
 * condition is a width condition that is not in canonical form, else null.
 */
function check(condition: string): string | null {
  const text = condition.trim();
  // Custom properties (container style queries) are never width features.
  if (text.startsWith('--')) {
    return null;
  }

  // Legacy `name: value` form.
  const colon = text.indexOf(':');
  if (colon !== -1) {
    const name = text.slice(0, colon).trim().toLowerCase();
    if (!WIDTH_FEATURES.has(name)) {
      return null;
    }
    const feature = canonicalFeature(name);
    const pixels = toPixels(text.slice(colon + 1).trim());
    return name.startsWith('max-') ? upperBound(feature, pixels) : lowerBound(feature, pixels);
  }

  // Range form: `a op b` or `a op b op c`.
  const parts = text.split(/\s*(<=|>=|<|>|=)\s*/).map((part) => part.trim());
  if (parts.length !== 3 && parts.length !== 5) {
    return null; // a boolean feature such as (hover)
  }
  const nameIndex = parts.findIndex((part, index) => index % 2 === 0 && /^[a-z-]+$/i.test(part) && part !== '');
  if (nameIndex === -1) {
    return null;
  }
  const name = (parts[nameIndex] ?? '').toLowerCase();
  if (!WIDTH_FEATURES.has(name)) {
    return null;
  }
  const feature = canonicalFeature(name);
  const ok = RANGE_FEATURES.has(name);

  if (parts.length === 3) {
    const [left = '', op = '', right = ''] = parts;
    if (nameIndex === 0) {
      if (ok && op === '>=' && isThreshold(right)) {
        return null;
      }
      if (ok && op === '<' && isThreshold(right)) {
        return null;
      }
      const pixels = toPixels(right);
      return op.startsWith('<') ? upperBound(feature, pixels) : lowerBound(feature, pixels);
    }
    // `N op width`: an upper bound when N is above the width.
    const pixels = toPixels(left);
    return op.startsWith('>') ? upperBound(feature, pixels) : lowerBound(feature, pixels);
  }

  // Two-sided: only `N <= width < M`, with N below M.
  const [low = '', opLow, , opHigh, high = ''] = parts;
  const lowPixels = toPixels(low);
  const highPixels = toPixels(high);
  if (
    ok &&
    nameIndex === 2 &&
    opLow === '<=' &&
    opHigh === '<' &&
    isThreshold(low) &&
    isThreshold(high) &&
    lowPixels !== null &&
    highPixels !== null &&
    lowPixels < highPixels
  ) {
    return null;
  }
  const [a = null, b = null] = [lowPixels, highPixels].sort((x, y) => (x ?? 0) - (y ?? 0));
  return `"(${nearest(a)}px <= ${feature} < ${nearest(b)}px)"`;
}

/** A function call with no nested parentheses, such as `calc(1px + 2px)`. */
const FUNCTION_CALL = /[\w-]+\([^()]*\)/;

/** Every such function call in a prelude. */
const FUNCTION_CALL_GLOBAL = new RegExp(FUNCTION_CALL.source, 'g');

/** The rule: checks every innermost condition of every `@media` and `@container` prelude. */
const ruleFunction: Rule = Object.assign(
  (primary: unknown) =>
    (root: Parameters<ReturnType<Rule>>[0], result: Parameters<ReturnType<Rule>>[1]): void => {
      if (!primary) {
        return;
      }
      root.walkAtRules(/^(media|container)$/i, (atRule) => {
        // Function calls (calc(), a container's style()) collapse to a token
        // first, so a width condition holding one is still an innermost group
        // — and is refused, since no function value is a breakpoint.
        let params = atRule.params;
        while (FUNCTION_CALL.test(params)) {
          params = params.replace(FUNCTION_CALL_GLOBAL, 'fn');
        }
        // Every innermost parenthesized group is one condition.
        for (const match of params.matchAll(/\(([^()]*)\)/g)) {
          const condition = match[1] ?? '';
          const suggestion = check(condition);
          if (suggestion !== null) {
            stylelint.utils.report({
              ruleName,
              result,
              node: atRule,
              message: messages.rejected(`(${condition.trim()})`, suggestion),
            });
          }
        }
      });
    },
  { ruleName, messages },
);

/**
 * The stylelint plugin of `tm/allowed-breakpoints`: every width condition of
 * a `@media` or `@container` query is range syntax at a platform breakpoint.
 */
export const tmAllowedBreakpoints = stylelint.createPlugin(ruleName, ruleFunction);

// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

/**
 * tm/allowed-breakpoints — every width condition in a `@media` or
 * `@container` prelude uses the platform's one spelling:
 * range syntax on `width` or `inline-size`, a lower bound with `>=` and an
 * upper bound with `<`, at a threshold from the closed breakpoint set —
 * `(width >= 768px)`, `(width < 576px)`, `(576px <= width < 992px)`.
 *
 * Refused: `min-width`/`max-width`/`min-inline-size`/`max-inline-size`/
 * `device-width` (and the other legacy width features), `width <= N`,
 * `width > N` and every other operator arrangement, any unit but `px`, and
 * any value outside the set. Each message names the nearest threshold and
 * the canonical form. Other features (`height`, `orientation`, `pointer`,
 * `hover`, `prefers-*`, `forced-colors`, …) are unrestricted.
 *
 * The thresholds are read from `@tellma/core-ui/breakpoints.json` — the
 * same file `TM_BREAKPOINTS` imports — so stylesheets and script can never
 * disagree on a value.
 */
import { readFileSync } from 'node:fs';
import { createRequire } from 'node:module';
import { join } from 'node:path';
import { fileURLToPath, URL } from 'node:url';

import stylelint from 'stylelint';

// The breakpoint contract this rule enforces is spec 0010 §B.1.
const ruleName = 'tm/allowed-breakpoints';

/**
 * Resolves the breakpoint file through the `@tellma/core-ui` package
 * export. Inside the platform's own workspace the library is source rather
 * than an installed package, so the export is resolved through the
 * library's manifest instead — the same `exports` entry either way.
 */
function loadBreakpoints() {
  let file;
  try {
    file = createRequire(join(process.cwd(), 'package.json')).resolve(
      '@tellma/core-ui/breakpoints.json',
    );
  } catch {
    const libraryDir = fileURLToPath(
      new URL('../../projects/core/tellma-core-ui/', import.meta.url),
    );
    const manifest = JSON.parse(readFileSync(join(libraryDir, 'package.json'), 'utf8'));
    const target = manifest.exports['./breakpoints.json'];
    file = join(libraryDir, typeof target === 'string' ? target : target.default);
  }
  const table = JSON.parse(readFileSync(file, 'utf8'));
  return Object.values(table)
    .map(Number)
    .sort((a, b) => a - b);
}

const THRESHOLDS = loadBreakpoints();
const SET_TEXT = THRESHOLDS.map((value) => `${value}px`).join(', ');

/** The features a width condition may name in range syntax. */
const RANGE_FEATURES = new Set(['width', 'inline-size']);

/** Every width feature, legacy or not; anything else is unrestricted. */
const WIDTH_FEATURES = new Set([
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

const messages = stylelint.utils.ruleMessages(ruleName, {
  rejected: (condition, suggestion) =>
    `Width condition "${condition}" is not a platform breakpoint query; write ${suggestion}. ` +
    `Width queries use range syntax on width or inline-size — a lower bound with >= and an ` +
    `upper bound with < — at a breakpoint: ${SET_TEXT}.`,
});

/** A length in CSS pixels, or null when it is not a plain length. */
function toPixels(text) {
  const match = /^(-?\d*\.?\d+)(px|rem|em)?$/i.exec(text);
  if (!match) {
    return null;
  }
  const number = Number(match[1]);
  const unit = (match[2] ?? '').toLowerCase();
  return unit === 'px' || (unit === '' && number === 0) ? number : unit === '' ? null : number * 16;
}

/** The threshold nearest to a pixel length (the lower one on a tie). */
function nearest(pixels) {
  if (pixels === null) {
    return THRESHOLDS[0];
  }
  return THRESHOLDS.reduce((best, value) =>
    Math.abs(value - pixels) < Math.abs(best - pixels) ? value : best,
  );
}

/** Whether `text` is an exact threshold written in px. */
function isThreshold(text) {
  const match = /^(\d+)px$/.exec(text);
  return match !== null && THRESHOLDS.includes(Number(match[1]));
}

/** The canonical feature name for a width feature (`max-inline-size` → `inline-size`). */
function canonicalFeature(name) {
  return name.includes('inline-size') ? 'inline-size' : 'width';
}

/** `(feature >= Npx)`, `(feature < Npx)` or the two-sided form. */
function lowerBound(feature, pixels) {
  return `"(${feature} >= ${nearest(pixels)}px)"`;
}
function upperBound(feature, pixels) {
  return `"(${feature} < ${nearest(pixels)}px)"`;
}

/**
 * Checks one innermost parenthesized condition. Returns a suggestion when
 * the condition is a width condition that is not in canonical form, else
 * null.
 */
function check(condition) {
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
  const nameIndex = parts.findIndex(
    (part, index) => index % 2 === 0 && /^[a-z-]+$/i.test(part) && part !== '',
  );
  if (nameIndex === -1) {
    return null;
  }
  const name = parts[nameIndex].toLowerCase();
  if (!WIDTH_FEATURES.has(name)) {
    return null;
  }
  const feature = canonicalFeature(name);
  const ok = RANGE_FEATURES.has(name);

  if (parts.length === 3) {
    const [left, op, right] = parts;
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
  const [low, opLow, , opHigh, high] = parts;
  if (
    ok &&
    nameIndex === 2 &&
    opLow === '<=' &&
    opHigh === '<' &&
    isThreshold(low) &&
    isThreshold(high) &&
    toPixels(low) < toPixels(high)
  ) {
    return null;
  }
  const [a, b] = [toPixels(low), toPixels(high)].sort((x, y) => (x ?? 0) - (y ?? 0));
  return `"(${nearest(a)}px <= ${feature} < ${nearest(b)}px)"`;
}

/** A function call with no nested parentheses, such as `calc(1px + 2px)`. */
const FUNCTION_CALL = /[\w-]+\([^()]*\)/;
const FUNCTION_CALL_GLOBAL = new RegExp(FUNCTION_CALL.source, 'g');

const ruleFunction = (primary) => {
  return (root, result) => {
    if (!primary) {
      return;
    }
    root.walkAtRules(/^(media|container)$/i, (atRule) => {
      // Function calls (calc(), a container's style()) collapse to a token
      // first, so a width condition holding one is still an innermost
      // group — and is refused, since no function value is a breakpoint.
      let params = atRule.params;
      while (FUNCTION_CALL.test(params)) {
        params = params.replace(FUNCTION_CALL_GLOBAL, 'fn');
      }
      // Every innermost parenthesized group is one condition.
      for (const match of params.matchAll(/\(([^()]*)\)/g)) {
        const suggestion = check(match[1]);
        if (suggestion !== null) {
          stylelint.utils.report({
            ruleName,
            result,
            node: atRule,
            message: messages.rejected(`(${match[1].trim()})`, suggestion),
          });
        }
      }
    });
  };
};

ruleFunction.ruleName = ruleName;
ruleFunction.messages = messages;

export default stylelint.createPlugin(ruleName, ruleFunction);

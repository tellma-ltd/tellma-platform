// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

/**
 * Metric-override fallback faces (the fontaine technique): a local system
 * font is re-declared under the web font's metrics (size-adjust from the
 * average character width, ascent/descent/line-gap overrides), so text
 * renders at the web font's exact layout from the first frame and the
 * font-display swap causes no layout shift. Metrics come from
 * @capsizecss/metrics (dev-only; values are baked into the generated CSS).
 */
import { createRequire } from 'node:module';

const require = createRequire(import.meta.url);

/**
 * @param {object} options
 * @param {string} options.family    emitted font-family, e.g. 'Noto Sans Fallback'
 * @param {string} options.preferred @capsizecss/metrics id of the web font
 * @param {string} options.fallback  @capsizecss/metrics id of the local font
 * @param {string} options.local     local() source name, e.g. 'Arial'
 * @param {string} [options.unicodeRange] restrict the fallback to the web
 *   font's own scripts, so it never shadows another script's fallback.
 */
export function fallbackFace({ family, preferred, fallback, local, unicodeRange }) {
  const p = require(`@capsizecss/metrics/${preferred}`);
  const f = require(`@capsizecss/metrics/${fallback}`);
  const sizeAdjust = p.xWidthAvg / p.unitsPerEm / (f.xWidthAvg / f.unitsPerEm);
  const pct = (units) => `${((units / p.unitsPerEm / sizeAdjust) * 100).toFixed(2)}%`;
  return [
    '@font-face {',
    `  font-family: '${family}';`,
    `  src: local('${local}');`,
    `  size-adjust: ${(sizeAdjust * 100).toFixed(2)}%;`,
    `  ascent-override: ${pct(p.ascent)};`,
    `  descent-override: ${pct(Math.abs(p.descent))};`,
    `  line-gap-override: ${pct(p.lineGap)};`,
    ...(unicodeRange ? [`  unicode-range: ${unicodeRange};`] : []),
    '}',
    '',
  ].join('\n');
}

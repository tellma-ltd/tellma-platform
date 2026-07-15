// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { readFileSync } from 'node:fs';
import { join } from 'node:path';
import { describe, expect, it } from 'vitest';

/**
 * The committed @font-face stylesheets must carry the metric-adjusted local
 * fallback faces (size-adjust + ascent/descent overrides scoped by
 * unicode-range), so the font-display swap causes no layout shift.
 */
const CASES = [
  {
    css: 'projects/core/tellma-core-ui/fonts/fonts.css',
    families: ['Noto Sans Fallback', 'Noto Sans Mono Fallback'],
  },
  {
    css: 'projects/locale/tellma-locale-ar/fonts/fonts.css',
    families: ['Noto Sans Arabic Fallback'],
  },
];

describe('metric-override fallback faces', () => {
  for (const { css, families } of CASES) {
    it(`${css} declares ${families.join(', ')}`, () => {
      const text = readFileSync(join(process.cwd(), css), 'utf8');
      for (const family of families) {
        const block = text.split('@font-face').find((b) => b.includes(`'${family}'`));
        expect(block, family).toBeDefined();
        expect(block).toContain('src: local(');
        expect(block).toMatch(/size-adjust: \d+(\.\d+)?%/);
        expect(block).toMatch(/ascent-override: \d+(\.\d+)?%/);
        expect(block).toMatch(/descent-override: \d+(\.\d+)?%/);
        expect(block).toContain('unicode-range:');
      }
    });
  }
});

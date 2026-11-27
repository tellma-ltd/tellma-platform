// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { readFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';

import { describe, expect, it } from 'vitest';

import { TM_HOME_WIDGETS } from '../../projects/core/tellma-core/dashboards/src/tm-home-widgets.js';
import { tmPackDashboard } from '../../projects/core/tellma-core/dashboards/src/tm-pack-dashboard.js';

// The home skeleton's frames are placed by the global dashboard stylesheet,
// which the route host draws before the dashboards chunk loads, so they
// cannot read the layout the home page computes. This check holds the
// stylesheet's numbers to that layout: the skeleton's three frames stand
// where tmPackDashboard places the home widgets, in both layouts.

const clientDir = join(dirname(fileURLToPath(import.meta.url)), '..', '..');

/** The global dashboard stylesheet. */
const css = readFileSync(join(clientDir, 'projects', 'core', 'tellma-core', 'styles', 'tm-dashboard.css'), 'utf8');

/**
 * The placement properties the stylesheet gives the skeleton's nth frame.
 *
 * @param nth - The frame, 1-based.
 * @returns Its column, span, row, rows and narrow row.
 */
function frame(nth: number): number[] {
  const rule = new RegExp(`\\.tm-home-skeleton > :nth-child\\(${nth}\\) \\{([^}]*)\\}`).exec(css);
  if (rule === null) {
    return [];
  }
  const value = (name: string): number => Number(new RegExp(`${name}:\\s*(\\d+);`).exec(rule[1]!)?.[1]);
  return ['--tm-col', '--tm-span', '--tm-row', '--tm-rows', '--tm-row-narrow'].map(value);
}

describe('the home skeleton', () => {
  it("places its frames where the home page's layout places the home widgets, wide and narrow", () => {
    const placements = tmPackDashboard(TM_HOME_WIDGETS).map(({ col, span, row, rows, rowNarrow }) => [col, span, row, rows, rowNarrow]);

    expect([1, 2, 3].map(frame)).toEqual(placements);
    expect(css).not.toMatch(/\.tm-home-skeleton > :nth-child\(4\)/);
  });
});

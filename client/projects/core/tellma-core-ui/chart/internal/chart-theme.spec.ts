// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { ɵtmReadChartTheme } from './chart-theme';

// The theme bridge reads the chart tokens off the chart's element; marks
// meet 3:1 against the plot's ground (spec 0034 §7.8), so the ramp it hands
// the option builder keeps only the steps that do.

/** A ramp from near-white to near-black, one grey per step. */
const GREYS = [250, 220, 190, 140, 90, 50, 20];

/** The element the theme is read off, removed after each test. */
let host: HTMLElement | undefined;

/** An element in the document whose ramp is the given greys and whose ground is the given grey. */
function chartElement(ground: number, ramp: readonly number[] = GREYS): HTMLElement {
  host = document.createElement('div');
  ramp.forEach((grey, index) =>
    host!.style.setProperty(`--chart-sequential-${index + 1}`, `rgb(${grey}, ${grey}, ${grey})`),
  );
  host.style.setProperty('--chart-surface', `rgb(${ground}, ${ground}, ${ground})`);
  document.body.append(host);
  return host;
}

/** The greys at the given steps, as the theme resolves them. */
function greysAt(steps: readonly number[], ramp: readonly number[] = GREYS): string[] {
  return steps.map((step) => `rgba(${ramp[step]}, ${ramp[step]}, ${ramp[step]}, 1)`);
}

describe('ɵtmReadChartTheme', () => {
  afterEach(() => {
    host?.remove();
    host = undefined;
  });

  it('keeps the ramp steps that meet 3:1 against the ground, in either scheme', () => {
    // On white the three faintest steps fall short of 3:1; on black the
    // last two do.
    expect(ɵtmReadChartTheme(chartElement(255)).sequential).toEqual(greysAt([3, 4, 5, 6]));
    host!.remove();
    expect(ɵtmReadChartTheme(chartElement(0)).sequential).toEqual(greysAt([0, 1, 2, 3, 4]));
  });

  it('keeps every step when fewer than two meet 3:1, so series still differ', () => {
    const close = [100, 110, 120, 130, 140, 150, 160];
    expect(ɵtmReadChartTheme(chartElement(128, close)).sequential).toEqual(
      greysAt([0, 1, 2, 3, 4, 5, 6], close),
    );
    host!.remove();
    // Only the last step meets 3:1 against white.
    const one = [250, 240, 230, 220, 210, 200, 90];
    expect(ɵtmReadChartTheme(chartElement(255, one)).sequential).toEqual(
      greysAt([0, 1, 2, 3, 4, 5, 6], one),
    );
  });
});

// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import type { ɵTmChartTheme } from './chart-plot';

// The theme bridge (spec 0034 §7.4): the chart's colours and font come from
// its tokens, read through getComputedStyle on the chart's own element, so a
// scheme, a distribution's theme layer or a scoped override all apply. No
// colour is written in code. ECharts parses only sRGB colour strings, so
// each token's resolved value (a hex, an oklch(), a color-mix()) is painted
// on a one-pixel canvas and read back as rgba().

/** The least contrast a mark keeps against the plot's ground (WCAG 1.4.11). */
const MARK_CONTRAST = 3;

/**
 * The relative luminance of a colour as the theme resolves it,
 * `rgba(r, g, b, a)`; `NaN` for a value left unresolved (no canvas).
 */
function luminance(color: string): number {
  const [r, g, b] = color
    .slice(5)
    .split(',', 3)
    .map((channel) => {
      const value = Number(channel) / 255;
      return value <= 0.04045 ? value / 12.92 : ((value + 0.055) / 1.055) ** 2.4;
    }) as [number, number, number];
  return color.startsWith('rgba(') ? 0.2126 * r + 0.7152 * g + 0.0722 * b : NaN;
}

/**
 * The ramp's steps a mark may take: those that meet 3:1 against the
 * ground. A ramp starts at a step near the ground's own lightness, so its
 * faintest steps would draw near-invisible marks. All of them when fewer
 * than two pass (one step would give every series the same colour) or the
 * colours cannot be read.
 */
function markSteps(ramp: readonly string[], ground: string): readonly string[] {
  const back = luminance(ground);
  const passing = ramp.filter((color) => {
    const front = luminance(color);
    // An unreadable colour (NaN) fails no comparison, so it is kept.
    return !((Math.max(front, back) + 0.05) / (Math.min(front, back) + 0.05) < MARK_CONTRAST);
  });
  return passing.length > 1 ? passing : ramp;
}

/** The pixel a colour is painted on and read back from; one per page. */
let pixel: CanvasRenderingContext2D | null | undefined;

/** The shared one-pixel context, or `null` where canvas is unavailable. */
function pixelContext(): CanvasRenderingContext2D | null {
  if (pixel === undefined) {
    const canvas = document.createElement('canvas');
    canvas.width = 1;
    canvas.height = 1;
    pixel = canvas.getContext('2d', { willReadFrequently: true });
  }
  return pixel;
}

/**
 * A CSS colour as `rgba(r, g, b, a)`, resolved by the canvas. A value the
 * canvas does not parse resolves to transparent black, never to the
 * previous colour.
 */
export function ɵtmResolveChartColor(value: string): string {
  const context = pixelContext();
  if (context === null) {
    return value;
  }
  context.clearRect(0, 0, 1, 1);
  // An invalid assignment is ignored, so transparent is set first.
  context.fillStyle = 'rgba(0, 0, 0, 0)';
  context.fillStyle = value;
  context.fillRect(0, 0, 1, 1);
  const [r, g, b, a] = context.getImageData(0, 0, 1, 1).data;
  return `rgba(${r}, ${g}, ${b}, ${Math.round(((a ?? 0) / 255) * 1000) / 1000})`;
}

/**
 * Reads the chart tokens off an element: the series and the ramp in their
 * fixed order (the ramp's steps that meet 3:1 against the plot's ground only), the diverging pair, the line and text colours, the threshold
 * tones, the plot's ground, the tooltip, the decal colour and the font.
 *
 * @param element - The chart's plot element; its computed style is read.
 * @returns The theme, every colour as `rgba()`.
 */
export function ɵtmReadChartTheme(element: Element): ɵTmChartTheme {
  const style = getComputedStyle(element);
  const read = (name: string): string => style.getPropertyValue(name).trim();
  const color = (name: string): string => ɵtmResolveChartColor(read(name));
  const fontSize = Number.parseFloat(read('--chart-font-size'));
  const surface = color('--chart-surface');
  return {
    series: Array.from({ length: 8 }, (_, index) => color(`--chart-series-${index + 1}`)),
    sequential: markSteps(
      Array.from({ length: 7 }, (_, index) => color(`--chart-sequential-${index + 1}`)),
      surface,
    ),
    divergingNegative: color('--chart-diverging-negative'),
    divergingPositive: color('--chart-diverging-positive'),
    grid: color('--chart-grid'),
    axis: color('--chart-axis'),
    label: color('--chart-label'),
    tones: {
      positive: color('--chart-positive'),
      warning: color('--chart-warning'),
      negative: color('--chart-negative'),
    },
    surface,
    tooltipBg: color('--chart-tooltip-bg'),
    tooltipBorder: color('--chart-tooltip-border'),
    decal: color('--chart-decal'),
    fontFamily: read('--font-ui') || style.fontFamily,
    fontSize: Number.isFinite(fontSize) && fontSize > 0 ? fontSize : 12,
  };
}

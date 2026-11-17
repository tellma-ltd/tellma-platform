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
 * fixed order, the diverging pair, the line and text colours, the threshold
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
  return {
    series: Array.from({ length: 8 }, (_, index) => color(`--chart-series-${index + 1}`)),
    sequential: Array.from({ length: 7 }, (_, index) => color(`--chart-sequential-${index + 1}`)),
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
    surface: color('--chart-surface'),
    tooltipBg: color('--chart-tooltip-bg'),
    tooltipBorder: color('--chart-tooltip-border'),
    decal: color('--chart-decal'),
    fontFamily: read('--font-ui') || style.fontFamily,
    fontSize: Number.isFinite(fontSize) && fontSize > 0 ? fontSize : 12,
  };
}

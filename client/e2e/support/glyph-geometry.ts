// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { expect, type Locator } from '@playwright/test';

// Glyph geometry for the e2e batteries: what a drawn glyph measures against
// the container that boxes it, and the assertions every built-in glyph
// shares (the Lucide stroke contract, a token-sized square, centring and
// the right-to-left mirror).

/** Sub-pixel rounding allowed on a measured edge or centre. */
export const EPSILON = 0.5;

/** What one glyph measures: its drawn box, its container's, and its strokes. */
export interface GlyphMeasure {
  /** The drawn `<svg>` box. */
  readonly svg: { x: number; y: number; width: number; height: number };
  /** The container's box. */
  readonly container: { x: number; y: number; width: number; height: number };
  /** The container's inline-end border plus padding, in px. */
  readonly containerEndInset: number;
  /** The token value the glyph is sized from, in px. */
  readonly tokenSize: number;
  /** Every shape: its tag, geometry attributes and computed stroke. */
  readonly shapes: readonly {
    readonly tag: string;
    readonly geometry: string;
    readonly strokeWidth: string;
    readonly lineCap: string;
    readonly lineJoin: string;
    readonly fill: string;
    readonly strokeIsCurrentColor: boolean;
    readonly painted: boolean;
  }[];
  /** The viewBox the shapes are drawn in. */
  readonly viewBox: string | null;
}

/**
 * Measures a glyph host — an inline `<svg>` or a `tm-icon` holding one —
 * against the container it sits in. Any other element measures as its own
 * box, with the shapes drawn inside it.
 */
export async function measure(
  glyph: Locator,
  container: Locator,
  token: string,
): Promise<GlyphMeasure> {
  const containerHandle = await container.elementHandle();
  return glyph.evaluate(
    (host, args) => {
      // A host that draws no glyph itself (a checkbox box) measures as its own box.
      const svg = host.localName === 'svg' ? host : (host.querySelector(':scope > svg') ?? host);
      const rect = (el: Element) => {
        const r = el.getBoundingClientRect();
        return { x: r.x, y: r.y, width: r.width, height: r.height };
      };
      const box = args.container as Element;
      const boxStyle = getComputedStyle(box);
      const rtl = boxStyle.direction === 'rtl';
      const endInset = rtl
        ? parseFloat(boxStyle.borderLeftWidth) + parseFloat(boxStyle.paddingLeft)
        : parseFloat(boxStyle.borderRightWidth) + parseFloat(boxStyle.paddingRight);
      const geometryAttributes = ['d', 'cx', 'cy', 'r', 'x1', 'x2', 'y1', 'y2'];
      const shapes = [...svg.querySelectorAll('path, circle, line, polyline, rect')].map(
        (shape) => {
          const style = getComputedStyle(shape);
          return {
            tag: shape.localName,
            geometry: geometryAttributes
              .filter((name) => shape.hasAttribute(name))
              .map((name) => `${name}=${shape.getAttribute(name)}`)
              .join(' '),
            strokeWidth: style.strokeWidth,
            lineCap: style.strokeLinecap,
            lineJoin: style.strokeLinejoin,
            fill: style.fill,
            strokeIsCurrentColor: style.stroke === style.color,
            painted: shape.checkVisibility({ opacityProperty: true, visibilityProperty: true }),
          };
        },
      );
      return {
        svg: rect(svg),
        container: rect(box),
        containerEndInset: endInset,
        tokenSize: parseFloat(
          getComputedStyle(document.documentElement).getPropertyValue(args.token),
        ),
        shapes,
        viewBox: svg.getAttribute('viewBox'),
      };
    },
    { container: containerHandle, token },
  );
}

/** Asserts the Lucide drawing contract on every shape of a glyph. */
export function expectLucideStroke(measured: GlyphMeasure, geometry: readonly string[]): void {
  expect(measured.viewBox).toBe('0 0 24 24');
  expect(measured.shapes.map((shape) => shape.geometry)).toEqual(geometry);
  for (const shape of measured.shapes) {
    expect(shape.strokeWidth).toBe('1.75px');
    expect(shape.lineCap).toBe('round');
    expect(shape.lineJoin).toBe('round');
    expect(shape.fill).toBe('none');
    expect(shape.strokeIsCurrentColor).toBe(true);
  }
}

/** Asserts a square glyph of the token's size. */
export function expectTokenSquare(measured: GlyphMeasure): void {
  expect(measured.tokenSize).toBeGreaterThan(0);
  expect(Math.abs(measured.svg.width - measured.tokenSize)).toBeLessThanOrEqual(EPSILON);
  expect(Math.abs(measured.svg.height - measured.tokenSize)).toBeLessThanOrEqual(EPSILON);
}

/** The vertical centre offset between a glyph and its container. */
export function blockCentreOffset(measured: GlyphMeasure): number {
  return Math.abs(
    measured.svg.y +
      measured.svg.height / 2 -
      (measured.container.y + measured.container.height / 2),
  );
}

/** The gap between the glyph and its container's inline-end edge. */
export function inlineEndGap(measured: GlyphMeasure, dir: 'ltr' | 'rtl'): number {
  return dir === 'rtl'
    ? measured.svg.x - measured.container.x
    : measured.container.x + measured.container.width - (measured.svg.x + measured.svg.width);
}

/**
 * Whether a glyph host draws its `<svg>` mirrored: a horizontal flip
 * (`scale(-1, 1)`) on the svg, read from its computed transform.
 */
export async function isMirrored(glyph: Locator): Promise<boolean> {
  return glyph.evaluate((host) => {
    const svg = host.localName === 'svg' ? host : host.querySelector(':scope > svg');
    if (svg === null) {
      return false;
    }
    const transform = getComputedStyle(svg).transform;
    return transform !== 'none' && new DOMMatrix(transform).a === -1;
  });
}

// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { type Locator, type Page } from '@playwright/test';

/**
 * Pixel reads of what the browser painted. A computed style says what a
 * rule asks for; only the screenshot says what reached the screen — under
 * forced colors the browser rewrites colours, and a border whose colour
 * becomes the canvas colour is drawn yet invisible. These helpers decode a
 * Playwright screenshot inside the page itself (an image decoded onto a
 * canvas), so no image library is needed.
 */

/** One RGB colour, each channel 0–255. */
export interface Rgb {
  /** The red channel. */
  readonly r: number;
  /** The green channel. */
  readonly g: number;
  /** The blue channel. */
  readonly b: number;
}

/** A colour with its opacity, as a page resolves it: RGB channels 0–255, alpha 0–1. */
export interface Rgba extends Rgb {
  /** The opacity, from 0 (transparent) to 1 (opaque). */
  readonly a: number;
}

/**
 * What `layers` paint over `below`, the first layer lowest: each is
 * composited source-over on what is under it, as the screen blends a
 * translucent colour. An opaque layer replaces what is under it.
 */
export function paintOver(below: Rgb, layers: readonly Rgba[]): Rgb {
  return layers.reduce<Rgb>(
    (under, layer) => ({
      r: Math.round(layer.r * layer.a + under.r * (1 - layer.a)),
      g: Math.round(layer.g * layer.a + under.g * (1 - layer.a)),
      b: Math.round(layer.b * layer.a + under.b * (1 - layer.a)),
    }),
    below,
  );
}

/** How many pixels of an image {@link TmPixels.paintedOver} found painted, out of how many it read. */
export interface TmPaintedCount {
  /** The pixels that show the layers painted over the reference pixel. */
  readonly painted: number;
  /** The pixels read. */
  readonly total: number;
}

/** A decoded screenshot: its size and its RGBA bytes, row by row. */
export class TmPixels {
  /**
   * @param width - The image width in pixels.
   * @param height - The image height in pixels.
   * @param data - The RGBA bytes, four per pixel, row by row.
   */
  constructor(
    readonly width: number,
    readonly height: number,
    readonly data: Uint8Array,
  ) {}

  /** The colour at (`x`, `y`), clamped into the image. */
  at(x: number, y: number): Rgb {
    const cx = Math.min(this.width - 1, Math.max(0, Math.round(x)));
    const cy = Math.min(this.height - 1, Math.max(0, Math.round(y)));
    const offset = (cy * this.width + cx) * 4;
    return { r: this.data[offset], g: this.data[offset + 1], b: this.data[offset + 2] };
  }

  /**
   * How many pixels differ from the same pixel of `other` by more than
   * `tolerance` on some channel. The images must be the same size.
   */
  differingPixels(other: TmPixels, tolerance = 24): number {
    if (other.width !== this.width || other.height !== this.height) {
      throw new Error(
        `Cannot compare a ${this.width}x${this.height} image with a ${other.width}x${other.height} one.`,
      );
    }
    let count = 0;
    for (let offset = 0; offset < this.data.length; offset += 4) {
      if (
        Math.abs(this.data[offset] - other.data[offset]) > tolerance ||
        Math.abs(this.data[offset + 1] - other.data[offset + 1]) > tolerance ||
        Math.abs(this.data[offset + 2] - other.data[offset + 2]) > tolerance
      ) {
        count += 1;
      }
    }
    return count;
  }

  /**
   * How many pixels show `layers` painted over the same pixel of `before`, a
   * shot of the same box without them: this image's pixel is within
   * `tolerance` of the layers composited on `before`'s. Reading against the
   * pixel beneath, not a fixed colour, holds on a platform whose system
   * colour is translucent and blends with what it covers. `where` limits
   * the read to some pixels, such as a ring at the edge. The images must be
   * the same size.
   *
   * Every layer is assumed painted at every pixel read, and only the top
   * one is tested: a pixel counts when the top layer moves some channel by
   * more than `minChange` from the lower layers composited on `before`'s
   * pixel, so a pixel where the lower layers are not painted counts
   * whenever it happens to show the top layer's colour. Pass more than one
   * layer only for pixels where the lower ones are known to be painted;
   * to read a mark drawn on a fill, shoot the mark's own box against the
   * same box with the mark hidden, and pass the mark's colour alone.
   */
  paintedOver(
    before: TmPixels,
    layers: readonly Rgba[],
    options: {
      readonly tolerance?: number;
      readonly minChange?: number;
      readonly where?: (x: number, y: number) => boolean;
    } = {},
  ): TmPaintedCount {
    if (before.width !== this.width || before.height !== this.height) {
      throw new Error(
        `Cannot compare a ${this.width}x${this.height} image with a ${before.width}x${before.height} one.`,
      );
    }
    const { tolerance = 3, minChange = 24, where = () => true } = options;
    let painted = 0;
    let total = 0;
    for (let y = 0; y < this.height; y += 1) {
      for (let x = 0; x < this.width; x += 1) {
        if (!where(x, y)) {
          continue;
        }
        total += 1;
        const under = before.at(x, y);
        const expected = paintOver(under, layers);
        // What the top layer covers: the reference pixel under the others.
        const covered = paintOver(under, layers.slice(0, -1));
        const changes = !near(expected, covered, minChange);
        if (changes && near(this.at(x, y), expected, tolerance)) {
          painted += 1;
        }
      }
    }
    return { painted, total };
  }

  /** How many pixels of the image differ from `colour` by more than `tolerance`. */
  pixelsUnlike(colour: Rgb, tolerance = 24): number {
    let count = 0;
    for (let offset = 0; offset < this.data.length; offset += 4) {
      if (
        !near(
          { r: this.data[offset], g: this.data[offset + 1], b: this.data[offset + 2] },
          colour,
          tolerance,
        )
      ) {
        count += 1;
      }
    }
    return count;
  }
}

/** Whether two colours are within `tolerance` of each other on every channel. */
export function near(a: Rgb, b: Rgb, tolerance = 24): boolean {
  return (
    Math.abs(a.r - b.r) <= tolerance &&
    Math.abs(a.g - b.g) <= tolerance &&
    Math.abs(a.b - b.b) <= tolerance
  );
}

/**
 * Screenshots the element (grown by `margin` pixels on every side, so an
 * outline drawn outside the box is in the picture) and decodes it. The
 * caret is hidden, so a blinking text caret never makes two shots differ.
 */
export async function pixelsOf(page: Page, locator: Locator, margin = 0): Promise<TmPixels> {
  const box = await locator.boundingBox();
  if (box === null) {
    throw new Error('The element to screenshot is not rendered.');
  }
  const png = await page.screenshot({
    clip: {
      x: Math.max(0, box.x - margin),
      y: Math.max(0, box.y - margin),
      width: box.width + 2 * margin,
      height: box.height + 2 * margin,
    },
    caret: 'hide',
    animations: 'disabled',
  });
  const decoded = await page.evaluate(async (base64) => {
    const bytes = Uint8Array.from(atob(base64), (char) => char.charCodeAt(0));
    const bitmap = await createImageBitmap(new Blob([bytes], { type: 'image/png' }));
    const canvas = new OffscreenCanvas(bitmap.width, bitmap.height);
    const context = canvas.getContext('2d')!;
    context.drawImage(bitmap, 0, 0);
    const rgba = context.getImageData(0, 0, bitmap.width, bitmap.height).data;
    // Back to the test as base64: a byte array as JSON would be ten times larger.
    let binary = '';
    for (let index = 0; index < rgba.length; index += 0x8000) {
      binary += String.fromCharCode(...rgba.subarray(index, index + 0x8000));
    }
    return { width: bitmap.width, height: bitmap.height, data: btoa(binary) };
  }, png.toString('base64'));
  return new TmPixels(
    decoded.width,
    decoded.height,
    new Uint8Array(Buffer.from(decoded.data, 'base64')),
  );
}

/**
 * A CSS colour value as this page resolves it, alpha included, such as a
 * system colour under forced colors. Some platforms' forced palettes are
 * translucent (Linux Chromium's Highlight), so what such a colour paints
 * depends on what lies beneath: read it with {@link TmPixels.paintedOver}.
 */
export async function resolvedColour(page: Page, cssColour: string): Promise<Rgba> {
  return page.evaluate((value) => {
    const probe = document.createElement('div');
    // Opting out of forced colours keeps the value as given; a system
    // colour still resolves to the forced palette's.
    probe.style.cssText = `forced-color-adjust:none;background-color:${value};`;
    document.body.append(probe);
    const [r, g, b, a = 1] = (getComputedStyle(probe).backgroundColor.match(/[0-9.]+/g) ?? []).map(
      Number,
    );
    probe.remove();
    return { r, g, b, a };
  }, cssColour);
}

/**
 * The colour a CSS colour value paints as on this page, such as a system
 * colour (`Canvas`, `CanvasText`, `Highlight`) under forced colors. Its
 * alpha is dropped, so it is what an opaque colour paints; for one that
 * may be translucent, use {@link resolvedColour}.
 */
export async function paintedColour(page: Page, cssColour: string): Promise<Rgb> {
  return page.evaluate((value) => {
    const probe = document.createElement('div');
    // Opting out of forced colours keeps the value as given; a system
    // colour still resolves to the forced palette's.
    probe.style.cssText = `forced-color-adjust:none;background-color:${value};`;
    document.body.append(probe);
    const match = /rgba?\((\d+), (\d+), (\d+)/.exec(getComputedStyle(probe).backgroundColor)!;
    probe.remove();
    return { r: Number(match[1]), g: Number(match[2]), b: Number(match[3]) };
  }, cssColour);
}

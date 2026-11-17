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
 * The colour a CSS colour value paints as on this page, such as a system
 * colour (`Canvas`, `CanvasText`, `Highlight`) under forced colors.
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

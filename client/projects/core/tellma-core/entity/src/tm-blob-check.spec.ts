// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import type { TmBlobKindPolicy } from '@tellma/core';

import { ɵtmByteSize, ɵtmCropCoverSquare, tmCheckBlobFile } from './tm-blob-check';

// The client blob-kind check and the cover-square crop, over real files
// and a real canvas in the browser, and a file's size as a person reads it.

/** A customer image's policy, as the entity model carries it. */
const IMAGE: TmBlobKindPolicy = {
  kind: 'customer-image',
  accept: ['image/jpeg', 'image/png', 'image/webp', 'image/gif'],
  maxSize: 1024,
  image: { maxDimension: 64, fit: 'cover-square', format: 'webp' },
  thumbnail: true,
  readAccess: 'owner-read',
};

/**
 * A file of a size and a type.
 *
 * @param size - Its size in bytes.
 * @param type - Its content type.
 * @returns The file.
 */
function file(size: number, type: string): File {
  return new File([new Uint8Array(size)], 'picked', { type });
}

/**
 * A PNG of a size, its left half red and its right half blue.
 *
 * @param width - Its width.
 * @param height - Its height.
 * @returns The image.
 */
async function picture(width: number, height: number): Promise<Blob> {
  const canvas = new OffscreenCanvas(width, height);
  const context = canvas.getContext('2d')!;
  context.fillStyle = '#ff0000';
  context.fillRect(0, 0, width / 2, height);
  context.fillStyle = '#0000ff';
  context.fillRect(width / 2, 0, width / 2, height);
  return canvas.convertToBlob({ type: 'image/png' });
}

/**
 * A JPEG of a picture, its EXIF orientation set: the camera's sensor holds
 * the picture as drawn, and a viewer turns it as the tag says.
 *
 * @param picture - The picture, as the sensor holds it.
 * @param orientation - The EXIF orientation, such as 6 for a quarter turn clockwise.
 * @returns The JPEG.
 */
async function withOrientation(picture: Blob, orientation: number): Promise<Blob> {
  const bitmap = await createImageBitmap(picture);
  const canvas = new OffscreenCanvas(bitmap.width, bitmap.height);
  canvas.getContext('2d')!.drawImage(bitmap, 0, 0);
  const jpeg = new Uint8Array(await (await canvas.convertToBlob({ type: 'image/jpeg', quality: 1 })).arrayBuffer());

  // An APP1 segment: "Exif" and a little-endian TIFF header with one IFD
  // entry, Orientation (0x0112), a SHORT holding the tag's value.
  const tiff = [0x49, 0x49, 0x2a, 0x00, 0x08, 0x00, 0x00, 0x00, 0x01, 0x00, 0x12, 0x01, 0x03, 0x00, 0x01, 0x00, 0x00, 0x00, orientation, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00];
  const payload = [0x45, 0x78, 0x69, 0x66, 0x00, 0x00, ...tiff];
  const length = payload.length + 2;
  const app1 = new Uint8Array([0xff, 0xe1, length >> 8, length & 0xff, ...payload]);

  // The segment goes right after the start-of-image marker.
  return new Blob([jpeg.slice(0, 2), app1, jpeg.slice(2)], { type: 'image/jpeg' });
}

describe('tmCheckBlobFile', () => {
  it('passes a file of an accepted type within the size', () => {
    expect(tmCheckBlobFile(IMAGE, file(1024, 'image/png'))).toBe('ok');
    expect(tmCheckBlobFile(IMAGE, file(10, 'IMAGE/JPEG'))).toBe('ok');
  });

  it('refuses a file larger than the policy allows', () => {
    expect(tmCheckBlobFile(IMAGE, file(1025, 'image/png'))).toBe('size');
  });

  it('refuses a typed file the kind does not accept', () => {
    expect(tmCheckBlobFile(IMAGE, file(10, 'image/avif'))).toBe('type');
    expect(tmCheckBlobFile(IMAGE, file(10, 'application/pdf'))).toBe('type');
  });

  it('matches a wildcard entry, and leaves an untyped file to the server', () => {
    const any = { ...IMAGE, accept: ['image/*'] };
    expect(tmCheckBlobFile(any, file(10, 'image/avif'))).toBe('ok');
    expect(tmCheckBlobFile(any, file(10, 'text/plain'))).toBe('type');
    expect(tmCheckBlobFile(IMAGE, file(10, ''))).toBe('ok');
  });
});

describe('cover-square crop', () => {
  it('crops the framed square, scaled to the largest edge, in the kind’s format', async () => {
    // The right half of a 200 × 100 image is the blue square the user framed.
    const cropped = await ɵtmCropCoverSquare(
      await picture(200, 100),
      { rect: { x: 0.5, y: 0, width: 0.5, height: 1 }, focal: { x: 0.75, y: 0.5 } },
      IMAGE.image!,
    );
    expect(cropped.type).toBe('image/webp');
    const bitmap = await createImageBitmap(cropped);
    expect([bitmap.width, bitmap.height]).toEqual([64, 64]);
    const canvas = new OffscreenCanvas(64, 64);
    const context = canvas.getContext('2d')!;
    context.drawImage(bitmap, 0, 0);
    const [red, , blue] = context.getImageData(32, 32, 1, 1).data;
    expect(blue).toBeGreaterThan(200);
    expect(red).toBeLessThan(50);
  });

  it('frames a photo as it is shown, turned by its EXIF orientation', async () => {
    // The sensor holds a 200 × 100 picture, red left and blue right; turned a
    // quarter clockwise it shows 100 × 200, red above and blue below. The
    // user framed the lower half of what they saw.
    const photo = await withOrientation(await picture(200, 100), 6);
    const shown = await createImageBitmap(photo, { imageOrientation: 'from-image' });
    expect([shown.width, shown.height]).toEqual([100, 200]);

    const cropped = await ɵtmCropCoverSquare(photo, { rect: { x: 0, y: 0.5, width: 1, height: 0.5 }, focal: { x: 0.5, y: 0.75 } }, IMAGE.image!);
    const bitmap = await createImageBitmap(cropped);
    expect([bitmap.width, bitmap.height]).toEqual([64, 64]);
    const canvas = new OffscreenCanvas(64, 64);
    const context = canvas.getContext('2d')!;
    context.drawImage(bitmap, 0, 0);
    // Blue across the square; a crop of the unturned picture would be red on its left.
    for (const [x, y] of [[8, 32], [32, 32], [56, 32]] as const) {
      const [red, , blue] = context.getImageData(x, y, 1, 1).data;
      expect(blue).toBeGreaterThan(180);
      expect(red).toBeLessThan(70);
    }
  });

  it('keeps a small crop at its own size', async () => {
    const cropped = await ɵtmCropCoverSquare(
      await picture(40, 40),
      { rect: { x: 0, y: 0, width: 1, height: 1 }, focal: { x: 0.5, y: 0.5 } },
      IMAGE.image!,
    );
    const bitmap = await createImageBitmap(cropped);
    expect([bitmap.width, bitmap.height]).toEqual([40, 40]);
  });

  it('uploads a contained image as it was picked', async () => {
    const picked = await picture(40, 20);
    const kept = await ɵtmCropCoverSquare(picked, { rect: { x: 0, y: 0, width: 1, height: 1 }, focal: { x: 0.5, y: 0.5 } }, {
      maxDimension: 400,
      fit: 'contain',
      format: 'png',
    });
    expect(kept).toBe(picked);
  });
});

describe('a file size', () => {
  it('takes the largest binary unit the size fills, with one fraction digit at most', () => {
    expect(ɵtmByteSize(0, 'en')).toBe('0 byte');
    expect(ɵtmByteSize(1023, 'en')).toBe('1,023 byte');
    expect(ɵtmByteSize(1024, 'en')).toBe('1 kB');
    expect(ɵtmByteSize(1536, 'en')).toBe('1.5 kB');
    expect(ɵtmByteSize(1024 * 1024 - 1, 'en')).toBe('1,024 kB');
    expect(ɵtmByteSize(10 * 1024 * 1024, 'en')).toBe('10 MB');
    expect(ɵtmByteSize(5 * 1024 ** 3, 'en')).toBe('5 GB');
    // Gigabytes are the largest unit.
    expect(ɵtmByteSize(2 * 1024 ** 4, 'en')).toBe('2,048 GB');
  });

  it("reads a long's decimal text, and says nothing for a size that is not one", () => {
    expect(ɵtmByteSize('2048', 'en')).toBe('2 kB');
    expect(ɵtmByteSize('', 'en')).toBe('0 byte');
    expect(ɵtmByteSize('large', 'en')).toBe('');
    expect(ɵtmByteSize(-1, 'en')).toBe('');
  });

  it("writes the locale's digits and separators", () => {
    expect(ɵtmByteSize(1536, 'ar')).toContain('كيلوبايت');
    const arabicDigits = ɵtmByteSize(1536, 'ar-u-nu-arab');
    expect(arabicDigits).toContain('١٫٥');
    expect(arabicDigits).not.toMatch(/[0-9]/);
    expect(ɵtmByteSize(1536, 'de')).toContain('1,5');
  });
});

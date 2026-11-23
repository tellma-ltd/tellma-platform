// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import type { TmBlobKindPolicy } from '@tellma/core';

import { ɵtmCropCoverSquare, tmCheckBlobFile } from './tm-blob-check';

// The client blob-kind check and the cover-square crop, over real files
// and a real canvas in the browser.

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

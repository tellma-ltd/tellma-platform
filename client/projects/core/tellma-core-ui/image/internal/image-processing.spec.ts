// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { tmProcessPickedFile } from './image-processing';
import { tmDefaultSrcForSize, tmSizeBucket } from './size-buckets';

/** Draws a canvas and encodes it. */
async function makeImageBlob(
  width: number,
  height: number,
  type: 'image/png' | 'image/jpeg',
): Promise<Blob> {
  const canvas = document.createElement('canvas');
  canvas.width = width;
  canvas.height = height;
  const context = canvas.getContext('2d')!;
  context.fillStyle = '#4ca0b6';
  context.fillRect(0, 0, width, height);
  context.fillStyle = '#0a141a';
  context.fillRect(0, 0, Math.ceil(width / 2), Math.ceil(height / 2));
  return new Promise((resolve) => canvas.toBlob((blob) => resolve(blob!), type));
}

/** A minimal single-frame GIF (1×1, GIF89a). */
function tinyGif(): Blob {
  const bytes = Uint8Array.from(
    atob('R0lGODlhAQABAIAAAP///wAAACH5BAEAAAAALAAAAAABAAEAAAICRAEAOw=='),
    (char) => char.codePointAt(0)!,
  );
  return new Blob([bytes], { type: 'image/gif' });
}

const NO_LIMITS = { maxBytes: 20 * 1024 * 1024, maxEdgePx: 4096 };

describe('tmSizeBucket', () => {
  it('picks the smallest covering bucket at min(dpr, 2)', () => {
    expect(tmSizeBucket(48, 48, 1)).toBe(64);
    expect(tmSizeBucket(48, 48, 2)).toBe(128);
    expect(tmSizeBucket(48, 48, 3)).toBe(128); // dpr capped at 2
    expect(tmSizeBucket(96, 40, 1)).toBe(128); // longest edge decides
    expect(tmSizeBucket(200, 200, 2)).toBe(512);
    expect(tmSizeBucket(1500, 400, 2)).toBe(2048);
    expect(tmSizeBucket(4000, 4000, 2)).toBe(2048); // top bucket caps
  });

  it('the default srcForSize appends the size query', () => {
    expect(tmDefaultSrcForSize('/api/img/7', 128)).toBe('/api/img/7?size=128');
    expect(tmDefaultSrcForSize('/api/img/7?v=2', 128)).toBe('/api/img/7?v=2&size=128');
  });
});

describe('tmProcessPickedFile', () => {
  it('rejects oversized files before any decoding', async () => {
    const blob = await makeImageBlob(8, 8, 'image/png');
    const result = await tmProcessPickedFile(blob, { maxBytes: 10, maxEdgePx: 4096 });
    expect(result.kind).toBe('tooLarge');
  });

  it('rejects files that fail to decode', async () => {
    const result = await tmProcessPickedFile(
      new Blob(['not an image'], { type: 'image/png' }),
      NO_LIMITS,
    );
    expect(result.kind).toBe('undecodable');
  });

  it('passes a within-limits file through byte-identical', async () => {
    const blob = await makeImageBlob(120, 80, 'image/jpeg');
    const result = await tmProcessPickedFile(blob, NO_LIMITS);
    expect(result.kind).toBe('ok');
    if (result.kind === 'ok') {
      expect(result.blob).toBe(blob); // untouched — no needless re-encode
      expect(result.width).toBe(120);
      expect(result.height).toBe(80);
    }
  });

  it('downscales past the edge limit, re-encoding as JPEG for opaque sources', async () => {
    const blob = await makeImageBlob(300, 100, 'image/jpeg');
    const result = await tmProcessPickedFile(blob, { maxBytes: NO_LIMITS.maxBytes, maxEdgePx: 150 });
    expect(result.kind).toBe('ok');
    if (result.kind === 'ok') {
      expect(result.width).toBe(150);
      expect(result.height).toBe(50);
      expect(result.blob.type).toBe('image/jpeg');
      const check = await createImageBitmap(result.blob);
      expect(check.width).toBe(150);
      check.close();
    }
  });

  it('de-animates GIFs at original resolution (no downscale, PNG re-encode)', async () => {
    const result = await tmProcessPickedFile(tinyGif(), {
      maxBytes: NO_LIMITS.maxBytes,
      maxEdgePx: 4096,
    });
    expect(result.kind).toBe('ok');
    if (result.kind === 'ok') {
      expect(result.blob.type).toBe('image/png'); // first frame, de-animated
      expect(result.width).toBe(1);
      expect(result.height).toBe(1);
    }
  });
});

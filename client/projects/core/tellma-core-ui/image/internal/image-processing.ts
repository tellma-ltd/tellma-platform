// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

/** The outcome of running a picked file through the guardrails. */
export type TmProcessedPick =
  | {
      readonly kind: 'ok';
      /** The bytes to upload — the original file unless a re-encode was needed. */
      readonly blob: Blob;
      /** Pixel dimensions of `blob` — the frame the fit rect normalizes against. */
      readonly width: number;
      readonly height: number;
    }
  | { readonly kind: 'tooLarge' }
  | { readonly kind: 'undecodable' };

/** Limits of {@link tmProcessPickedFile}; both surface as component inputs. */
export interface TmImageLimits {
  /** Reject files over this many bytes before any processing. */
  readonly maxBytes: number;
  /** Downscale output whose longest edge exceeds this many pixels. */
  readonly maxEdgePx: number;
}

/**
 * Whether the source FORMAT can carry transparency — the deterministic
 * signal for choosing the re-encode target. (A sampled-pixel probe would
 * miss small transparent regions once smoothing averages them out, and
 * flattening real alpha to JPEG black is the worse failure.)
 */
function formatCarriesAlpha(type: string): boolean {
  return type === 'image/png' || type === 'image/webp' || type === 'image/avif' || type === 'image/gif';
}

/** Draws the bitmap at the given size and encodes it. */
async function reencode(
  bitmap: ImageBitmap,
  width: number,
  height: number,
  preserveAlpha: boolean,
): Promise<Blob | null> {
  const canvas = document.createElement('canvas');
  canvas.width = width;
  canvas.height = height;
  const context = canvas.getContext('2d');
  if (context === null) {
    return null;
  }
  context.drawImage(bitmap, 0, 0, width, height);
  return new Promise<Blob | null>((resolve) => {
    if (preserveAlpha) {
      canvas.toBlob(resolve, 'image/png');
    } else {
      canvas.toBlob(resolve, 'image/jpeg', 0.9);
    }
  });
}

/**
 * Runs a picked file through the edit-mode guardrails:
 *
 * - Files over `maxBytes` are rejected before any decoding (a limit only
 *   realistically hit by abuse).
 * - GIFs are silently de-animated — a canvas pass keeps the first frame at
 *   ORIGINAL resolution (exempt from the downscale; the byte limit still
 *   applies).
 * - Anything else that decodes (`createImageBitmap` — EXIF orientation
 *   applies automatically) passes through byte-identical, unless its
 *   longest edge exceeds `maxEdgePx`: then it is downscaled and re-encoded
 *   (JPEG 0.9, or PNG when the source carries alpha).
 * - A file that fails to decode is rejected.
 */
export async function tmProcessPickedFile(
  file: Blob,
  limits: TmImageLimits,
): Promise<TmProcessedPick> {
  if (file.size > limits.maxBytes) {
    return { kind: 'tooLarge' };
  }
  let bitmap: ImageBitmap;
  try {
    bitmap = await createImageBitmap(file);
  } catch {
    return { kind: 'undecodable' };
  }
  try {
    const { width, height } = bitmap;
    if (file.type === 'image/gif') {
      // De-animate: keep the first frame at original resolution (exempt
      // from the downscale by policy) — but never attempt a canvas past
      // engine area limits: reject upfront instead of allocating one
      // that yields a null encode anyway.
      if (width * height > 64_000_000) {
        return { kind: 'undecodable' };
      }
      const blob = await reencode(bitmap, width, height, true);
      return blob === null ? { kind: 'undecodable' } : { kind: 'ok', blob, width, height };
    }
    const longest = Math.max(width, height);
    if (longest <= limits.maxEdgePx) {
      return { kind: 'ok', blob: file, width, height };
    }
    const scale = limits.maxEdgePx / longest;
    const scaledWidth = Math.max(1, Math.round(width * scale));
    const scaledHeight = Math.max(1, Math.round(height * scale));
    const blob = await reencode(bitmap, scaledWidth, scaledHeight, formatCarriesAlpha(file.type));
    return blob === null
      ? { kind: 'undecodable' }
      : { kind: 'ok', blob, width: scaledWidth, height: scaledHeight };
  } finally {
    bitmap.close();
  }
}

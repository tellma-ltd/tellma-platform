// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import type { TmBlobKindPolicy } from '@tellma/core';
import type { TmImageFit } from '@tellma/core-ui/image';

// Client blob-kind policies (spec 0029 §16): a file is checked against its
// kind's policy before it uploads, so a refusal is immediate and the upload
// never starts. The server stays the authority on type: a file the browser
// cannot type passes here and is judged there. A `cover-square` kind is
// cropped on the client, to the square the user framed, since the server
// never re-crops.

/** What a file check found: the file passes, or is too large, or is of a type the kind does not accept. */
export type TmBlobFileCheck = 'ok' | 'size' | 'type';

/**
 * Whether a content type matches one entry of a policy's `accept` list:
 * the same type, ignoring case, or any subtype of a `type/*` entry.
 *
 * @param type - The file's content type, such as `image/png`.
 * @param accept - The entry, such as `image/*`.
 * @returns Whether it matches.
 */
function accepts(type: string, accept: string): boolean {
  const entry = accept.trim().toLowerCase();
  return entry.endsWith('/*') ? type.startsWith(entry.slice(0, -1)) : type === entry;
}

/**
 * Checks a file against its blob kind's policy before it uploads: `size`
 * when it is larger than the policy allows, `type` when the browser types
 * it and the type is not among those the kind accepts, `ok` otherwise. A
 * file the browser cannot type passes, and the server judges it.
 *
 * @param policy - The blob kind's policy.
 * @param file - The file.
 * @returns The check's finding.
 *
 * @example
 * ```ts
 * if (tmCheckBlobFile(property.blob, file) === 'size') {
 *   // "This file is too large."
 * }
 * ```
 */
export function tmCheckBlobFile(policy: TmBlobKindPolicy, file: File): TmBlobFileCheck {
  if (file.size > policy.maxSize) {
    return 'size';
  }
  const type = file.type.toLowerCase();
  return type === '' || policy.accept.length === 0 || policy.accept.some((entry) => accepts(type, entry)) ? 'ok' : 'type';
}

/**
 * Crops a picked image to the square the user framed, scaled down to the
 * kind's largest edge and encoded in the kind's format, for a
 * `cover-square` kind; any other image is returned as picked. The fit is
 * in the image's display orientation, which the decode applies. Internal.
 *
 * @param blob - The picked image.
 * @param fit - The framed crop, as fractions of the oriented image.
 * @param image - The kind's image rules.
 * @returns The image to upload.
 * @throws Error when the browser cannot decode or encode the image.
 */
export async function ɵtmCropCoverSquare(
  blob: Blob,
  fit: TmImageFit,
  image: NonNullable<TmBlobKindPolicy['image']>,
): Promise<Blob> {
  if (image.fit !== 'cover-square') {
    return blob;
  }

  // --- The framed square in the oriented image's pixels ---
  const bitmap = await createImageBitmap(blob, { imageOrientation: 'from-image' });
  try {
    const side = Math.max(1, Math.round(Math.min(fit.rect.width * bitmap.width, fit.rect.height * bitmap.height)));
    const x = Math.min(Math.max(0, Math.round(fit.rect.x * bitmap.width)), bitmap.width - side);
    const y = Math.min(Math.max(0, Math.round(fit.rect.y * bitmap.height)), bitmap.height - side);
    const edge = Math.min(side, image.maxDimension);

    // --- Drawn at the largest edge, then encoded in the kind's format ---
    const canvas = new OffscreenCanvas(edge, edge);
    const context = canvas.getContext('2d');
    if (context === null) {
      throw new Error('The browser offers no 2D canvas to crop the image with.');
    }
    context.imageSmoothingQuality = 'high';
    context.drawImage(bitmap, x, y, side, side, 0, 0, edge, edge);
    return await canvas.convertToBlob({ type: `image/${image.format}`, ...(image.format !== 'png' && { quality: 0.9 }) });
  } finally {
    bitmap.close();
  }
}

/** The byte-size formats, by locale and unit, built once each. */
const BYTE_FORMATS = new Map<string, Intl.NumberFormat>();

/** The units a size is written in, each 1024 times the one before. */
const BYTE_UNITS = ['byte', 'kilobyte', 'megabyte', 'gigabyte'] as const;

/**
 * A file's size as a person reads it, in the largest unit it fills:
 * `512 bytes`, `1.5 kB`, `10 MB`, with one fraction digit at most. The
 * units are binary (a kilobyte is 1024 bytes), as file pickers show them.
 * Internal: the file views and the editors' size limits share it.
 *
 * @param bytes - The size, in bytes; a `long` as its decimal text too.
 * @param locale - The locale to write it in.
 * @returns The text; empty when the size is not a number.
 */
export function ɵtmByteSize(bytes: number | string, locale: string): string {
  let size = Number(bytes);
  if (!Number.isFinite(size) || size < 0) {
    return '';
  }
  let unit = 0;
  while (size >= 1024 && unit < BYTE_UNITS.length - 1) {
    size /= 1024;
    unit++;
  }
  const key = `${locale}|${unit}`;
  let format = BYTE_FORMATS.get(key);
  if (format === undefined) {
    format = new Intl.NumberFormat(locale, { style: 'unit', unit: BYTE_UNITS[unit], unitDisplay: 'short', maximumFractionDigits: unit === 0 ? 0 : 1 });
    BYTE_FORMATS.set(key, format);
  }
  return format.format(size);
}

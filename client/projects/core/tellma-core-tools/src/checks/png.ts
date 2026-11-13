// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

// A PNG's pixel size, from its header: the 8-byte signature, then the IHDR
// chunk (length, type, then width and height as big-endian 32-bit integers),
// which the PNG standard requires first.

/** The PNG signature. */
const SIGNATURE = [0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a];

/** A PNG's size in pixels. */
export interface TmPngSize {
  /** The width. */
  readonly width: number;
  /** The height. */
  readonly height: number;
}

/**
 * Reads a PNG's size from its header.
 *
 * @param bytes - The file's bytes; the first 24 suffice.
 * @returns The size, or null when the bytes do not open with a PNG signature and IHDR chunk.
 */
export function tmPngSize(bytes: Uint8Array): TmPngSize | null {
  if (bytes.length < 24 || !SIGNATURE.every((value, index) => bytes[index] === value)) {
    return null;
  }
  const view = new DataView(bytes.buffer, bytes.byteOffset, bytes.byteLength);
  const type = String.fromCharCode(bytes[12]!, bytes[13]!, bytes[14]!, bytes[15]!);
  if (type !== 'IHDR') {
    return null;
  }
  return { width: view.getUint32(16), height: view.getUint32(20) };
}

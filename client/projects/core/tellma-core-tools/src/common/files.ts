// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { createHash } from 'node:crypto';
import { readdirSync } from 'node:fs';
import { join, relative, sep } from 'node:path';
import { brotliCompressSync, constants, gzipSync } from 'node:zlib';

// File helpers the build shares: output-relative paths in URL form on every
// OS, a recursive listing, the emitted files' content hash, and the two
// compressed sizes the budgets report.

/** A path relative to `from`, with forward slashes on every OS. */
export function tmRelativePosix(from: string, to: string): string {
  return relative(from, to).split(sep).join('/');
}

/**
 * Lists every file under a directory, recursively, as paths relative to it
 * with forward slashes, in ordinal order.
 *
 * @param dir - The directory.
 * @returns The relative paths of its files.
 */
export function tmListFiles(dir: string): string[] {
  const files: string[] = [];
  const walk = (current: string): void => {
    for (const entry of readdirSync(current, { withFileTypes: true })) {
      const path = join(current, entry.name);
      if (entry.isDirectory()) {
        walk(path);
      } else if (entry.isFile()) {
        files.push(tmRelativePosix(dir, path));
      }
    }
  };
  walk(dir);
  return files.sort();
}

/** Crockford's base32 alphabet: digits and uppercase letters without I, L, O and U. */
const CROCKFORD = '0123456789ABCDEFGHJKMNPQRSTVWXYZ';

/**
 * The hash in an emitted metadata file's name: the first 12 characters of the
 * SHA-256 of its bytes in Crockford's base32, most significant bit first.
 *
 * @param content - The file's bytes.
 * @returns The 12-character hash.
 */
export function tmContentHash(content: Uint8Array): string {
  let text = '';
  let buffer = 0;
  let bits = 0;
  for (const value of createHash('sha256').update(content).digest()) {
    buffer = ((buffer << 8) | value) & 0xffff;
    bits += 8;
    while (bits >= 5) {
      bits -= 5;
      text += CROCKFORD[(buffer >> bits) & 0x1f];
    }
    if (text.length >= 12) {
      break;
    }
  }
  return text.slice(0, 12);
}

/** The compressed sizes of some bytes, in bytes, at the levels the budgets are measured at. */
export interface TmCompressedSize {
  /** The gzip size at level 9. */
  readonly gzip: number;
  /** The Brotli size at quality 11, reported beside the gzip size. */
  readonly brotli: number;
}

/** Measures the gzip (level 9) and Brotli (quality 11) sizes of some bytes. */
export function tmCompressedSize(content: Uint8Array | string): TmCompressedSize {
  return {
    gzip: gzipSync(content, { level: 9 }).length,
    brotli: brotliCompressSync(content, { params: { [constants.BROTLI_PARAM_QUALITY]: 11 } }).length,
  };
}

// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

/**
 * The megabyte value a byte ceiling is announced with (the `maxMb` message
 * parameter).
 *
 * Whole-megabyte rounding misstates everything under a few MB: a
 * sub-megabyte ceiling reads "0 MB", and 512 KiB reads "1 MB" — twice the
 * real limit, so the user picks a file the guardrail then rejects. Two
 * significant digits state every realistic ceiling exactly enough. The
 * value stays a megabyte NUMBER rather than a unit-aware size string
 * because the messages carry their own " MB" literal.
 */
export function tmMaxMegabytes(bytes: number): number {
  const mb = bytes / (1024 * 1024);
  if (Number.isNaN(mb) || mb <= 0) {
    return 0;
  }
  // One decimal under 10 MB, plus one more per leading zero under 1 MB.
  const decimals = mb >= 10 ? 0 : mb >= 1 ? 1 : 1 - Math.floor(Math.log10(mb));
  const factor = 10 ** decimals;
  return Math.round(mb * factor) / factor;
}

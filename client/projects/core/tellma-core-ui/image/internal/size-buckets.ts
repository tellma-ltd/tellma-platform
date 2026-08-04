// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

/**
 * The rendition size buckets. Bucketing (vs exact px) keeps server
 * rendition counts bounded and cache keys stable across near-identical
 * layouts.
 */
const BUCKETS = [64, 128, 256, 512, 1024, 2048] as const;

/**
 * The smallest bucket that covers the box at the device pixel ratio
 * (capped at 2 — beyond that the bandwidth exceeds the visible gain).
 * Larger boxes than the top bucket get the top bucket.
 */
export function tmSizeBucket(width: number, height: number, devicePixelRatio: number): number {
  const target = Math.max(width, height) * Math.min(devicePixelRatio, 2);
  for (const bucket of BUCKETS) {
    if (bucket >= target) {
      return bucket;
    }
  }
  return BUCKETS[BUCKETS.length - 1];
}

/** The default size-hint URL: appends `?size=<bucket>` (or `&size=`). */
export function tmDefaultSrcForSize(src: string, size: number): string {
  // The query must land BEFORE any fragment — and a `?` inside the
  // fragment must not be mistaken for an existing query.
  const hashAt = src.indexOf('#');
  const base = hashAt === -1 ? src : src.slice(0, hashAt);
  const fragment = hashAt === -1 ? '' : src.slice(hashAt);
  return `${base}${base.includes('?') ? '&' : '?'}size=${size}${fragment}`;
}

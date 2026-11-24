// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

// The outdated-client verdict's build rules (spec 0024 §5.6): which side of a
// `412 client-outdated` or `404 route-not-found` is old, judged from the
// order of the two builds, which TmAppVersion.compare decides (spec 0026 §7),
// so the verdict and the About dialog's Reload alert order builds alike. Only
// the HTTP layer loads this file, with import(), at the first such answer: no
// first-frame call needs it, so it is a chunk of its own rather than part of
// the primary entry point. When the chunk cannot load, the layer treats the
// builds as unordered and the worker decides.

/**
 * The side the builds blame: `'same'` for the identical build refusing its
 * own contract, or `null` when they are unordered and the worker decides.
 */
export type TmBuildVerdict = 'same' | 'client-outdated' | 'server-outdated' | null;

/**
 * Judges an outdated answer from the builds: a newer server blames the
 * client; an older one blames the server, once per slot swap; anything else
 * is left to the worker.
 *
 * @param order - The server's build against the UI's: 1 newer, -1 older, 0
 *   the same build, `null` unordered.
 * @param build - The server's build, from `Tellma-Build`.
 * @param judgedOlder - When each older server build was first judged; updated here.
 * @param now - The current time, in epoch milliseconds.
 * @param windowMs - How long a build's first older verdict covers later ones: the retry's wait.
 * @returns The verdict, `'same'` for the identical build, or `null` for the worker to decide.
 */
export function tmBuildVerdict(
  order: -1 | 0 | 1 | null,
  build: string | null,
  judgedOlder: Map<string, number>,
  now: number,
  windowMs: number,
): TmBuildVerdict {
  if (order === 0) {
    return 'same';
  }
  if (order === 1) {
    return 'client-outdated';
  }
  if (order === -1 && build !== null) {
    // A verdict for the same build within the first one's retry window is the
    // same slot swap; a later one is a second verdict, as after a rollback,
    // and asks the worker.
    const first = judgedOlder.get(build);
    if (first === undefined) {
      judgedOlder.set(build, now);
      return 'server-outdated';
    }
    if (now - first < windowMs) {
      return 'server-outdated';
    }
  }
  return null;
}

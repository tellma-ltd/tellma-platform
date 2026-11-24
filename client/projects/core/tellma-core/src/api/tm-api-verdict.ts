// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

// The outdated-client verdict's build rules (spec 0024 §5.6): which side of a
// `412 client-outdated` or `404 route-not-found` is old, judged from the two
// builds. Only the HTTP layer loads this file, with import(), at the first
// such answer: no first-frame call needs it, so it is a chunk of its own
// rather than part of the primary entry point. When the chunk cannot load,
// the layer treats the builds as unordered and the worker decides.

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
 * @param ui - The UI's build, such as `1.4.0+3f9c2ab`.
 * @param build - The server's build, from `Tellma-Build`.
 * @param judgedOlder - When each older server build was first judged; updated here.
 * @param now - The current time, in epoch milliseconds.
 * @param windowMs - How long a build's first older verdict covers later ones: the retry's wait.
 * @returns The verdict, `'same'` for the identical build, or `null` for the worker to decide.
 */
export function tmBuildVerdict(
  ui: string,
  build: string | null,
  judgedOlder: Map<string, number>,
  now: number,
  windowMs: number,
): TmBuildVerdict {
  const order = tmCompareBuilds(ui, build);
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

/**
 * Orders a server build against the UI's build by SemVer 2.0 precedence,
 * which ignores the `+<sha>` build metadata.
 *
 * @param ui - The UI's build, such as `1.4.0+3f9c2ab`.
 * @param build - The server's build, from `Tellma-Build`.
 * @returns 1 when the server is newer, -1 when older, 0 for the identical
 *   build, and `null` when the two are unordered: the same version with
 *   another commit, or a value that is not a version.
 */
export function tmCompareBuilds(ui: string, build: string | null): -1 | 0 | 1 | null {
  if (build === ui) {
    return 0;
  }
  const a = parseVersion(ui);
  const b = parseVersion(build ?? '');
  if (a === null || b === null) {
    return null;
  }
  for (let i = 0; i < 3; i++) {
    if (a.core[i] !== b.core[i]) {
      return b.core[i] > a.core[i] ? 1 : -1;
    }
  }
  return comparePreRelease(a.pre, b.pre);
}

/** A parsed SemVer version: its three numbers and its pre-release identifiers. */
interface ParsedVersion {
  /** Major, minor and patch. */
  readonly core: readonly [number, number, number];
  /** The pre-release identifiers; empty for a release. */
  readonly pre: readonly string[];
}

/** SemVer 2.0: `X.Y.Z`, an optional `-pre.release`, an optional `+build`. */
const SEMVER = /^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)(?:-([0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*))?(?:\+[0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*)?$/;

/**
 * Parses a SemVer version.
 *
 * @param text - The version.
 * @returns The parsed version, or `null` when the text is not one.
 */
function parseVersion(text: string): ParsedVersion | null {
  const match = SEMVER.exec(text);
  return match === null
    ? null
    : { core: [Number(match[1]), Number(match[2]), Number(match[3])], pre: match[4]?.split('.') ?? [] };
}

/**
 * Orders the pre-release parts of two versions whose numbers are equal, as
 * the server's build against the UI's.
 *
 * @param ui - The UI's identifiers.
 * @param build - The server's identifiers.
 * @returns 1 when the server is newer, -1 when older, `null` when equal: the
 *   same version with another commit is unordered.
 */
function comparePreRelease(ui: readonly string[], build: readonly string[]): -1 | 1 | null {
  // A release outranks every pre-release of its version.
  if (ui.length === 0 || build.length === 0) {
    return ui.length === build.length ? null : ui.length === 0 ? -1 : 1;
  }
  for (let i = 0; i < Math.min(ui.length, build.length); i++) {
    const x = ui[i];
    const y = build[i];
    if (x === y) {
      continue;
    }
    const xNumeric = /^\d+$/.test(x);
    const yNumeric = /^\d+$/.test(y);
    // Numeric identifiers compare as numbers and rank below alphanumeric ones.
    if (xNumeric && yNumeric) {
      return Number(y) > Number(x) ? 1 : -1;
    }
    if (xNumeric !== yNumeric) {
      return xNumeric ? 1 : -1;
    }
    return y > x ? 1 : -1;
  }
  // A longer set of identifiers outranks its prefix.
  return ui.length === build.length ? null : build.length > ui.length ? 1 : -1;
}

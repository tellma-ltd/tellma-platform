// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { computed, inject, Injectable, signal, type Signal } from '@angular/core';

import { TmClientMetadataStore } from '../metadata/tm-client-metadata';

// The UI version's identity (spec 0026 §7). Client and server ship as one
// build, so the UI version is the distribution's informational version,
// `X.Y.Z[-pre]+<sha>`, the same value the server reports in `Tellma-Build`.
// Every member reads the client metadata through its store when asked, never
// through TM_CLIENT_METADATA: the request log reports a server build from the
// first response, which may land before the Core chunk is loaded. The order
// of two builds is SemVer 2.0 precedence, which ignores the build metadata;
// the HTTP layer judges an outdated answer by the same order (spec 0024 §5.6).

/** A SemVer 2.0 version: `X.Y.Z`, an optional `-pre.release`, an optional `+build`. */
const SEMVER = /^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)(?:-([0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*))?(?:\+([0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*))?$/;

/** A commit: the hexadecimal object name SourceLink appends, abbreviated to at least seven digits. */
const COMMIT = /^[0-9a-f]{7,40}$/i;

/** One feature's key and version, as the About dialog lists them. */
export interface TmFeatureVersion {
  /** The feature's key, such as `gl`. */
  readonly key: string;
  /** The feature's version. */
  readonly version: string;
}

/**
 * The identity of the running build: the UI version, its parts, the
 * platform's and the features' versions, and the last server build a
 * response reported. The session menu shows {@link TmAppVersion.display},
 * and the About dialog {@link TmAppVersion.label} with a Reload alert while
 * {@link TmAppVersion.serverNewer} holds. Every member is empty until the
 * client metadata loads, which the app does before its first render.
 *
 * @example
 * ```ts
 * const version = inject(TmAppVersion);
 * const reload = computed(() => version.serverNewer());
 * ```
 */
@Injectable({ providedIn: 'root' })
export class TmAppVersion {
  readonly #store = inject(TmClientMetadataStore);

  /** The last server build seen. */
  readonly #server = signal<string | null>(null);

  /** The last `Tellma-Build` a response reported, or `null` before the first. */
  readonly server: Signal<string | null> = this.#server.asReadonly();

  /** Whether the last server build seen is newer than this UI's: a reload would bring it. */
  readonly serverNewer: Signal<boolean> = computed(() => {
    const server = this.#server();
    return server !== null && this.compare(server) === 1;
  });

  /** The UI version, such as `1.4.0+3f9c2ab1e5d4…`: the distribution's informational version. */
  get ui(): string {
    return this.#store.peek()?.distribution.version ?? '';
  }

  /** The UI version without its build metadata, such as `1.4.0`, as the session menu shows it. */
  get display(): string {
    return this.ui.split('+', 1)[0];
  }

  /** The version with its abbreviated commit, such as `1.4.0 (3f9c2ab)`, or the version alone without a commit. */
  get label(): string {
    const commit = this.commit;
    return commit === null ? this.display : `${this.display} (${commit.slice(0, 7)})`;
  }

  /** The commit the UI was built from: its hexadecimal object name after the `+`, or `null` without one. */
  get commit(): string | null {
    const build = SEMVER.exec(this.ui)?.[5];
    return build !== undefined && COMMIT.test(build) ? build.toLowerCase() : null;
  }

  /** The platform's version that the distribution was built on. */
  get platform(): string {
    return this.#store.peek()?.distribution.platformVersion ?? '';
  }

  /** The composed features' keys and versions. */
  get features(): readonly TmFeatureVersion[] {
    return (this.#store.peek()?.features ?? []).map(({ key, version }) => ({ key, version }));
  }

  /**
   * Orders a build against this UI's by SemVer 2.0 precedence, which ignores
   * the `+<sha>` build metadata.
   *
   * @param build - Another build, such as a server's `Tellma-Build`.
   * @returns 1 when the build is newer, -1 when older, 0 for the same build,
   *   and `null` when the two are unordered: the same version from another
   *   commit, or a value that is not a version.
   */
  compare(build: string): -1 | 0 | 1 | null {
    const ui = this.ui;
    return ui === '' ? null : tmCompareBuilds(ui, build);
  }

  /**
   * Records the server build a response reported. Internal: the request log
   * calls it as each request ends.
   *
   * @param build - The response's `Tellma-Build`.
   */
  ɵserver(build: string): void {
    this.#server.set(build);
  }
}

/**
 * Orders a build against the UI's build by SemVer 2.0 precedence, which
 * ignores the `+<sha>` build metadata. Internal: {@link TmAppVersion.compare}
 * applies it to the running UI.
 *
 * @param ui - The UI's build, such as `1.4.0+3f9c2ab`.
 * @param build - The other build, such as the server's `Tellma-Build`.
 * @returns 1 when the build is newer, -1 when older, 0 for the identical
 *   build, and `null` when the two are unordered: the same version with
 *   another commit, or a value that is not a version.
 */
export function tmCompareBuilds(ui: string, build: string | null): -1 | 0 | 1 | null {
  if (build === ui) {
    return 0;
  }
  const a = SEMVER.exec(ui);
  const b = SEMVER.exec(build ?? '');
  if (a === null || b === null) {
    return null;
  }
  // --- Major, minor and patch, as numbers ---
  for (let i = 1; i <= 3; i++) {
    const x = Number(a[i]);
    const y = Number(b[i]);
    if (x !== y) {
      return y > x ? 1 : -1;
    }
  }
  // --- Then the pre-release identifiers ---
  return comparePreRelease(a[4]?.split('.') ?? [], b[4]?.split('.') ?? []);
}

/**
 * Orders the pre-release parts of two versions whose numbers are equal, as
 * the other build against the UI's.
 *
 * @param ui - The UI's identifiers.
 * @param build - The other build's identifiers.
 * @returns 1 when the build is newer, -1 when older, `null` when equal: the
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

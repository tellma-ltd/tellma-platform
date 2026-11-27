/**
 * The Vitest preset of a Tellma client workspace: the unit runner's reporters, with a watchdog
 * that ends a run which stops making progress and writes what was pending.
 *
 * The Angular unit-test builder reads it as its runner configuration. A workspace's
 * `vitest-base.config.mjs` re-exports the preset, and each test target names that file:
 *
 * ```js
 * // vitest-base.config.mjs
 * export { default } from '@tellma/core-tools/vitest';
 * ```
 *
 * ```json
 * "test": { "builder": "@angular/build:unit-test", "options": { "runnerConfig": "vitest-base.config.mjs" } }
 * ```
 *
 * @packageDocumentation
 */

// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { isAgent } from 'std-env';
import type { ViteUserConfig } from 'vitest/config';

import { TmVitestWatchdog, type TmVitestWatchdogOptions } from './watchdog.js';

export { TM_VITEST_EXIT_MS, TM_VITEST_STALL_MS, TmVitestWatchdog, type TmVitestWatchdogOptions } from './watchdog.js';

/** What the Vitest preset is configured with. */
export interface TmVitestConfigOptions {
  /** The watchdog's limits and where its report goes. */
  readonly watchdog?: TmVitestWatchdogOptions;
}

/**
 * The Vitest preset: Vitest's default reporters, then the watchdog.
 *
 * Naming any reporter replaces Vitest's defaults, so the preset names them again as Vitest picks
 * them itself: the agent reporter under a coding agent (as `std-env` detects one), else the default
 * reporter, then on GitHub Actions the annotations reporter.
 *
 * @param options - The watchdog's limits.
 * @returns The configuration, for the unit-test builder's `runnerConfig`.
 *
 * @example
 * ```js
 * // vitest-base.config.mjs: a two-minute stall limit
 * import { tmVitestConfig } from '@tellma/core-tools/vitest';
 *
 * export default tmVitestConfig({ watchdog: { stallMs: 120_000 } });
 * ```
 */
export function tmVitestConfig(options: TmVitestConfigOptions = {}): ViteUserConfig {
  // Vitest's own choice when no reporter is named, read from the same detection it uses.
  const defaults = [isAgent ? 'agent' : 'default'];
  if (process.env['GITHUB_ACTIONS'] === 'true') {
    defaults.push('github-actions');
  }
  return {
    test: {
      reporters: [...defaults, new TmVitestWatchdog(options.watchdog)],
    },
  };
}

/** The preset with the watchdog's default limits. */
const preset: ViteUserConfig = tmVitestConfig();

export default preset;

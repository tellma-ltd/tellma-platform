/**
 * The Vitest preset of a Tellma client workspace: the unit runner's reporters, with a watchdog
 * that ends a run which stops making progress and writes what was pending, and the restore of
 * every spy and stub before each test.
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
 * The Vitest preset: Vitest's default reporters, then the watchdog; and before each test, the
 * restore of every spy made with `vi.spyOn` and of every global and environment variable stubbed
 * with `vi.stubGlobal` or `vi.stubEnv`.
 *
 * Naming any reporter replaces Vitest's defaults, so the preset names them again as Vitest picks
 * them itself: the agent reporter under a coding agent (as `std-env` detects one), else the default
 * reporter, then on GitHub Actions the annotations reporter.
 *
 * The unit-test builder runs a browser project's files one after another in one page per worker
 * (`isolate: false`), and Vitest's browser tester, unlike its Node runner, restores no mock between
 * files. A spy one file leaves on a prototype, such as a mocked `HTMLAnchorElement.prototype.click`,
 * would otherwise stay in force for whichever files follow it in the same page, which depends on
 * how the run splits the files between its workers. A test therefore starts from the real
 * implementations, whatever ran before it in its page or its file.
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
      // Before each test: what an earlier test, or an earlier file in the same page, patched with
      // vi.spyOn, vi.stubGlobal or vi.stubEnv is put back, so no test inherits another's mocks.
      restoreMocks: true,
      unstubGlobals: true,
      unstubEnvs: true,
    },
  };
}

/** The preset with the watchdog's default limits. */
const preset: ViteUserConfig = tmVitestConfig();

export default preset;

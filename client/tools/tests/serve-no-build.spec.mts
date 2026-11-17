// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { spawnSync } from 'node:child_process';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';

import { describe, expect, it } from 'vitest';

// The CI-mode e2e web server runs `serve.mjs --prod --no-build`: it must never
// build, so that its startup timeout only ever times the server, and it must
// fail at once, not time out, when the bundle it should serve is missing.

/** The Angular workspace (client/), where serve.mjs runs. */
const clientDir = join(dirname(fileURLToPath(import.meta.url)), '..', '..');

/**
 * How long one serve.mjs invocation may take before the test gives up. Both
 * cases exit in about half a second; the margin absorbs a loaded machine.
 */
const RUN_LIMIT_MS = 60_000;

/** Runs serve.mjs with the given arguments and returns its exit code and output. */
function serve(...args: string[]): { status: number | null; output: string } {
  const run = spawnSync(process.execPath, [join('scripts', 'serve.mjs'), ...args], {
    cwd: clientDir,
    encoding: 'utf8',
    timeout: RUN_LIMIT_MS,
  });
  return { status: run.status, output: `${run.stdout}${run.stderr}` };
}

describe('serve.mjs --no-build', () => {
  it(
    'fails at once, without building, when the production bundle is missing',
    () => {
      // A project that has never been built, so no bundle exists for it.
      const { status, output } = serve('never-built-app', '--port', '1', '--prod', '--no-build');

      expect(status).toBe(1);
      expect(output).toContain('no production build of never-built-app');
      expect(output).not.toContain('building');
    },
    RUN_LIMIT_MS + 30_000,
  );

  it(
    'is refused without --prod, since the dev server always compiles',
    () => {
      const { status, output } = serve('showcase', '--port', '1', '--no-build');

      expect(status).toBe(1);
      expect(output).toContain('--no-build applies to --prod only');
    },
    RUN_LIMIT_MS + 30_000,
  );
});

// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

/**
 * Runs the Playwright suite with an OS-assigned (or .dev-ports.local) port
 * for the showcase web server, so parallel worktrees never collide (§1.3).
 * The port is handed to playwright.config.ts via the SHOWCASE_PORT env var;
 * the config's webServer starts `ng serve` on it.
 *
 * Locally the four browser projects run one AFTER another (CI, whose
 * runners are sized for it, keeps the single parallel invocation). Run
 * together, a headless Firefox or WebKit that loses the CPU stops
 * painting rather than merely slowing down, so Playwright's actionability
 * check never sees the two stable frames it waits for and a click on a
 * perfectly settled page times out. Sequential projects cost a few
 * minutes of wall-clock and buy a suite whose failures mean something.
 * Pass --project to run just one; the sequencing then steps aside.
 *
 * Every argument passes through to `playwright test` unchanged, which is how
 * the test tiers select their tests (--project, --grep, --grep-invert,
 * --pass-with-no-tests); CONTRIBUTING.md at the repository root gives the
 * exact PR-tier and nightly-tier invocations.
 *
 * Playwright runs in client/ whatever the caller's directory, so a relative
 * path argument (--output, a spec file) resolves against client/.
 *
 * Usage: node client/scripts/e2e.mjs [playwright args...]   (from the repository root)
 *        node scripts/e2e.mjs [playwright args...]          (from client/)
 */
import { spawn } from 'node:child_process';
import { createRequire } from 'node:module';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';

import { getPort } from './ports.mjs';

const clientDir = join(dirname(fileURLToPath(import.meta.url)), '..');
const require = createRequire(import.meta.url);
const playwrightCli = require.resolve('@playwright/test/cli', { paths: [clientDir] });

const port = await getPort('CLIENT_SHOWCASE_E2E');
const args = process.argv.slice(2);

/**
 * One Playwright invocation; resolves with its exit code. A run killed by a
 * signal (the OOM killer, a cancelled job) has no exit code and counts as a
 * failure, as does a runner that never starts: a suite that did not finish
 * must never read as green.
 */
function runPlaywright(extraArgs) {
  return new Promise((resolve) => {
    const child = spawn(
      process.execPath,
      [playwrightCli, 'test', '--config', 'e2e/playwright.config.ts', ...args, ...extraArgs],
      {
        cwd: clientDir,
        stdio: 'inherit',
        env: { ...process.env, SHOWCASE_PORT: String(port) },
      },
    );
    child.on('error', (error) => {
      console.error(`[e2e] could not start Playwright: ${error.message}`);
      resolve(1);
    });
    child.on('exit', (code) => resolve(code ?? 1));
  });
}

const PROJECTS = ['chromium', 'firefox', 'webkit', 'touch'];
const sequential = !process.env['CI'] && !args.some((arg) => arg.startsWith('--project'));

let failed = 0;
if (sequential) {
  for (const project of PROJECTS) {
    console.log(`
[e2e] ${project}`);
    failed = (await runPlaywright(['--project', project])) || failed;
  }
} else {
  failed = await runPlaywright([]);
}
process.exit(failed);

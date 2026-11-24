// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

/**
 * Runs the Acme browser suite. It picks the e2e host's port, ACME_WEB_E2E
 * from the worktree's .dev-ports.local or else one the OS assigns, so it
 * never collides with the development host on ACME_WEB nor with another
 * worktree, and hands it to playwright.config.ts as ACME_E2E_PORT. The
 * config's webServer then starts the e2e host (host/acme-e2e-host.mjs) on it.
 *
 * Every argument passes through to `playwright test` unchanged, which is how
 * the tiers select their tests: the pull-request tier runs
 * `--grep-invert "@nightly|@perf"` and the nightly tier `--grep "@nightly|@perf"`.
 *
 * Usage: pnpm --dir distributions/acme/test/Tellma.Distro.Acme.E2E run e2e [playwright args...]
 */
import { spawn } from 'node:child_process';
import { existsSync, readFileSync } from 'node:fs';
import { createRequire } from 'node:module';
import { createServer } from 'node:net';
import { dirname, join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import { parseEnv } from 'node:util';

const packageDir = resolve(dirname(fileURLToPath(import.meta.url)), '..');
const repoRoot = resolve(packageDir, '..', '..', '..', '..');
const require = createRequire(import.meta.url);
const playwrightCli = require.resolve('@playwright/test/cli', { paths: [packageDir] });

// --- The port: the worktree's, or the OS's ---
const portsFile = join(repoRoot, '.dev-ports.local');
const pinned = existsSync(portsFile)
  ? parseEnv(readFileSync(portsFile, 'utf8'))['ACME_WEB_E2E']
  : undefined;
const port = pinned !== undefined && /^\d+$/.test(pinned) ? Number(pinned) : await osAssignedPort();

// --- The run: Playwright in this package, its exit code the script's ---
// A run killed by a signal has no exit code and counts as a failure: a suite
// that did not finish must never read as green.
const child = spawn(process.execPath, [playwrightCli, 'test', ...process.argv.slice(2)], {
  cwd: packageDir,
  env: { ...process.env, ACME_E2E_PORT: String(port) },
  stdio: 'inherit',
});
child.on('exit', (code, signal) => process.exit(code ?? (signal ? 1 : 0)));

/** A port the OS reports free on the loopback interface. */
function osAssignedPort() {
  return new Promise((settle, reject) => {
    const server = createServer();
    server.once('error', reject);
    server.listen(0, '127.0.0.1', () => {
      const address = server.address();
      server.close(() =>
        settle(typeof address === 'object' && address !== null ? address.port : 0),
      );
    });
  });
}

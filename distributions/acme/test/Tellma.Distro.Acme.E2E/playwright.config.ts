// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { cpus } from 'node:os';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';

import { defineConfig, devices } from '@playwright/test';

/** This package's directory, which every path below is relative to. */
const packageDir = dirname(fileURLToPath(import.meta.url));

/**
 * The e2e host's port, which scripts/e2e.mjs picks (ACME_WEB_E2E from
 * .dev-ports.local, or one the OS assigns). Every worker loads this file
 * again, so the facts below must come from the environment or be fixed
 * paths, never chosen here.
 */
const port = Number(process.env['ACME_E2E_PORT']);
if (!Number.isInteger(port) || port <= 0) {
  throw new Error(
    'ACME_E2E_PORT is not set. Run the suite through `pnpm run e2e` (scripts/e2e.mjs).',
  );
}

// --- The host's facts, published to the host and to every worker's helpers ---
const origin = `https://localhost:${port}`;
process.env['TELLMA_E2E_ORIGIN'] = origin;
process.env['TELLMA_E2E_MAIL_DROP'] = join(packageDir, '.artifacts', 'host', 'mail');
process.env['TELLMA_E2E_STATE_DIR'] = join(packageDir, '.artifacts', 'state');

export default defineConfig({
  testDir: './specs',
  // A flow signs in, warms the app (the worker's install and the launch
  // documents) and then acts: a minute leaves room for the first sign-in of
  // a principal, which waits for its emailed code.
  timeout: 60_000,
  fullyParallel: true,
  forbidOnly: !!process.env['CI'],
  // CI retries a failed test once so the retry records a trace, and a test
  // that passes only on retry still fails the run: a flake must surface.
  retries: process.env['CI'] ? 1 : 0,
  failOnFlakyTests: !!process.env['CI'],
  // One host serves every worker; locally a third of the cores leaves the
  // host and the browsers room to run.
  workers: process.env['CI'] ? undefined : Math.max(2, Math.floor(cpus().length / 3)),
  reporter: [['html', { outputFolder: './.artifacts/report', open: 'never' }], ['list']],
  outputDir: './.artifacts/results',
  // Once the host is up: the Arabic project's principal reads Arabic.
  globalSetup: './support/global-setup.ts',
  use: {
    baseURL: origin,
    // The host serves Kestrel's development certificate, which no runner's browser trusts:
    // Chromium reads the OS store on Windows but its own NSS store on Linux, which the host
    // leaves alone.
    ignoreHTTPSErrors: true,
    // Ignoring the errors per context is not enough for the service worker: Chromium refuses to
    // register a worker whose script came with a certificate error unless the browser itself
    // runs with the switch, and a warm launch needs the worker.
    launchOptions: { args: ['--ignore-certificate-errors'] },
    trace: 'on-first-retry',
  },
  projects: [
    // Chromium runs every flow in English; the CDP-driven nightly checks need it too.
    { name: 'chromium', use: { ...devices['Desktop Chrome'], locale: 'en-US' } },
    // And every flow again in Arabic, as an Arabic reader's browser and account. The flows
    // that pick their own languages (responsive, per test; the right-to-left boot, Arabic)
    // run once, in the English project, and so do the nightly gates, which time one launch
    // whatever its language.
    {
      name: 'chromium-ar',
      use: { ...devices['Desktop Chrome'], locale: 'ar' },
      testIgnore: ['**/responsive.spec.ts', '**/rtl-boot.spec.ts', '**/nightly.spec.ts'],
    },
  ],
  webServer: {
    // The host migrates, builds the client and starts the web host; its
    // readiness is the web host's liveness probe.
    command: 'node host/acme-e2e-host.mjs',
    cwd: packageDir,
    url: `${origin}/health/live`,
    ignoreHTTPSErrors: true,
    // A fixed host per run: a server already on the port is another
    // worktree's or a stale one, never this run's.
    reuseExistingServer: false,
    // A cold client build takes about five minutes and a fresh database's
    // migration about one; twenty minutes stops a hung start in time.
    timeout: 20 * 60_000,
    gracefulShutdown: { signal: 'SIGTERM', timeout: 15_000 },
    stdout: 'pipe',
    stderr: 'pipe',
  },
});

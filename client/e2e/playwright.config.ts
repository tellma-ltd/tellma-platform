// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { cpus } from 'node:os';

import { defineConfig, devices } from '@playwright/test';

/**
 * The showcase port is supplied by scripts/e2e.mjs (from .dev-ports.local or
 * an OS-assigned free port — spec 0002 §1.3). No literal port ever appears
 * in the repo; run the suite via `pnpm run e2e`.
 */
const port = Number(process.env['SHOWCASE_PORT']);
if (!Number.isFinite(port) || port <= 0) {
  throw new Error('SHOWCASE_PORT is not set. Run the suite via `pnpm run e2e` (scripts/e2e.mjs).');
}

export default defineConfig({
  testDir: './specs',
  fullyParallel: true,
  forbidOnly: !!process.env['CI'],
  retries: process.env['CI'] ? 2 : 0,
  // Locally the battery runs four projects at once, and a headless
  // Firefox or WebKit that loses the CPU does not merely run slow — it
  // stops painting, so elements never reach "stable" and assertions fail
  // on a correct page. Playwright's default (half the cores) oversubscribes
  // a developer box that is also running a dev server and an editor; a
  // third of them leaves each engine room to render. CI sizes its own
  // runners and keeps the default.
  workers: process.env['CI'] ? undefined : Math.max(2, Math.floor(cpus().length / 3)),
  reporter: [['html', { outputFolder: '../.artifacts/e2e/report', open: 'never' }], ['list']],
  outputDir: '../.artifacts/e2e/results',
  use: {
    baseURL: `http://localhost:${port}`,
    trace: 'on-first-retry',
  },
  projects: [
    // Chromium runs the FULL battery (real-clipboard permissions, touch
    // specs excluded — those need a touch-enabled device project).
    {
      name: 'chromium',
      use: { ...devices['Desktop Chrome'] },
      testIgnore: /grid-touch|tooltip-touch/,
    },
    // Firefox/WebKit run the @cross-engine subset: tests that dispatch
    // synthetic ClipboardEvents (no OS clipboard, no Chromium-only
    // permissions), pinning the parse/serialize paths on every engine.
    {
      name: 'firefox',
      use: { ...devices['Desktop Firefox'] },
      grep: /@cross-engine/,
      // Same starvation, second symptom: a row can be in the DOM a beat
      // before its aria attributes are painted, so an assertion that is
      // merely CORRECT-eventually needs longer than the 5s default here.
      expect: { timeout: 15_000 },
      timeout: 60_000,
      // Headless Firefox starves its rendering pipeline when many instances
      // run in parallel, and Playwright's pre-click stability check then
      // times out on perfectly idle pages. Serializing within each file
      // caps the concurrent Firefox instances; the subset is small.
      fullyParallel: false,
    },
    {
      name: 'webkit',
      use: { ...devices['Desktop Safari'] },
      grep: /@cross-engine/,
      // Same accommodation as Firefox, and for the same reason: starved of
      // CPU, WebKit stops producing the consecutive stable frames
      // Playwright's actionability check waits for, so a click on a
      // perfectly settled page times out.
      fullyParallel: false,
      expect: { timeout: 15_000 },
      timeout: 60_000,
    },
    // The touch battery runs on a real coarse-pointer device descriptor
    // (chromium engine with touch + mobile emulation — no extra browser
    // install beyond chromium). Only the touch specs run here; chromium's
    // testIgnore keeps the same specs out of the desktop run.
    {
      name: 'touch',
      testMatch: /grid-touch|tooltip-touch/,
      use: { ...devices['Pixel 7'] },
    },
  ],
  webServer: {
    // CI serves an optimized production build: the dev server's latency
    // widens the grid's async post-click focus race, so synthetic key
    // presses intermittently land before focus settles and get dropped
    // (broad keyboard/clipboard flakiness). Local keeps `ng serve` for fast
    // rebuilds. The prod path folds a full build into startup, hence the
    // wider timeout.
    command: process.env['CI']
      ? `node scripts/serve.mjs showcase --port ${port} --prod`
      : `node scripts/serve.mjs showcase --port ${port}`,
    url: `http://localhost:${port}`,
    cwd: '..',
    reuseExistingServer: !process.env['CI'],
    timeout: process.env['CI'] ? 300_000 : 180_000,
  },
});

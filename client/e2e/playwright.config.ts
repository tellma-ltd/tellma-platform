// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

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
      testIgnore: /grid-touch/,
    },
    // Firefox/WebKit run the @cross-engine subset: tests that dispatch
    // synthetic ClipboardEvents (no OS clipboard, no Chromium-only
    // permissions), pinning the parse/serialize paths on every engine.
    {
      name: 'firefox',
      use: { ...devices['Desktop Firefox'] },
      grep: /@cross-engine/,
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
    },
    // The touch battery runs on a real coarse-pointer device descriptor
    // (chromium engine with touch + mobile emulation — no extra browser
    // install beyond chromium). Only /grid-touch/ runs here; chromium's
    // testIgnore keeps the same specs out of the desktop run.
    {
      name: 'touch',
      testMatch: /grid-touch/,
      use: { ...devices['Pixel 7'] },
    },
  ],
  webServer: {
    command: `node scripts/serve.mjs showcase --port ${port}`,
    url: `http://localhost:${port}`,
    cwd: '..',
    reuseExistingServer: !process.env['CI'],
    timeout: 180_000,
  },
});

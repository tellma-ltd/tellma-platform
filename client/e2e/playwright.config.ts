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
    {
      name: 'chromium',
      use: { ...devices['Desktop Chrome'] },
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

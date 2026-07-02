/**
 * Runs the Playwright suite with an OS-assigned (or .dev-ports.local) port
 * for the sandbox web server, so parallel worktrees never collide (§1.3).
 * The port is handed to playwright.config.ts via the SANDBOX_PORT env var;
 * the config's webServer starts `ng serve` on it.
 *
 * Usage: node scripts/e2e.mjs [playwright args...]
 */
import { spawn } from 'node:child_process';
import { createRequire } from 'node:module';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';

import { getPort } from './ports.mjs';

const clientDir = join(dirname(fileURLToPath(import.meta.url)), '..');
const require = createRequire(import.meta.url);
const playwrightCli = require.resolve('@playwright/test/cli', { paths: [clientDir] });

const port = await getPort('CLIENT_SANDBOX_E2E');
const child = spawn(
  process.execPath,
  [playwrightCli, 'test', '--config', 'e2e/playwright.config.ts', ...process.argv.slice(2)],
  {
    cwd: clientDir,
    stdio: 'inherit',
    env: { ...process.env, SANDBOX_PORT: String(port) },
  },
);
child.on('exit', (code) => process.exit(code ?? 0));

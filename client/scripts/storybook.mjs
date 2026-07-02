/**
 * Launches Storybook on a port-free basis (spec 0002 §1.3): the port comes
 * from .dev-ports.local (CLIENT_STORYBOOK) when present, else the OS.
 */
import { spawn } from 'node:child_process';
import { createRequire } from 'node:module';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';

import { getPort } from './ports.mjs';

const clientDir = join(dirname(fileURLToPath(import.meta.url)), '..');
const require = createRequire(import.meta.url);
const ng = require.resolve('@angular/cli/bin/ng.js', { paths: [clientDir] });

const port = await getPort('CLIENT_STORYBOOK');
console.log(`[storybook] http://localhost:${port}/`);
const child = spawn(
  process.execPath,
  [ng, 'run', 'sandbox:storybook', '--port', String(port), ...process.argv.slice(2)],
  { cwd: clientDir, stdio: 'inherit' },
);
child.on('exit', (code) => process.exit(code ?? 0));

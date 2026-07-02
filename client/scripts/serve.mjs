/**
 * Launches `ng serve` for a project (default: sandbox) on a port-free basis
 * per spec 0002 §1.3: the port comes from .dev-ports.local when present,
 * else the OS assigns one.
 *
 * Usage: node scripts/serve.mjs [project] [--port <n>]
 */
import { spawn } from 'node:child_process';
import { createRequire } from 'node:module';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';

import { getPort } from './ports.mjs';

const clientDir = join(dirname(fileURLToPath(import.meta.url)), '..');
const require = createRequire(import.meta.url);
const ng = require.resolve('@angular/cli/bin/ng.js', { paths: [clientDir] });

const args = process.argv.slice(2);
const portFlag = args.indexOf('--port');
const project = args.find((a) => !a.startsWith('--')) ?? 'sandbox';
const port =
  portFlag >= 0 ? Number(args[portFlag + 1]) : await getPort(`CLIENT_${project.toUpperCase().replaceAll('-', '_')}`);

console.log(`[serve] ${project} on http://localhost:${port}/`);
const child = spawn(process.execPath, [ng, 'serve', project, '--port', String(port)], {
  cwd: clientDir,
  stdio: 'inherit',
});
child.on('exit', (code) => process.exit(code ?? 0));

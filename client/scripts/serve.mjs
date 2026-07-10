/**
 * Launches `ng serve` for a project (default: showcase) on a port-free basis
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
// Positionals exclude flags AND flag values ('--port 4300' must not make
// '4300' the project).
const positionals = args.filter((a, i) => !a.startsWith('--') && (portFlag < 0 || i !== portFlag + 1));
const project = positionals[0] ?? 'showcase';
const port =
  portFlag >= 0 ? Number(args[portFlag + 1]) : await getPort(`CLIENT_${project.toUpperCase().replaceAll('-', '_')}`);

console.log(`[serve] ${project} on http://localhost:${port}/`);
const child = spawn(process.execPath, [ng, 'serve', project, '--port', String(port)], {
  cwd: clientDir,
  stdio: 'inherit',
});
child.on('exit', (code) => process.exit(code ?? 0));

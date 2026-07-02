/**
 * Builds every package in dependency order:
 * tokens → core-ui → testing → locale-ar (ng-packagr), then the MCP Node
 * package (tsc -b). Run from anywhere; paths resolve relative to this file.
 */
import { execFileSync } from 'node:child_process';
import { createRequire } from 'node:module';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';

const clientDir = join(dirname(fileURLToPath(import.meta.url)), '..');
const require = createRequire(import.meta.url);
const ng = require.resolve('@angular/cli/bin/ng.js', { paths: [clientDir] });
const tsc = require.resolve('typescript/bin/tsc', { paths: [clientDir] });

const run = (bin, args) =>
  execFileSync(process.execPath, [bin, ...args], { cwd: clientDir, stdio: 'inherit' });

const tsx = require.resolve('tsx/cli', { paths: [clientDir] });

// Generated assets first (token CSS + JSON schema ship as package assets).
run(tsx, ['tools/tokens/check.mts']);
run(tsx, ['tools/tokens/build-css.mts']);

for (const project of ['core-ui-tokens', 'core-ui', 'core-ui-testing', 'locale-ar']) {
  run(ng, ['build', project]);
}
run(tsc, ['-b', 'projects/core/tellma-core-ui-mcp']);

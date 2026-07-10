/**
 * Per-entry-point bundle budget (spec §8, DoD 9): measures each surface's
 * SELF-weight — its built FESM bundled with esbuild while the assumed app
 * baseline (@angular/* incl. cdk+aria, rxjs, tslib, @jsverse/*) AND the
 * other @tellma entry points stay external — then gzips and compares to the
 * concrete ceilings. The ceilings are RATCHETS: set to catch regressions,
 * inspected and tightened as real builds land, never loosened silently.
 *
 * Usage: node scripts/bundle-size.mjs   (after `pnpm run build`)
 */
import { gzipSync } from 'node:zlib';
import { createRequire } from 'node:module';
import { dirname, join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const clientDir = resolve(dirname(fileURLToPath(import.meta.url)), '..');
const require = createRequire(join(clientDir, 'package.json'));
const esbuild = require('esbuild');

/** Ceilings in KB gzipped (§8). */
const BUDGETS = [
  { name: '@tellma/core-ui/input', entry: 'dist/core-ui/fesm2022/tellma-core-ui-input.mjs', kb: 3 },
  {
    name: '@tellma/core-ui/checkbox',
    entry: 'dist/core-ui/fesm2022/tellma-core-ui-checkbox.mjs',
    kb: 4,
  },
  {
    name: '@tellma/core-ui/form-field',
    entry: 'dist/core-ui/fesm2022/tellma-core-ui-form-field.mjs',
    kb: 4,
  },
  {
    name: '@tellma/core-ui/select',
    entry: 'dist/core-ui/fesm2022/tellma-core-ui-select.mjs',
    kb: 8,
  },
  {
    name: '@tellma/core-ui-tokens',
    entry: 'dist/core-ui-tokens/fesm2022/tellma-core-ui-tokens.mjs',
    kb: 8,
  },
];

let failed = false;
const rows = [];

for (const budget of BUDGETS) {
  const result = await esbuild.build({
    entryPoints: [join(clientDir, budget.entry)],
    bundle: true,
    minify: true,
    format: 'esm',
    write: false,
    logLevel: 'silent',
    // The assumed app baseline (§8: counting CDK against tm-select would
    // double-count) + sibling @tellma entry points (measured on their own).
    external: ['@angular/*', 'rxjs', 'rxjs/*', 'tslib', '@jsverse/*', '@tellma/*'],
  });
  const gzipped = gzipSync(result.outputFiles[0].contents).length;
  const kb = gzipped / 1024;
  const over = kb > budget.kb;
  failed ||= over;
  rows.push({
    'entry point': budget.name,
    'gzipped (KB)': kb.toFixed(2),
    'ceiling (KB)': budget.kb,
    status: over ? 'OVER BUDGET' : 'ok',
  });
}

console.table(rows);
if (failed) {
  console.error('bundle-size gate FAILED — a surface exceeded its ceiling (§8).');
  process.exit(1);
}
console.log('bundle-size gate OK.');

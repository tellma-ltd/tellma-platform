// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

/**
 * Per-entry-point bundle budget (spec §8): measures each surface's
 * SELF-weight — its built FESM bundled with esbuild while the assumed app
 * baseline (@angular/* incl. cdk+aria, rxjs, tslib, @jsverse/*) AND the
 * other @tellma entry points stay external — then gzips and compares to the
 * ceilings each library declares in its package.json "tellma".budgetsInKb.
 * The ceilings are RATCHETS: set just above measured reality to catch
 * regressions, inspected and tightened as real builds land, never loosened
 * silently.
 *
 * Coverage is enforced both ways: a library that declares budgets must
 * cover EVERY discovered entry point, and every budget key must match a
 * real entry point. A library with no "tellma".budgetsInKb block is exempt by
 * decision — visible in its own package.json.
 *
 * Usage: node scripts/bundle-size.mjs   (after `pnpm run build`)
 */
import { gzipSync } from 'node:zlib';
import { createRequire } from 'node:module';
import { dirname, join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

import { discoverLibraries } from '../tools/workspace.mjs';

const clientDir = resolve(dirname(fileURLToPath(import.meta.url)), '..');
const require = createRequire(join(clientDir, 'package.json'));
const esbuild = require('esbuild');

let failed = false;
const rows = [];

for (const library of discoverLibraries(clientDir)) {
  const budgets = library.tellma?.budgetsInKb;
  if (!budgets) {
    continue;
  }
  const declared = new Set(Object.keys(budgets));
  for (const entryPoint of library.entryPoints) {
    const kb = budgets[entryPoint.id];
    if (kb === undefined) {
      console.error(
        `${library.name}: entry point '${entryPoint.id}' has no ceiling in "tellma".budgetsInKb — ` +
          `every entry point of a budgeted library must be covered.`,
      );
      failed = true;
      continue;
    }
    declared.delete(entryPoint.id);
    const result = await esbuild.build({
      entryPoints: [join(clientDir, entryPoint.fesm)],
      bundle: true,
      minify: true,
      format: 'esm',
      write: false,
      logLevel: 'silent',
      // The assumed app baseline (counting CDK against tm-select would
      // double-count) + sibling @tellma entry points (measured on their own).
      external: ['@angular/*', 'rxjs', 'rxjs/*', 'tslib', '@jsverse/*', '@tellma/*'],
    });
    const gzipped = gzipSync(result.outputFiles[0].contents).length / 1024;
    const over = gzipped > kb;
    failed ||= over;
    rows.push({
      'entry point': entryPoint.importPath,
      'gzipped (KB)': gzipped.toFixed(2),
      'ceiling (KB)': kb,
      status: over ? 'OVER BUDGET' : 'ok',
    });
  }
  for (const orphan of declared) {
    console.error(`${library.name}: "tellma".budgetsInKb declares '${orphan}', which is not an entry point.`);
    failed = true;
  }
}

console.table(rows);
if (failed) {
  console.error('bundle-size gate FAILED.');
  process.exit(1);
}
console.log('bundle-size gate OK.');

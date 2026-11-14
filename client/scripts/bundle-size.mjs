// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

/**
 * Per-entry-point bundle budget (spec §8): measures each surface's
 * SELF-weight as an app ships it — its built FESM and every chunk it loads
 * (`@defer` chunks included) linked by the Angular linker and bundled with
 * esbuild under the production options, while the assumed app baseline
 * (@angular/* incl. cdk+aria, rxjs, tslib, @jsverse/*) AND the other @tellma
 * entry points stay external (tools/bundle-measure.mjs says why it links
 * first, and fails on anything left unlinked) — then gzips and compares to
 * the ceilings each library declares in its package.json "tellma".budgetsInKb.
 * The ceilings are RATCHETS: set just above measured reality to catch
 * regressions, inspected and tightened as real builds land, never loosened
 * silently.
 *
 * A library may also declare "tellma".staticGraphExcludes: entry points
 * another entry point may load only with import(). The check bundles that
 * entry point with its sibling entry points resolved and fails when an
 * excluded one is reachable through static imports.
 *
 * A library may also declare "tellma".treeShakes: per entry point, an app
 * module's named imports and the strings its production bundle must not
 * contain — a component the app never imports has to fall away even though
 * it ships in the same FESM.
 *
 * Coverage is enforced both ways: a library that declares budgets must
 * cover EVERY discovered entry point, and every budget key must match a
 * real entry point. A library with no "tellma".budgetsInKb block is exempt by
 * decision — visible in its own package.json.
 *
 * Usage: node scripts/bundle-size.mjs   (after `pnpm run build`)
 */
import { createRequire } from 'node:module';
import { dirname, join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

import { createBundleMeasurer, createProductionBundler } from '../tools/bundle-measure.mjs';
import { discoverLibraries } from '../tools/workspace.mjs';

const clientDir = resolve(dirname(fileURLToPath(import.meta.url)), '..');
const require = createRequire(join(clientDir, 'package.json'));
const esbuild = require('esbuild');
const measure = await createBundleMeasurer(clientDir);
const bundle = await createProductionBundler(clientDir);

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
    // The linked production weight; the externals are the assumed app
    // baseline (counting CDK against tm-select would double-count) and the
    // sibling @tellma entry points (measured on their own).
    const gzipped = (await measure(join(clientDir, entryPoint.fesm))) / 1024;
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

// Section: lazy boundaries. A library's "tellma".staticGraphExcludes maps an
// entry point to the sibling entry points it may reach only through import():
// code a readonly page must never download. The budgets above measure each
// entry point with its siblings external, so they cannot see a static import
// that pulls one of those siblings back in through a third entry point. This
// check bundles the entry point WITH its library's siblings resolved to their
// built FESMs and code splitting on, walks the static imports from the entry
// output, and fails if any excluded sibling's code is reachable that way.
for (const library of discoverLibraries(clientDir)) {
  const excludes = library.tellma?.staticGraphExcludes;
  if (!excludes) {
    continue;
  }
  const fesmByPath = new Map(
    library.entryPoints.map((entryPoint) => [entryPoint.importPath, join(clientDir, entryPoint.fesm)]),
  );
  const fesmById = new Map(
    library.entryPoints.map((entryPoint) => [entryPoint.id, join(clientDir, entryPoint.fesm)]),
  );
  // Resolves the library's own entry points to their FESMs; everything else
  // stays external, as in the budget measurement.
  const siblings = {
    name: 'library-siblings',
    setup(build) {
      build.onResolve({ filter: /^@/ }, (args) => {
        const path = fesmByPath.get(args.path);
        return path === undefined ? { path: args.path, external: true } : { path };
      });
    },
  };
  for (const [id, excluded] of Object.entries(excludes)) {
    const entryFesm = fesmById.get(id);
    const banned = new Map(excluded.map((name) => [fesmById.get(name), name]));
    if (entryFesm === undefined || banned.has(undefined)) {
      console.error(`${library.name}: "tellma".staticGraphExcludes names an entry point that does not exist.`);
      failed = true;
      continue;
    }
    const result = await esbuild.build({
      entryPoints: [entryFesm],
      absWorkingDir: clientDir,
      bundle: true,
      format: 'esm',
      splitting: true,
      outdir: join(clientDir, '.tmp-static-graph'),
      write: false,
      metafile: true,
      logLevel: 'silent',
      external: ['@angular/*', 'rxjs', 'rxjs/*', 'tslib', '@jsverse/*'],
      plugins: [siblings],
    });
    const outputs = result.metafile.outputs;
    // Walk the entry output's static imports; dynamic ones are the boundary.
    const start = Object.keys(outputs).find((out) => outputs[out].entryPoint !== undefined);
    const seen = new Set();
    let reached = false;
    const queue = start === undefined ? [] : [start];
    while (queue.length > 0) {
      const out = queue.pop();
      if (seen.has(out)) {
        continue;
      }
      seen.add(out);
      for (const input of Object.keys(outputs[out].inputs)) {
        const name = banned.get(join(clientDir, input));
        if (name !== undefined) {
          console.error(
            `${library.name}: '${id}' reaches '${name}' through static imports — ` +
              `it may only load it with import().`,
          );
          reached = true;
        }
      }
      for (const imported of outputs[out].imports) {
        if (imported.kind !== 'dynamic-import' && outputs[imported.path] !== undefined) {
          queue.push(imported.path);
        }
      }
    }
    if (reached) {
      failed = true;
    } else {
      console.log(`${library.name}: '${id}' statically reaches none of ${excluded.join(', ')}.`);
    }
  }
}

// Section: tree shaking. A library's "tellma".treeShakes maps an entry point
// to the names an app imports from it and the strings (a component's
// selector) that app's production bundle must not contain. One FESM holds
// every component of an entry point, so the module-level `sideEffects`
// flag cannot drop an unused one; only the linker's pure definitions can,
// and a top-level side effect would silently keep it.
for (const library of discoverLibraries(clientDir)) {
  const checks = library.tellma?.treeShakes;
  if (!checks) {
    continue;
  }
  for (const [id, check] of Object.entries(checks)) {
    const entryPoint = library.entryPoints.find((candidate) => candidate.id === id);
    if (entryPoint === undefined) {
      console.error(`${library.name}: "tellma".treeShakes names '${id}', which is not an entry point.`);
      failed = true;
      continue;
    }
    const { text } = await bundle(join(clientDir, entryPoint.fesm), check.imports);
    const kept = check.absent.filter((needle) => text.includes(needle));
    if (kept.length > 0) {
      console.error(
        `${library.name}: an app importing only ${check.imports.join(', ')} from '${id}' ` +
          `still ships ${kept.join(', ')}.`,
      );
      failed = true;
    } else {
      console.log(
        `${library.name}: an app importing only ${check.imports.join(', ')} from '${id}' ` +
          `ships none of ${check.absent.join(', ')}.`,
      );
    }
  }
}

if (failed) {
  console.error('bundle-size gate FAILED.');
  process.exit(1);
}
console.log('bundle-size gate OK.');

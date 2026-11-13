// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

/**
 * The size gate's measurement: what an entry point adds to a production app.
 *
 * A published FESM (a Node package's compiled module) is partially compiled.
 * Every component and directive carries its decorator arguments twice: once
 * in its `ɵɵngDeclare*` definition and once more in `ɵɵngDeclareClassMetadata`,
 * which exists only for TestBed. An app's build runs the Angular linker over
 * the package, which turns the second into `ɵsetClassMetadata` behind an
 * `ngDevMode` guard, and the production build drops it with every other
 * dev-mode branch. Measuring the FESM as published would count that copy,
 * and gzip folds it only while the two copies lie within its 32 KB window,
 * so the measure would jump by kilobytes whenever a template and stylesheet
 * grew past that distance, with nothing a user downloads changing.
 *
 * So the measure links the FESM first, as an app build does, then bundles it
 * with esbuild with `ngDevMode` and the other build-time flags set to false,
 * minifies and gzips it. The app baseline (@angular/* including cdk and aria,
 * rxjs, tslib, @jsverse/*) and the sibling @tellma entry points stay
 * external, so each entry point is measured on its own weight.
 */
import { readFileSync } from 'node:fs';
import { createRequire } from 'node:module';
import { dirname, join } from 'node:path';
import { gzipSync } from 'node:zlib';
import { pathToFileURL } from 'node:url';

/** The imports every measure leaves out: the app baseline and sibling entry points. */
export const BUNDLE_EXTERNALS = [
  '@angular/*',
  'rxjs',
  'rxjs/*',
  'tslib',
  '@jsverse/*',
  '@tellma/*',
];

/** The build-time flags an Angular production build sets, which strip dev-only code. */
const PRODUCTION_DEFINES = { ngDevMode: 'false', ngJitMode: 'false', ngI18nClosureMode: 'false' };

/**
 * Creates the measurer for a client workspace: it resolves esbuild from the
 * workspace, and Babel and the Angular linker through @angular/compiler-cli,
 * which depends on Babel for the same linking an app build performs.
 *
 * @param {string} clientDir absolute path of the client workspace
 * @returns {Promise<(fesm: string) => Promise<number>>} gzipped bytes of one
 *   FESM, given its absolute path
 */
export async function createBundleMeasurer(clientDir) {
  const require = createRequire(join(clientDir, 'package.json'));
  const esbuild = require('esbuild');
  const compilerCli = require.resolve('@angular/compiler-cli/package.json');
  const babel = createRequire(compilerCli)('@babel/core');
  const { default: linkerPlugin } = await import(
    pathToFileURL(require.resolve('@angular/compiler-cli/linker/babel')).href
  );

  return async (fesm) => {
    // Link the partial declarations, as an app's build does.
    const source = readFileSync(fesm, 'utf8');
    const linked = await babel.transformAsync(source, {
      filename: fesm,
      configFile: false,
      babelrc: false,
      compact: false,
      sourceMaps: false,
      plugins: [linkerPlugin],
    });
    // Bundle and minify with the production flags, so dev-mode branches
    // (the class metadata among them) fall away.
    const result = await esbuild.build({
      stdin: { contents: linked.code, resolveDir: dirname(fesm), loader: 'js' },
      bundle: true,
      minify: true,
      format: 'esm',
      write: false,
      logLevel: 'silent',
      define: PRODUCTION_DEFINES,
      external: BUNDLE_EXTERNALS,
    });
    return gzipSync(result.outputFiles[0].contents).length;
  };
}

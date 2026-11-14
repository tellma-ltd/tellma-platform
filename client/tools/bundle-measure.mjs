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
 * So the measure bundles the FESM with esbuild, linking every module it
 * loads as an app build does (the entry and its `@defer` chunks alike), with
 * an app build's production options: `ngDevMode` and the other build-time
 * flags false, `forwardRef` pure and license comments dropped. It then
 * minifies and gzips the bundle. The app baseline (@angular/* including cdk
 * and aria, rxjs, tslib, @jsverse/*) and the sibling @tellma entry points
 * stay external, so each entry point is measured on its own weight. A
 * declaration left unlinked in the output fails the measure, so a module the
 * linker missed can never be counted with its metadata copy.
 *
 * The measure approximates an Angular production build rather than running
 * one: it leaves out `@angular/build`'s own Babel optimizations (pure
 * annotations, metadata elision, enum and static-member wrapping), which are
 * not a public API. Against a build that runs them, it reads up to about
 * 0.1 KB lower per entry point, the most on the largest.
 */
import { readFileSync } from 'node:fs';
import { createRequire } from 'node:module';
import { join } from 'node:path';
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
const PRODUCTION_DEFINES = {
  ngDevMode: 'false',
  ngJitMode: 'false',
  ngServerMode: 'false',
  ngHmrMode: 'false',
  ngI18nClosureMode: 'false',
};

// A partial declaration's name, plain or with its `ɵ` prefix escaped.
const UNLINKED = /ngDeclare/;

/**
 * Creates the measurer for a client workspace: it resolves esbuild from the
 * workspace, and Babel and the Angular linker through @angular/compiler-cli,
 * which depends on Babel for the same linking an app build performs.
 *
 * @param {string} clientDir absolute path of the client workspace
 * @returns {Promise<(fesm: string) => Promise<number>>} gzipped bytes of one
 *   FESM, given its absolute path; rejects when the bundle still holds an
 *   unlinked declaration
 */
export async function createBundleMeasurer(clientDir) {
  const require = createRequire(join(clientDir, 'package.json'));
  const esbuild = require('esbuild');
  const compilerCli = require.resolve('@angular/compiler-cli/package.json');
  const babel = createRequire(compilerCli)('@babel/core');
  const { default: linkerPlugin } = await import(
    pathToFileURL(require.resolve('@angular/compiler-cli/linker/babel')).href
  );

  // Links every module esbuild loads that holds a partial declaration: the
  // entry FESM and the chunks it imports, `@defer` chunks included.
  const link = {
    name: 'tellma-link',
    setup(build) {
      build.onLoad({ filter: /\.m?js$/ }, async (args) => {
        const source = readFileSync(args.path, 'utf8');
        if (!source.includes('ɵɵngDeclare')) {
          return { contents: source, loader: 'js' };
        }
        const linked = await babel.transformAsync(source, {
          filename: args.path,
          configFile: false,
          babelrc: false,
          compact: false,
          sourceMaps: false,
          plugins: [linkerPlugin],
        });
        return { contents: linked.code, loader: 'js' };
      });
    },
  };

  return async (fesm) => {
    // Bundle and minify with the production options, so dev-mode branches
    // (the class metadata among them) fall away.
    const result = await esbuild.build({
      entryPoints: [fesm],
      bundle: true,
      minify: true,
      format: 'esm',
      write: false,
      logLevel: 'silent',
      legalComments: 'none',
      pure: ['forwardRef'],
      define: PRODUCTION_DEFINES,
      external: BUNDLE_EXTERNALS,
      plugins: [link],
    });
    const output = result.outputFiles[0];
    // Fail loudly rather than count a metadata copy the linker never saw.
    if (UNLINKED.test(output.text)) {
      throw new Error(
        `bundle-measure: ${fesm} still holds an unlinked Angular declaration after linking.`,
      );
    }
    return gzipSync(output.contents).length;
  };
}

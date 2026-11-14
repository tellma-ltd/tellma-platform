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
  const bundle = await createProductionBundler(clientDir);
  return async (fesm) => gzipSync((await bundle(fesm)).contents).length;
}

/**
 * Creates the production bundler the measure runs on (see the file
 * comment): it links, bundles and minifies one FESM with the app baseline
 * and the sibling entry points external, and fails when a declaration is
 * left unlinked.
 *
 * With `imports`, the bundle's entry is an app module that imports only
 * those names from the FESM, so the output is what an app using just them
 * ships — the tree-shaking check reads it. That bundle also runs the
 * optimizations the measure leaves out, because they are what make an
 * unused component removable at all: a linked component is a class whose
 * static definitions call into @angular/core, which a bundler must keep
 * unless the class is wrapped in a pure expression, and that wrapping is
 * `@angular/build`'s. Its plugins are loaded from the installed package by
 * file path (they are not exported), with the options a production build
 * gives a `sideEffects: false` library; an upgrade that moves them fails
 * the check loudly instead of letting it pass on a weaker pipeline.
 *
 * @param {string} clientDir absolute path of the client workspace
 * @returns {Promise<(fesm: string, imports?: readonly string[]) =>
 *   Promise<{ text: string; contents: Uint8Array }>>} the minified bundle
 */
export async function createProductionBundler(clientDir) {
  const require = createRequire(join(clientDir, 'package.json'));
  const esbuild = require('esbuild');
  const compilerCli = require.resolve('@angular/compiler-cli/package.json');
  const babel = createRequire(compilerCli)('@babel/core');
  const { default: linkerPlugin } = await import(
    pathToFileURL(require.resolve('@angular/compiler-cli/linker/babel')).href
  );

  // The production build's advanced optimizations, loaded on first use: the
  // plugin list an app build runs over a `sideEffects: false` package.
  let optimizations = null;
  const advancedOptimizations = () => {
    if (optimizations === null) {
      const buildPackage = require.resolve('@angular/build/package.json');
      const plugins = createRequire(buildPackage)('./src/tools/babel/plugins/index.js');
      const { markTopLevelPure, elideAngularMetadata, adjustTypeScriptEnums, adjustStaticMembers } =
        plugins;
      if (
        ![markTopLevelPure, elideAngularMetadata, adjustTypeScriptEnums, adjustStaticMembers].every(
          (plugin) => typeof plugin === 'function',
        )
      ) {
        throw new Error(
          'bundle-measure: @angular/build no longer exposes its optimization plugins where the tree-shaking check loads them.',
        );
      }
      optimizations = [
        [markTopLevelPure, { topLevelSafeMode: true }],
        elideAngularMetadata,
        adjustTypeScriptEnums,
        [adjustStaticMembers, { wrapDecorators: true }],
      ];
    }
    return optimizations;
  };

  // Links every module esbuild loads that holds a partial declaration: the
  // entry FESM and the chunks it imports, `@defer` chunks included. With
  // `optimize`, every module also runs the production build's optimizations.
  const link = (optimize) => ({
    name: 'tellma-link',
    setup(build) {
      build.onLoad({ filter: /\.m?js$/ }, async (args) => {
        const source = readFileSync(args.path, 'utf8');
        const linking = source.includes('ɵɵngDeclare');
        if (!linking && !optimize) {
          return { contents: source, loader: 'js' };
        }
        const plugins = [
          ...(linking ? [linkerPlugin] : []),
          ...(optimize ? advancedOptimizations() : []),
        ];
        const transformed = await babel.transformAsync(source, {
          filename: args.path,
          configFile: false,
          babelrc: false,
          compact: false,
          sourceMaps: false,
          plugins,
        });
        return { contents: transformed.code, loader: 'js' };
      });
    },
  });

  return async (fesm, imports) => {
    // The entry: the FESM itself, or an app module that imports only the
    // given names from it and keeps them alive.
    const entry =
      imports === undefined
        ? { entryPoints: [fesm] }
        : {
            stdin: {
              contents:
                `import { ${imports.join(', ')} } from ${JSON.stringify(fesm)};\n` +
                `globalThis.tmKeep = [${imports.join(', ')}];\n`,
              resolveDir: dirname(fesm),
              loader: 'js',
            },
          };
    // Bundle and minify with the production options, so dev-mode branches
    // (the class metadata among them) fall away.
    const result = await esbuild.build({
      ...entry,
      bundle: true,
      minify: true,
      format: 'esm',
      write: false,
      logLevel: 'silent',
      legalComments: 'none',
      pure: ['forwardRef'],
      define: PRODUCTION_DEFINES,
      external: BUNDLE_EXTERNALS,
      plugins: [link(imports !== undefined)],
    });
    const output = result.outputFiles[0];
    // Fail loudly rather than count a metadata copy the linker never saw.
    if (UNLINKED.test(output.text)) {
      throw new Error(
        `bundle-measure: ${fesm} still holds an unlinked Angular declaration after linking.`,
      );
    }
    return { text: output.text, contents: output.contents };
  };
}

// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

// Spec 0024 §4.2: the generated tm-boot.js.

/**
 * The boot script template: bundles the boot script's
 * TypeScript entry in `@tellma/core` into one classic, minified script. The
 * template opens with the configuration marker, `var TM_BOOT=/*TM_BOOT*\/null;`,
 * which a distribution's build replaces with its boot configuration.
 */
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';

import { build } from 'esbuild';

const clientDir = join(dirname(fileURLToPath(import.meta.url)), '..', '..');

/** The `@tellma/core` package folder. */
export const coreDir = join(clientDir, 'projects', 'core', 'tellma-core');

/** The boot script's TypeScript entry. */
export const bootEntry = join(coreDir, 'src', 'boot', 'tm-boot.script.ts');

/** Where the package build expects the template: a package asset, generated and not checked in. */
export const bootTemplateFile = join(coreDir, 'boot', 'tm-boot.template.js');

/** The template's first line: the configuration marker the distribution's build replaces. */
export const bootTemplateBanner = 'var TM_BOOT=/*TM_BOOT*/null;';

/**
 * Bundles the boot script into the template's text.
 *
 * The output is an IIFE, so it declares no global but the banner's
 * `TM_BOOT`. It targets ES2017 so that a browser too old for the app still
 * parses it and reaches the feature gate, which shows the unsupported page
 * instead of a blank one.
 *
 * @returns The template's text.
 */
export async function buildBootTemplate(): Promise<string> {
  const result = await build({
    entryPoints: [bootEntry],
    bundle: true,
    write: false,
    format: 'iife',
    platform: 'browser',
    target: 'es2017',
    minify: true,
    legalComments: 'none',
    charset: 'utf8',
    banner: { js: bootTemplateBanner },
    logLevel: 'silent',
  });
  const [output] = result.outputFiles;
  if (output === undefined || result.outputFiles.length !== 1) {
    throw new Error('The boot script bundle must be exactly one file.');
  }
  return output.text;
}

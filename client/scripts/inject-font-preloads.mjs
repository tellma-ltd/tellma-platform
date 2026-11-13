// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

/**
 * Post-build font preloading for the workspace's own applications (the
 * showcase): injects `<link rel="preload" as="font" crossorigin>` into a
 * built index.html for each woff2 the built stylesheets reference whose name
 * matches a fragment. The logic lives in @tellma/core-tools/fonts, which a
 * distribution's `tellma-client build` runs on its own output.
 *
 * By default only the Latin faces are preloaded (the universal fallback);
 * other scripts stay on demand via their unicode-range. Pass additional
 * name fragments to preload more, e.g. for an Arabic-first application:
 *
 *   node scripts/inject-font-preloads.mjs dist/showcase/browser arabic
 */
import { tmInjectFontPreloads } from '@tellma/core-tools/fonts';

const [browserDir, ...extraFragments] = process.argv.slice(2);
if (!browserDir) {
  console.error('Usage: node scripts/inject-font-preloads.mjs <browser-output-dir> [fragment...]');
  process.exit(1);
}

try {
  const urls = tmInjectFontPreloads(browserDir, ['latin', ...extraFragments.map((fragment) => fragment.toLowerCase())]);
  console.log(`inject-font-preloads: preloaded ${urls.length} font(s) in ${browserDir}/index.html`);
  for (const url of urls) {
    console.log(`  ${url}`);
  }
} catch (error) {
  console.error(`inject-font-preloads: ${error instanceof Error ? error.message : String(error)}`);
  process.exit(1);
}

// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

/**
 * Post-build font preloading: scans the emitted stylesheets of a browser
 * build for fingerprinted woff2 URLs and injects the matching
 * `<link rel="preload" as="font" crossorigin>` tags into the built
 * index.html. Fonts otherwise flow through the regular build pipeline
 * untouched — this step only surfaces what the build already emitted, so
 * preload hrefs can never drift from the real URLs.
 *
 * By default only the Latin faces are preloaded (the universal fallback);
 * other scripts stay on-demand via their unicode-range. Pass additional
 * name fragments to preload more, e.g. for an Arabic-first distribution:
 *
 *   node scripts/inject-font-preloads.mjs dist/showcase/browser arabic
 */
import { readdirSync, readFileSync, writeFileSync } from 'node:fs';
import { join } from 'node:path';

const [browserDir, ...extraFragments] = process.argv.slice(2);
if (!browserDir) {
  console.error('Usage: node scripts/inject-font-preloads.mjs <browser-output-dir> [fragment...]');
  process.exit(1);
}
const fragments = ['latin', ...extraFragments.map((f) => f.toLowerCase())];

const cssFiles = readdirSync(browserDir).filter((f) => f.endsWith('.css'));
const urls = new Set();
for (const file of cssFiles) {
  const css = readFileSync(join(browserDir, file), 'utf8');
  for (const match of css.matchAll(/url\(["']?(?:\.\/)?(media\/[^)"']+\.woff2)["']?\)/g)) {
    const url = match[1];
    if (fragments.some((fragment) => url.toLowerCase().includes(fragment))) {
      urls.add(url);
    }
  }
}

if (urls.size === 0) {
  console.error(`inject-font-preloads: no matching woff2 URLs found in ${browserDir}/*.css`);
  process.exit(1);
}

const indexPath = join(browserDir, 'index.html');
const html = readFileSync(indexPath, 'utf8');
// crossorigin is required: font fetches are CORS-mode even same-origin, and
// a mode mismatch would double-download instead of reusing the preload.
// Idempotent: URLs already preloaded (a re-run) are skipped.
const links = [...urls]
  .filter((url) => !html.includes(`href="${url}"`))
  .sort()
  .map((url) => `<link rel="preload" href="${url}" as="font" type="font/woff2" crossorigin>`)
  .join('');
writeFileSync(indexPath, html.replace('</head>', `${links}</head>`));

console.log(`inject-font-preloads: injected ${urls.size} preload link(s) into ${indexPath}`);
for (const url of [...urls].sort()) {
  console.log(`  ${url}`);
}

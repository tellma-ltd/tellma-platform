// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

/**
 * fonts:copy-identity — refreshes the identity server's committed font assets
 * from the client workspace: the composed stylesheet, every woff2 it names, and
 * the license files. Run this after re-vendoring any font pack; `tokens:check`
 * fails if the committed copies drift from what this would produce.
 *
 * Usage: node --experimental-strip-types tools/fonts/copy-identity-fonts.mts
 */
import { copyFileSync, existsSync, mkdirSync, readFileSync, writeFileSync } from 'node:fs';
import { basename, dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';

import { composeIdentityFontsCss, identityFontFiles, identityFontSources } from './identity-fonts.mjs';

const clientDir = join(dirname(fileURLToPath(import.meta.url)), '..', '..');
const outDir = join(clientDir, '..', 'src', 'apps', 'Tellma.Identity', 'wwwroot', 'fonts');
mkdirSync(outDir, { recursive: true });

// The stylesheet: written only on a real change, so an identical rewrite does
// not move the file's mtime (a build input for the static e2e server).
const cssPath = join(outDir, 'fonts.css');
const css = composeIdentityFontsCss();
if (!existsSync(cssPath) || readFileSync(cssPath, 'utf8') !== css) {
  writeFileSync(cssPath, css);
}

for (const file of identityFontFiles()) {
  copyFileSync(file, join(outDir, basename(file)));
}

// One license file per source: the OFL body is identical, but each names the
// families it covers, and shipping a font obliges us to ship its license.
for (const [index, source] of identityFontSources.entries()) {
  const name = index === 0 ? 'OFL.txt' : `OFL.${basename(dirname(source.dir))}.txt`;
  copyFileSync(join(source.dir, 'OFL.txt'), join(outDir, name));
}

console.log(`fonts:copy-identity OK -> ${outDir}`);

// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

/**
 * identity:copy-assets — refreshes every asset the identity server commits a copy
 * of, because its .NET build cannot run this workspace's emitters: the emitted
 * design tokens, the composed font stylesheet, every woff2 it names, and the font
 * licenses. Run it after changing tokens or re-vendoring a font pack; the
 * tokens:check gate fails when a committed copy drifts from what this produces.
 *
 * Usage: node --experimental-strip-types tools/fonts/copy-identity-assets.mts
 */
import { copyFileSync, existsSync, mkdirSync, readFileSync, writeFileSync } from 'node:fs';
import { basename, dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';

import { tmEmitCss, tmTokensDefault } from '@tellma/core-ui-tokens';

import { composeIdentityFontsCss, identityFontFiles, identityFontSources } from './identity-fonts.mjs';

const clientDir = join(dirname(fileURLToPath(import.meta.url)), '..', '..');
const wwwroot = join(clientDir, '..', 'src', 'apps', 'Tellma.Identity', 'wwwroot');
const outDir = join(wwwroot, 'fonts');
mkdirSync(outDir, { recursive: true });
mkdirSync(join(wwwroot, 'css'), { recursive: true });

/** Writes only on a real change, so an identical rewrite does not move the file's mtime. */
function writeIfChanged(path: string, content: string): void {
  if (!existsSync(path) || readFileSync(path, 'utf8') !== content) {
    writeFileSync(path, content);
  }
}

// The emitted design tokens.
writeIfChanged(join(wwwroot, 'css', 'tokens.css'), tmEmitCss(tmTokensDefault));

// The composed font stylesheet.
writeIfChanged(join(outDir, 'fonts.css'), composeIdentityFontsCss());

for (const file of identityFontFiles()) {
  copyFileSync(file, join(outDir, basename(file)));
}

// One license file per source: the OFL body is identical, but each names the
// families it covers, and shipping a font obliges us to ship its license.
for (const [index, source] of identityFontSources.entries()) {
  const name = index === 0 ? 'OFL.txt' : `OFL.${basename(dirname(source.dir))}.txt`;
  copyFileSync(join(source.dir, 'OFL.txt'), join(outDir, name));
}

console.log(`identity:copy-assets OK -> ${wwwroot}`);

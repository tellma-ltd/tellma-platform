// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

/**
 * tokens:check — the build gate (§4, DoD 9):
 *   1. zod-parses the default preset (schema gate),
 *   2. runs the missing-ref gate (both schemes + the :lang() leading map),
 *   3. checks every var() the library stylesheets read against what the
 *      emitter actually produces (the dangling-var gate),
 *   4. emits the generated JSON Schema into the package's assets,
 *   5. verifies the identity server's committed copies (the emitted tokens
 *      stylesheet, the composed fonts.css and every woff2 it names) match this
 *      workspace's output — the .NET build cannot run the emitter, so those
 *      copies are committed and this gate is what keeps them from drifting.
 * Exits non-zero on any issue. Color-contrast accessibility is covered by
 * the axe browser battery, not here.
 */
import { existsSync, globSync, mkdirSync, readFileSync, writeFileSync } from 'node:fs';
import { basename, dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';

import { composeIdentityFontsCss, identityFontFiles } from '../fonts/identity-fonts.mjs';
import { z } from 'zod';

import { tmEmitCss, tmTokensDefault, tmValidateTokens } from '@tellma/core-ui-tokens';
import { tmCheckCssRefs } from './css-refs.mjs';
import { tmTokensZodSchema } from './zod-schema.mjs';

const clientDir = join(dirname(fileURLToPath(import.meta.url)), '..', '..');
const packageDir = join(clientDir, 'projects', 'core', 'tellma-core-ui-tokens');

// 1. Schema gate.
const parsed = tmTokensZodSchema.safeParse(tmTokensDefault);
if (!parsed.success) {
  console.error('tokens:check FAILED — preset does not match the schema:');
  console.error(z.prettifyError(parsed.error));
  process.exit(1);
}

// 2. Missing-ref gate.
const issues = tmValidateTokens(tmTokensDefault);
if (issues.length > 0) {
  console.error(`tokens:check FAILED — ${issues.length} issue(s):`);
  for (const issue of issues) {
    console.error(`  [${issue.gate}] ${issue.message}`);
  }
  process.exit(1);
}

// 3. Dangling-var gate: a name that misses is not a fallback — CSS drops the
//    whole declaration, so the rule silently stops applying.
const stylesheets = [
  ...globSync(join(clientDir, 'projects', '**', '*.css')).filter(
    (file) => !file.includes('tellma-core-ui-tokens'),
  ),
  // The identity server consumes the same emitted vocabulary but lives outside the client
  // workspace, so it would otherwise be the one consumer a renamed token could break in silence.
  join(clientDir, '..', 'src', 'apps', 'Tellma.Identity', 'wwwroot', 'css', 'identity.css'),
];
const dangling = tmCheckCssRefs(tmEmitCss(tmTokensDefault), stylesheets);
if (dangling.length > 0) {
  console.error(`tokens:check FAILED — ${dangling.length} dangling var() reference(s):`);
  for (const problem of dangling) {
    console.error(`  ${problem}`);
  }
  process.exit(1);
}

// 4. Generated JSON Schema (shipped as a package asset).
const jsonSchema = z.toJSONSchema(tmTokensZodSchema, { target: 'draft-7' });
const outDir = join(packageDir, 'generated');
mkdirSync(outDir, { recursive: true });
writeFileSync(join(outDir, 'tm-tokens.schema.json'), JSON.stringify(jsonSchema, null, 2) + '\n');

// 5. Identity-server copy gate: the RCL commits the emitted stylesheet, the composed
// font stylesheet and the woff2 files because its build has no Node toolchain.
// `pnpm run identity:copy-assets` regenerates all of them.
const identityWwwroot = join(clientDir, '..', 'src', 'apps', 'Tellma.Identity', 'wwwroot');
const copies: Array<{ name: string; expected: string; actual: string }> = [
  {
    name: 'src/apps/Tellma.Identity/wwwroot/css/tokens.css',
    expected: tmEmitCss(tmTokensDefault),
    actual: join(identityWwwroot, 'css', 'tokens.css'),
  },
  {
    name: 'src/apps/Tellma.Identity/wwwroot/fonts/fonts.css',
    expected: composeIdentityFontsCss(),
    actual: join(identityWwwroot, 'fonts', 'fonts.css'),
  },
];

// The stylesheet names its woff2 files by relative path, so a stale or missing
// binary is a broken page that the text comparison above would not catch.
for (const source of identityFontFiles()) {
  const copy = join(identityWwwroot, 'fonts', basename(source));
  if (!existsSync(copy) || !readFileSync(copy).equals(readFileSync(source))) {
    console.error(
      `tokens:check FAILED — wwwroot/fonts/${basename(source)} is missing or stale; ` +
        'run `pnpm run identity:copy-assets`.',
    );
    process.exit(1);
  }
}
for (const copy of copies) {
  const normalize = (s: string) => s.replace(/\r\n/g, '\n');
  if (normalize(readFileSync(copy.actual, 'utf8')) !== normalize(copy.expected)) {
    console.error(
      `tokens:check FAILED — ${copy.name} is stale; run \`pnpm run tokens:build-css\` and \`pnpm run fonts:copy-identity\`.`,
    );
    process.exit(1);
  }
}

console.log(
  `tokens:check OK — schema, missing-ref (light+dark), ${stylesheets.length} stylesheets scanned ` +
    'for dangling var(), identity-server copies.',
);

// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

/**
 * tokens:check — the build gate (§4, DoD 9):
 *   1. zod-parses the default preset (schema gate),
 *   2. runs the missing-ref gate (both schemes + the :lang() leading map),
 *   3. emits the generated JSON Schema into the package's assets,
 *   4. verifies the identity server's committed copies (the emitted tokens
 *      stylesheet and the vendored fonts.css) match this workspace's output —
 *      the .NET build cannot run the emitter, so those copies are committed
 *      and this gate is what keeps them from drifting.
 * Exits non-zero on any issue. Color-contrast accessibility is covered by
 * the axe browser battery, not here.
 */
import { mkdirSync, readFileSync, writeFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';
import { z } from 'zod';

import { tmEmitCss, tmTokensDefault, tmValidateTokens } from '@tellma/core-ui-tokens';
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

// 3. Generated JSON Schema (shipped as a package asset).
const jsonSchema = z.toJSONSchema(tmTokensZodSchema, { target: 'draft-7' });
const outDir = join(packageDir, 'generated');
mkdirSync(outDir, { recursive: true });
writeFileSync(join(outDir, 'tm-tokens.schema.json'), JSON.stringify(jsonSchema, null, 2) + '\n');

// 4. Identity-server copy gate: the RCL commits the emitted stylesheet (and the
// vendored fonts.css) because its build has no Node toolchain; regenerate with
// `pnpm run tokens:build-css` and re-copy when this gate fails.
const identityWwwroot = join(clientDir, '..', 'src', 'apps', 'Tellma.Identity', 'wwwroot');
const copies: Array<{ name: string; expected: string; actual: string }> = [
  {
    name: 'src/apps/Tellma.Identity/wwwroot/css/tokens.css',
    expected: tmEmitCss(tmTokensDefault),
    actual: join(identityWwwroot, 'css', 'tokens.css'),
  },
  {
    name: 'src/apps/Tellma.Identity/wwwroot/fonts/fonts.css',
    expected: readFileSync(join(clientDir, 'projects', 'core', 'tellma-core-ui', 'fonts', 'fonts.css'), 'utf8'),
    actual: join(identityWwwroot, 'fonts', 'fonts.css'),
  },
];
for (const copy of copies) {
  const normalize = (s: string) => s.replace(/\r\n/g, '\n');
  if (normalize(readFileSync(copy.actual, 'utf8')) !== normalize(copy.expected)) {
    console.error(
      `tokens:check FAILED — ${copy.name} is stale; run \`pnpm run tokens:build-css\` and re-copy the emitted output.`,
    );
    process.exit(1);
  }
}

console.log('tokens:check OK — schema, missing-ref (light+dark), identity-server copies.');

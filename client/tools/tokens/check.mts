/**
 * tokens:check — the build gate (§4, DoD 9):
 *   1. zod-parses the default preset (schema gate),
 *   2. runs the validation gates (missing-ref, contrast both schemes,
 *      exception hygiene, completeness),
 *   3. emits the generated JSON Schema into the package's assets.
 * Exits non-zero on any issue.
 */
import { mkdirSync, writeFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';
import { z } from 'zod';

import { tmTokensDefault, tmValidateTokens } from '@tellma/core-ui-tokens';
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

// 2. Validation gates.
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

console.log('tokens:check OK — schema, contrast (light+dark), completeness, exceptions.');

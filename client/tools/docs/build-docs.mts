/**
 * docs:build — generates components.json (schema-validated) + its JSON
 * Schema + llms.txt into the MCP package (spec §11). llms.txt is the
 * static, no-server path for a coding agent to load the whole library in
 * one fetch; the MCP server is the interactive path. Both derive from the
 * same components.json, so they never diverge.
 */
import { mkdirSync, writeFileSync } from 'node:fs';
import { dirname, join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import { z } from 'zod';

import { componentsJson } from './components-schema.mjs';
import { extractComponents } from './extract-components.mjs';
import type { ComponentDoc } from './components-schema.mjs';

const clientDir = resolve(dirname(fileURLToPath(import.meta.url)), '..', '..');
const outDir = join(clientDir, 'projects', 'core', 'tellma-core-ui-mcp', 'generated');
mkdirSync(outDir, { recursive: true });

const doc = extractComponents();

// Validate against the schema BEFORE writing (the CI contract, DoD 14).
const parsed = componentsJson.safeParse(doc);
if (!parsed.success) {
  console.error('components.json FAILED schema validation:');
  console.error(z.prettifyError(parsed.error));
  process.exit(1);
}

// Consumer-visible docs must stand alone: fail on internal spec references
// ("§" section sigils, "spec 0002") leaking out of TSDoc into the digest.
const SPEC_REF = /§|spec 0002/;
const leaking = doc.components.filter((component) => SPEC_REF.test(JSON.stringify(component)));
if (leaking.length > 0 || SPEC_REF.test(JSON.stringify(doc))) {
  const offenders = leaking.map((component) => component.name).join(', ') || '(document header)';
  console.error(
    `components.json contains internal spec references ("§" / "spec 0002") in: ${offenders}.`,
  );
  console.error('Rewrite the offending TSDoc to be self-contained — consumers never see the spec.');
  process.exit(1);
}

writeFileSync(join(outDir, 'components.json'), JSON.stringify(doc, null, 2) + '\n');
writeFileSync(
  join(outDir, 'components.schema.json'),
  JSON.stringify(z.toJSONSchema(componentsJson, { target: 'draft-7' }), null, 2) + '\n',
);

function llmsSection(component: ComponentDoc): string {
  const lines = [
    `## ${component.name} (\`${component.selector}\`)`,
    '',
    component.description,
    '',
    `- Kind: ${component.kind} | Group: ${component.group} | Status: ${component.status}`,
    `- Import: \`${component.entryPoint}\``,
    `- Forms: ${component.formControl ?? 'not a form control'}`,
    `- Harness: \`${component.harness}\` (@tellma/core-ui-testing)`,
  ];
  if (component.inputs.length > 0) {
    lines.push('', '### Inputs', '');
    for (const input of component.inputs) {
      lines.push(
        `- \`${input.name}\` (${input.signal}): \`${input.type}\`` +
          (input.required ? ' — required' : input.default ? ` — default \`${input.default}\`` : '') +
          (input.description ? ` — ${input.description.split('\n')[0]}` : ''),
      );
    }
  }
  if (component.outputs.length > 0) {
    lines.push('', '### Outputs', '');
    for (const output of component.outputs) {
      lines.push(
        `- \`${output.name}\`: \`${output.type}\`` +
          (output.description ? ` — ${output.description.split('\n')[0]}` : ''),
      );
    }
  }
  if (component.slots.length > 0) {
    lines.push('', '### Slots', '');
    for (const slot of component.slots) {
      lines.push(`- \`${slot.selector}\` — ${slot.description}`);
    }
  }
  if (component.a11y.keyboard.length > 0) {
    lines.push('', '### Keyboard', '');
    for (const key of component.a11y.keyboard) {
      lines.push(`- ${key}`);
    }
  }
  if (component.examples.length > 0) {
    lines.push('', '### Example', '', '```html', component.examples[0].code, '```');
  }
  lines.push('', `Tokens read: ${component.tokens.join(', ') || '(none)'}`, '');
  return lines.join('\n');
}

const llms = [
  '# @tellma/core-ui — component digest',
  '',
  `Generated from components.json (schema ${doc.schemaVersion}, library ${doc.libraryVersion}).`,
  'Signal-first Angular components on @angular/cdk + @angular/aria; Signal Forms native',
  '([formField] binds each control; the bound field is authoritative for',
  'disabled/readonly/required). Theming = CSS custom properties emitted by',
  '@tellma/core-ui-tokens (@layer tm.base < tm.theme < inline styles).',
  '',
  ...doc.components.map(llmsSection),
].join('\n');

writeFileSync(join(outDir, 'llms.txt'), llms);

console.log(`docs:build OK — ${doc.components.length} components ->`);
for (const file of ['components.json', 'components.schema.json', 'llms.txt']) {
  console.log(`  ${join(outDir, file)}`);
}

// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { readFileSync } from 'node:fs';
import { dirname, join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

import ts from 'typescript';
import { beforeAll, describe, expect, it } from 'vitest';

// The server's client entity model against the client's types. The server's
// suite writes three fixtures from its Acme-like test model: the Entities
// chunk and the Core chunk's summaries (in @tellma/core-testing) and the
// emitted declarations (beside this spec). Each is compiled here, through the
// workspace's own TypeScript options and @tellma path mappings, so a member
// the server writes and the client does not type, or a declaration the
// compiler refuses, fails this suite.

/** The client workspace's root. */
const clientRoot = resolve(dirname(fileURLToPath(import.meta.url)), '..', '..');

/**
 * The folder of the emitted declarations' fixture. The fixture is kept as
 * text, `tellma-entities.d.ts.txt`: as a declaration file it would augment
 * @tellma/core in every program a tool builds over the workspace.
 */
const fixturesDir = join(clientRoot, 'tools', 'tests', 'fixtures', 'entity-model');

/** A fixture of @tellma/core-testing, as text. */
function testingFixture(name: string): string {
  // Trimmed: a line break before `as const` would end the statement.
  return readFileSync(join(clientRoot, 'projects', 'core', 'tellma-core-testing', 'fixtures', 'entity-model', name), 'utf8').trimEnd();
}

/** A file the program holds in memory, in the fixture's folder. */
function virtual(name: string): string {
  return join(fixturesDir, name).replaceAll('\\', '/');
}

/** The emitted declarations, under their emitted name, which the checked files import. */
const declarations = virtual('tellma-entities.d.ts');

// --- The files under check ---

/** The chunk fixtures, each satisfying its client type without widening. */
const chunks = virtual('chunks.check.ts');

/** Uses of the emitted types: what they accept and, under @ts-expect-error, what they refuse. */
const usage = virtual('usage.check.ts');

/** A chunk the client type refuses, which proves the check can fail. */
const refused = virtual('refused.check.ts');

/** Paths over a synthetic, densely connected model, whose checking time is bounded. */
const connected = virtual('connected.check.ts');

// --- The synthetic model: 20 entities, each with 30 scalars, a group and 12 navigations to others ---

/** The synthetic model's number of entities. */
const ENTITIES = 20;

/** Each synthetic entity's number of navigations. */
const NAVIGATIONS = 12;

/** The synthetic model's shapes, as the emitted declarations would augment them. */
function connectedShapes(): string {
  const scalars = Array.from({ length: 30 }, (_, index) => `'S${index}'`).join(' | ');
  const entities = Array.from({ length: ENTITIES }, (_, entity) => {
    const navigations = Array.from({ length: NAVIGATIONS }, (_, index) => `N${index}: 'syn.E${(entity + index + 1) % ENTITIES}'`).join('; ');
    return `    'syn.E${entity}': { scalars: ${scalars}; groups: 'G'; navigations: { ${navigations} }; children: {} };`;
  });
  return ["declare module '@tellma/core' {", '  interface TmEntityShapes {', ...entities, '  }', '}'].join('\n');
}

/**
 * The most the connected model's file may take to check, in milliseconds:
 * about 0.2 s measured, against minutes, or TS2590, when every path was
 * enumerated.
 */
const CONNECTED_BUDGET_MS = 10_000;

const sources = new Map<string, string>([
  [declarations, readFileSync(join(fixturesDir, 'tellma-entities.d.ts.txt'), 'utf8')],
  [
    chunks,
    [
      "import type { TmEntitiesChunk, TmEntitySummary } from '@tellma/core';",
      `export const chunk = ${testingFixture('entities-chunk.json')} as const satisfies TmEntitiesChunk;`,
      `export const summaries = ${testingFixture('core-entities.json')} as const satisfies readonly TmEntitySummary[];`,
    ].join('\n'),
  ],
  [
    usage,
    [
      "import { tmEntity, type TmDecimal, type TmEntityShapes, type TmEntityType, type TmJsonObject, type TmOrderInput, type TmPath } from '@tellma/core';",
      "import type { AcmeCustomer, AcmeCustomerType, GlCenter } from './tellma-entities';",
      '',
      "declare const customer: TmEntityType<'acme.Customer'>;",
      "declare const center: TmEntityType<'gl.Center'>;",
      '',
      '// A registered name gives its wire interface.',
      'export const row: AcmeCustomer = customer;',
      'export const limit: TmDecimal = customer.creditLimit;',
      'export const reference: TmDecimal | null | undefined = customer.externalReference;',
      'export const kind: AcmeCustomerType = customer.customerType;',
      'export const twin: string | null | undefined = customer.name2;',
      'export const contacts: readonly { readonly email?: string | null }[] | null | undefined = customer.contacts;',
      'export const served: AcmeCustomerType | null | undefined = center.servedCustomerType;',
      'export const tree: GlCenter = center;',
      "export const wholesale: AcmeCustomerType = 'wholesale-trade';",
      '',
      '// An unregistered name gives a JSON object.',
      "export const untyped: TmEntityType<'acme.Unknown'> = { anything: 1 } satisfies TmJsonObject;",
      '',
      '// The path shapes name properties, groups, navigations and children by their C# names.',
      "export const scalar: TmEntityShapes['acme.Customer']['scalars'] = 'CreditLimit';",
      "export const group: TmEntityShapes['acme.Customer']['groups'] = 'Name';",
      "export const target: TmEntityShapes['acme.Customer']['navigations']['Currency'] = 'acme.Currency';",
      "export const child: TmEntityShapes['acme.Customer']['children']['Contacts'] = 'acme.CustomerContact';",
      '',
      '// An annotation checks the first two segments of a path exactly.',
      "export const path: TmPath<'acme.Customer'> = 'Currency.Code';",
      "export const three: TmPath<'acme.Customer'> = 'Currency.CreatedBy.Image.FileName';",
      "export const four: TmPath<'acme.Customer'> = 'Currency.CreatedBy.ModifiedBy.Image.Anything';",
      "export const anyPath: TmPath<'acme.Unknown'> = 'Whatever.Goes';",
      "export const order: TmOrderInput<'acme.Customer'> = 'Currency.Code:desc';",
      '',
      '// tmEntity checks every segment of the literal paths its declaration names.',
      "export const declared = tmEntity('acme.Customer', { columns: ['Code', 'Name', 'Currency', { path: 'CreditLimit', width: 120 }], orderBy: 'Code:desc' });",
      "export const deep = tmEntity('acme.Customer', {",
      "  columns: ['Currency.CreatedBy.Image.FileName', { path: 'Currency.Code', select: ['Currency.ModifiedBy.Name'] }, { expression: 'Code', labelKey: 'Code' }],",
      "  orderBy: 'Currency.CreatedBy.Name:desc',",
      "  tile: { image: 'Image', title: 'Currency.CreatedBy.Image.FileName' },",
      "  picker: { description: ['Currency.ModifiedBy.Image.Size'], orderBy: 'Currency.Code' },",
      '});',
      "export const anyEntity = tmEntity('acme.Unknown', { columns: ['Whatever.Goes.Deep'] });",
      '',
      '// @ts-expect-error a column names a property the entity has',
      "export const typo = tmEntity('acme.Customer', { columns: ['Cod'] });",
      '// @ts-expect-error a path steps only through navigations',
      "export const throughScalar: TmPath<'acme.Customer'> = 'Code.Length';",
      '// @ts-expect-error an annotation checks the first two segments of a longer path',
      "export const badPrefix: TmPath<'acme.Customer'> = 'Currency.Nope.CreatedBy.Image.Anything';",
      '// @ts-expect-error the path of an order is checked apart from its suffix',
      "export const badOrder: TmOrderInput<'acme.Customer'> = 'Currency.Cod:desc';",
      '// @ts-expect-error tmEntity checks the fourth segment of a column',
      "export const deepColumn = tmEntity('acme.Customer', { columns: ['Currency.CreatedBy.Image.Nope'] });",
      '// @ts-expect-error tmEntity checks the select of a column',
      "export const deepSelect = tmEntity('acme.Customer', { columns: [{ path: 'Code', select: ['Currency.CreatedBy.Nope.Name'] }] });",
      '// @ts-expect-error tmEntity checks the path of an order apart from its suffix',
      "export const deepOrder = tmEntity('acme.Customer', { orderBy: 'Currency.CreatedBy.Nope:desc' });",
      '// @ts-expect-error tmEntity checks the slots of a tile',
      "export const deepTile = tmEntity('acme.Customer', { tile: { subtitle: 'Currency.CreatedBy.Image.Nope' } });",
      '// @ts-expect-error tmEntity checks the description of a picker',
      "export const deepPicker = tmEntity('acme.Customer', { picker: { description: ['Currency.CreatedBy.Image.Nope'] } });",
      '// @ts-expect-error a decimal is text, never a number',
      'export const numeric: number = customer.creditLimit;',
      '// @ts-expect-error the wire spells the member in camelCase',
      'export const pascal = customer.CreditLimit;',
      '// @ts-expect-error the wire spells the member by its JSON name',
      "export const csharp: AcmeCustomerType = 'Wholesale';",
      '// @ts-expect-error a JSON column is no path',
      "export const notes: TmEntityShapes['acme.Customer']['scalars'] = 'Notes';",
      '// @ts-expect-error a currency has no groups',
      "export const none: TmEntityShapes['acme.Currency']['groups'] = 'Name';",
    ].join('\n'),
  ],
  [
    connected,
    [
      "import { tmEntity, type TmOrderInput, type TmPath } from '@tellma/core';",
      '',
      connectedShapes(),
      '',
      "export const path: TmPath<'syn.E0'> = 'N0.N1.N2.S3';",
      "export const order: TmOrderInput<'syn.E0'> = 'N0.N1.S2:desc';",
      "export const declared = tmEntity('syn.E0', { columns: ['N0.N1.N2.N3.S4', 'N11.G'], orderBy: 'N1.N2.N3.S0:desc' });",
      '// @ts-expect-error a fourth segment the entity lacks',
      "export const refusedPath = tmEntity('syn.E0', { columns: ['N0.N1.N2.Nope'] });",
    ].join('\n'),
  ],
  [
    refused,
    [
      "import type { TmEntitiesChunk } from '@tellma/core';",
      "export const chunk = { entities: [], enums: [{ name: 'acme.Tier', values: [{ value: 1 }] }] } as const satisfies TmEntitiesChunk;",
    ].join('\n'),
  ],
]);

/** The diagnostics of one file, formatted, with paths from the client root. */
function diagnosticsOf(program: ts.Program, fileName: string): string[] {
  const file = program.getSourceFile(fileName);
  if (file === undefined) {
    return [`${fileName} is not in the program`];
  }

  return [...program.getSyntacticDiagnostics(file), ...program.getSemanticDiagnostics(file)].map((diagnostic) =>
    ts.formatDiagnostic(diagnostic, {
      getCanonicalFileName: (name) => name,
      getCurrentDirectory: () => clientRoot,
      getNewLine: () => '\n',
    }),
  );
}

// Building a program over @tellma/core's sources takes seconds, and on a busy
// machine with a .NET build alongside it can take a minute or more.
describe('the client entity model fixtures', { timeout: 300_000 }, () => {
  let program: ts.Program;

  beforeAll(() => {
    // --- The workspace's options, with every declaration checked ---
    const parsed = ts.getParsedCommandLineOfConfigFile(
      join(clientRoot, 'tsconfig.json'),
      { noEmit: true, skipLibCheck: false, types: [] },
      {
        ...ts.sys,
        onUnRecoverableConfigFileDiagnostic: (diagnostic) => {
          throw new Error(ts.flattenDiagnosticMessageText(diagnostic.messageText, '\n'));
        },
      },
    );
    if (parsed === undefined) {
      throw new Error('The client workspace tsconfig.json did not parse.');
    }

    // --- A host that serves the checked files from memory and the rest from disk ---
    const host = ts.createCompilerHost(parsed.options, true);
    const readFile = host.readFile.bind(host);
    const fileExists = host.fileExists.bind(host);
    const getSourceFile = host.getSourceFile.bind(host);
    host.readFile = (name) => sources.get(name.replaceAll('\\', '/')) ?? readFile(name);
    host.fileExists = (name) => sources.has(name.replaceAll('\\', '/')) || fileExists(name);
    host.getSourceFile = (name, language, onError, shouldCreate) => {
      const text = sources.get(name.replaceAll('\\', '/'));
      return text === undefined
        ? getSourceFile(name, language, onError, shouldCreate)
        : ts.createSourceFile(name, text, language, true);
    };

    program = ts.createProgram({ rootNames: [...sources.keys()], options: parsed.options, host });
  }, 300_000);

  it('the declarations compile', () => {
    expect(diagnosticsOf(program, declarations)).toEqual([]);
  });

  it('the Entities chunk and the summaries satisfy their client types', () => {
    expect(diagnosticsOf(program, chunks)).toEqual([]);
  });

  it('the declarations type rows and paths by entity name', () => {
    expect(diagnosticsOf(program, usage)).toEqual([]);
  });

  it('paths over a densely connected model check quickly', () => {
    // Every other file is checked first, so the timing covers this file's paths alone.
    for (const name of [declarations, chunks, usage]) {
      diagnosticsOf(program, name);
    }
    const started = performance.now();
    const diagnostics = diagnosticsOf(program, connected);
    const elapsed = performance.now() - started;

    expect(diagnostics).toEqual([]);
    expect(elapsed).toBeLessThan(CONNECTED_BUDGET_MS);
  });

  it('a chunk off its client type is refused', () => {
    const refusals = diagnosticsOf(program, refused);

    expect(refusals).toHaveLength(1);
    expect(refusals[0]).toContain("Type 'number' is not assignable to type 'string'");
  });
});

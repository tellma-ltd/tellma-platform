// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

// Regenerates the tellma/*.html fixtures AND the excel/*.txt TSV fixtures.
//
// - tellma/*.html go through the REAL serializer (`tmSerializeHtmlTable`),
//   so the pinned payloads always carry the exact `data-tm-*` attribute
//   grammar the grid writes — if the grammar changes, rerunning this script
//   updates the fixtures and the paste specs still assert the contract.
// - excel/*.txt are byte-authored here (CRLF row terminators, Excel
//   quoting) because a text editor cannot reliably produce the exact CR/LF
//   and embedded-control bytes the round-trip assertions compare.
//
// Run from client/:  pnpm exec tsx e2e/fixtures/clipboard/generate-tellma.mts

import { writeFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';

import { tmSerializeHtmlTable } from '../../../projects/core/tellma-core-ui/grid-engine/tm-grid-clipboard-serialize';

const here = dirname(fileURLToPath(import.meta.url));

function emit(relative: string, content: string): void {
  const path = join(here, relative);
  writeFileSync(path, content, { encoding: 'utf8' });
  console.log(`wrote ${relative} (${Buffer.byteLength(content)} bytes)`);
}

// ---------------------------------------------------------------------------
// tellma/typed-2x3.html — a 2-row × 3-column copy of number cells (quantity,
// unitPrice, discount) from tenant t1. The first cell's DISPLAY string
// ('1,5') deliberately disagrees with its raw value (1.5): under an 'en'
// parse it would come out 15, so the paste specs can prove the typed fast
// path wrote the raw value without parsing.
emit(
  'tellma/typed-2x3.html',
  tmSerializeHtmlTable({
    matrix: [
      ['1,5', '200', '10'],
      ['3', '4.5', '0'],
    ],
    meta: {
      v: 1,
      tenantId: 't1',
      distributionKey: 'd1',
      locale: 'en-US',
      cols: [
        { key: 'quantity', type: 'number' },
        { key: 'unitPrice', type: 'number' },
        { key: 'discount', type: 'number' },
      ],
    },
    rawValues: [
      [{ value: 1.5 }, { value: 200 }, { value: 10 }],
      [{ value: 3 }, { value: 4.5 }, { value: 0 }],
    ],
  }),
);

// ---------------------------------------------------------------------------
// tellma/full-rows.html — a FULL-ROW copy (per-row data-tm-rowid) of two
// rows across every grid-editable story column, tenant t1. The trailing
// column mirrors the story's accessor Total column (key null — never
// written on paste). The agentId cells carry raw entity ids so a
// same-tenant paste writes them WITHOUT invoking the resolver.
emit(
  'tellma/full-rows.html',
  tmSerializeHtmlTable({
    matrix: [
      ['Moved A', '2', '3.5', '1', 'TRUE', 'Goods', 'Alice Green', '7'],
      ['Moved B', '4', '2.25', '0', 'FALSE', 'Services', 'Bob Stone', '9'],
    ],
    meta: {
      v: 1,
      tenantId: 't1',
      distributionKey: 'd1',
      locale: 'en-US',
      cols: [
        { key: 'description', type: 'text' },
        { key: 'quantity', type: 'number' },
        { key: 'unitPrice', type: 'number' },
        { key: 'discount', type: 'number' },
        { key: 'isPosted', type: 'boolean' },
        { key: 'category', type: 'enum' },
        { key: 'agentId', type: 'entity' },
        { key: null, type: 'number' },
      ],
    },
    rawValues: [
      [
        { value: 'Moved A' },
        { value: 2 },
        { value: 3.5 },
        { value: 1 },
        { value: true },
        { value: 'goods' },
        { value: 11 },
        { value: 7 },
      ],
      [
        { value: 'Moved B' },
        { value: 4 },
        { value: 2.25 },
        { value: 0 },
        { value: false },
        { value: 'services' },
        { value: 12 },
        { value: 9 },
      ],
    ],
    rowIds: [901, 902],
  }),
);

// ---------------------------------------------------------------------------
// tellma/with-headers.html — a copy-with-headers payload: the header row
// rides inside <thead> AND the metadata carries headers:true, so a paste
// back into a Tellma grid skips it via the FLAG (no content heuristic).
emit(
  'tellma/with-headers.html',
  tmSerializeHtmlTable({
    matrix: [
      ['11', '12.5'],
      ['13', '14.25'],
    ],
    meta: {
      v: 1,
      tenantId: 't1',
      distributionKey: 'd1',
      locale: 'en-US',
      cols: [
        { key: 'quantity', type: 'number' },
        { key: 'unitPrice', type: 'number' },
      ],
      headers: true,
    },
    rawValues: [
      [{ value: 11 }, { value: 12.5 }],
      [{ value: 13 }, { value: 14.25 }],
    ],
    headerRow: ['Qty', 'Unit price'],
  }),
);

// ---------------------------------------------------------------------------
// tellma/cross-tenant.html — a copy from ANOTHER tenant (t2) on the SAME
// distribution (d1) whose raw entity ids are deliberately WRONG for the
// labels (13 = 'Carol White', 16 = 'Dana Reed' in the story directory). If a
// paste ever trusted cross-tenant raw ids, the model would receive 13/16;
// the correct §9.4 behavior re-resolves the labels to 11 ('Alice Green') /
// 12 ('Bob Stone').
emit(
  'tellma/cross-tenant.html',
  tmSerializeHtmlTable({
    matrix: [['Alice Green'], ['Bob Stone']],
    meta: {
      v: 1,
      tenantId: 't2',
      distributionKey: 'd1',
      locale: 'en-US',
      cols: [{ key: 'agentId', type: 'entity' }],
    },
    rawValues: [[{ value: 13 }], [{ value: 16 }]],
  }),
);

// ---------------------------------------------------------------------------
// tellma/numbers-de.html — a copy whose metadata carries locale 'de' and
// German-formatted display strings WITHOUT raw values: the paste must parse
// through the source-locale hint (ctx.sourceLocale, §9.4). Under a plain
// 'en' parse '1.234,56' would come out 1.23456 and '2,5' would come out 25.
emit(
  'tellma/numbers-de.html',
  tmSerializeHtmlTable({
    matrix: [['1.234,56'], ['2,5']],
    meta: { v: 1, locale: 'de' },
  }),
);

// ---------------------------------------------------------------------------
// excel/simple-2x2.txt — the text/plain flavor Excel writes for a 2×2
// numeric range: TSV, CRLF after EVERY row (including the last).
emit('excel/simple-2x2.txt', '3\t4.50\r\n5\t6.25\r\n');

// ---------------------------------------------------------------------------
// excel/quoted-cells.txt — Excel quoting rules: a cell containing a tab,
// newline, or quote is wrapped in quotes with inner quotes doubled;
// in-cell line breaks are a bare LF inside the quoted field while rows
// still terminate with CRLF.
emit(
  'excel/quoted-cells.txt',
  '"tab\there"\t12\r\n"line\nbreak"\t"quote ""q"""\r\n',
);

// ---------------------------------------------------------------------------
// excel/numbers-de.txt — the TSV Excel writes under a German (de-DE) system
// locale: '.' thousands grouping, ',' decimal separator. NOTE: plain TSV
// carries no locale metadata, so a paste into an 'en' grid CANNOT know
// these are German numbers — source-locale parsing is only reachable
// through HTML metadata (see tellma/numbers-de.html). This fixture pins the
// wire format and documents that limitation.
emit('excel/numbers-de.txt', '1.234,56\t2.500,75\r\n');

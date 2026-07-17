// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { TM_PARSE_ERROR, type TmLabelResolution } from '@tellma/core-ui/contracts';

import type { TmGridCopyPayload } from './tm-grid-clipboard';
import { makeEngine, makeRows, type TestHarness } from './tm-grid-testing.util';

function requirePayload(payload: TmGridCopyPayload | null): TmGridCopyPayload {
  if (payload === null) {
    throw new Error('expected a copy payload');
  }
  return payload;
}

/** Selects the inclusive view-space rectangle and anchors activity at its start. */
function selectRect(
  harness: TestHarness,
  from: { row: number; col: number },
  to: { row: number; col: number },
): void {
  harness.engine.clickCell(from);
  harness.engine.selection.extendActiveTo(to);
}

describe('TmGridClipboard copy', () => {
  it('exports a single range as display text in view order with raw model values', () => {
    const h = makeEngine(makeRows(2));
    selectRect(h, { row: 0, col: 0 }, { row: 1, col: 1 });
    const payload = requirePayload(h.engine.clipboard.copy());
    expect(payload.matrix).toEqual([
      ['a1', 'b1'],
      ['a2', 'b2'],
    ]);
    expect(payload.rawValues).toEqual([
      [{ value: 'a1' }, { value: 'b1' }],
      [{ value: 'a2' }, { value: 'b2' }],
    ]);
    expect(payload.rowIds).toBeUndefined();
    expect(payload.headerRow).toBeUndefined();
    expect(payload.cellCount).toBe(4);
  });

  it('stamps the meta with version, tenant, locale, and the copied column keys/types', () => {
    const h = makeEngine(makeRows(1), { tenant: 't1', locale: 'fr' });
    selectRect(h, { row: 0, col: 0 }, { row: 0, col: 1 });
    const payload = requirePayload(h.engine.clipboard.copy());
    expect(payload.meta).toEqual({
      v: 1,
      tenant: 't1',
      locale: 'fr',
      cols: [
        { key: 'a', type: 'text' },
        { key: 'b', type: 'text' },
      ],
    });
  });

  it('copies boolean cells as TRUE/FALSE text carrying raw booleans', () => {
    const h = makeEngine(
      [
        { id: 1, flag: true },
        { id: 2, flag: false },
      ],
      { columns: [{ key: 'flag', type: 'boolean' }] },
    );
    selectRect(h, { row: 0, col: 0 }, { row: 1, col: 0 });
    const payload = requirePayload(h.engine.clipboard.copy());
    expect(payload.matrix).toEqual([['TRUE'], ['FALSE']]);
    expect(payload.rawValues).toEqual([[{ value: true }], [{ value: false }]]);
  });

  it('exports an invalid-input cell as its raw text with no raw value', () => {
    const h = makeEngine(makeRows(1));
    h.engine.clickCell({ row: 0, col: 0 });
    h.engine.edit.openEdit({ row: 0, col: 0 }, 'edit');
    h.engine.edit.commitText('BAD');
    h.engine.clickCell({ row: 0, col: 0 });
    const payload = requirePayload(h.engine.clipboard.copy());
    expect(payload.matrix).toEqual([['BAD']]);
    expect(payload.rawValues).toEqual([[undefined]]);
  });

  it('withHeaders adds the header row and flags the meta', () => {
    const h = makeEngine(makeRows(1), {
      columns: [
        { key: 'a', header: 'Alpha' },
        { key: 'b', header: 'Beta' },
      ],
    });
    selectRect(h, { row: 0, col: 0 }, { row: 0, col: 1 });
    const payload = requirePayload(h.engine.clipboard.copy({ withHeaders: true }));
    expect(payload.headerRow).toEqual(['Alpha', 'Beta']);
    expect(payload.meta.headers).toBe(true);
  });

  it('stacks aligned multi-range selections into one compaction', () => {
    const h = makeEngine(makeRows(3));
    selectRect(h, { row: 0, col: 0 }, { row: 0, col: 1 });
    h.engine.selection.addRange({
      anchor: { row: 2, col: 0 },
      focus: { row: 2, col: 1 },
      kind: 'cells',
    });
    const payload = requirePayload(h.engine.clipboard.copy());
    expect(payload.matrix).toEqual([
      ['a1', 'b1'],
      ['a3', 'b3'],
    ]);
  });

  it('refuses a misaligned multi-range selection with a notice', () => {
    const h = makeEngine(makeRows(3));
    h.engine.clickCell({ row: 0, col: 0 });
    h.engine.selection.addRange({
      anchor: { row: 1, col: 1 },
      focus: { row: 1, col: 2 },
      kind: 'cells',
    });
    expect(h.engine.clipboard.copy()).toBeNull();
    expect(h.notices).toContainEqual({ kind: 'copyRefusedMisaligned' });
  });

  it('carries rowIds for a full-row selection', () => {
    const h = makeEngine(makeRows(3));
    h.engine.selection.selectRows(0, 1, false);
    const payload = requirePayload(h.engine.clipboard.copy());
    expect(payload.rowIds).toEqual([1, 2]);
    expect(payload.matrix).toEqual([
      ['a1', 'b1', 'c1'],
      ['a2', 'b2', 'c2'],
    ]);
  });

  it('keeps raw values regardless of copy size (the HTML flavor sheds them, not the payload)', () => {
    // A large copy stays typed for a same-session paste; only the copied
    // HTML flavor drops per-cell raw values above the oversize threshold.
    const h = makeEngine(makeRows(2));
    h.engine.selection.selectRows(0, 1, false);
    const payload = requirePayload(h.engine.clipboard.copy());
    expect(payload.rawValues).toEqual([
      [{ value: 'a1' }, { value: 'b1' }, { value: 'c1' }],
      [{ value: 'a2' }, { value: 'b2' }, { value: 'c2' }],
    ]);
    expect(payload.meta.cols).toHaveLength(3);
    expect(payload.rowIds).toEqual([1, 2]);
    expect(payload.matrix[0]).toEqual(['a1', 'b1', 'c1']);
  });
});

describe('TmGridClipboard paste shaping', () => {
  it('fills every cell of the selection with a single source value', () => {
    const h = makeEngine(makeRows(2));
    selectRect(h, { row: 0, col: 0 }, { row: 1, col: 1 });
    const result = h.engine.clipboard.paste({ matrix: [['x']] });
    expect(result.cellsWritten).toBe(4);
    expect(h.rows()[0]['a']).toBe('x');
    expect(h.rows()[0]['b']).toBe('x');
    expect(h.rows()[1]['a']).toBe('x');
    expect(h.rows()[1]['b']).toBe('x');
    expect(h.rows()[0]['c']).toBe('c1');
  });

  it('tiles the source across a target that is an exact multiple on both axes', () => {
    const h = makeEngine(makeRows(4));
    selectRect(h, { row: 0, col: 0 }, { row: 3, col: 1 });
    const result = h.engine.clipboard.paste({ matrix: [['x'], ['y']] });
    expect(result.cellsWritten).toBe(8);
    expect(h.rows().map((row) => row['a'])).toEqual(['x', 'y', 'x', 'y']);
    expect(h.rows().map((row) => row['b'])).toEqual(['x', 'y', 'x', 'y']);
  });

  it('pastes once from the anchor when the target is not an exact multiple, writing past the selection', () => {
    const h = makeEngine(makeRows(3));
    selectRect(h, { row: 0, col: 0 }, { row: 2, col: 0 });
    h.engine.clipboard.paste({
      matrix: [
        ['p', 'q'],
        ['r', 's'],
      ],
    });
    expect(h.rows()[0]['a']).toBe('p');
    expect(h.rows()[1]['a']).toBe('r');
    // The second source column lands outside the one-column selection.
    expect(h.rows()[0]['b']).toBe('q');
    expect(h.rows()[1]['b']).toBe('s');
    // The third selected row is not pasted (no tiling).
    expect(h.rows()[2]['a']).toBe('a3');
  });

  it('drops source columns overflowing past the last grid column', () => {
    const h = makeEngine(makeRows(1));
    h.engine.clickCell({ row: 0, col: 2 });
    const result = h.engine.clipboard.paste({ matrix: [['x', 'y', 'z']] });
    expect(result.cellsWritten).toBe(1);
    expect(h.rows()[0]['c']).toBe('x');
    expect(h.rows()[0]['a']).toBe('a1');
    expect(h.rows()[0]['b']).toBe('b1');
  });

  it('materializes appended rows for row overflow when rows can be added', () => {
    const h = makeEngine(makeRows(2));
    h.engine.clickCell({ row: 1, col: 0 });
    const result = h.engine.clipboard.paste({ matrix: [['x'], ['y'], ['z']] });
    expect(result.rowsMaterialized).toBe(2);
    expect(result.rowsDropped).toBe(0);
    expect(h.rows()).toHaveLength(4);
    expect(h.rows()[1]['a']).toBe('x');
    expect(h.rows()[2]['a']).toBe('y');
    expect(h.rows()[3]['a']).toBe('z');
  });

  it('drops overflow rows when rows cannot be added', () => {
    const h = makeEngine(makeRows(2));
    h.canAddRows.set(false);
    h.engine.clickCell({ row: 1, col: 0 });
    const result = h.engine.clipboard.paste({ matrix: [['x'], ['y'], ['z']] });
    expect(result.rowsMaterialized).toBe(0);
    expect(result.rowsDropped).toBe(2);
    expect(h.rows()).toHaveLength(2);
    expect(h.rows()[1]['a']).toBe('x');
  });

  it('skips readonly cells in place without shifting values around them', () => {
    const h = makeEngine(makeRows(1), {
      columns: [{ key: 'a' }, { key: 'b', cellReadonly: (row) => row.id === 1 }, { key: 'c' }],
    });
    h.engine.clickCell({ row: 0, col: 0 });
    h.engine.clipboard.paste({ matrix: [['x', 'y', 'z']] });
    expect(h.rows()[0]['a']).toBe('x');
    expect(h.rows()[0]['b']).toBe('b1');
    expect(h.rows()[0]['c']).toBe('z');
  });

  it('materializes everything when pasting onto the placeholder row', () => {
    const h = makeEngine(makeRows(1));
    h.engine.clickCell({ row: 1, col: 0 }); // the placeholder row
    const result = h.engine.clipboard.paste({ matrix: [['x'], ['y']] });
    expect(result.rowsMaterialized).toBe(2);
    expect(h.rows()).toHaveLength(3);
    expect(h.rows()[1]['a']).toBe('x');
    expect(h.rows()[2]['a']).toBe('y');
  });

  it('writes the cleared value for empty-string source cells', () => {
    const h = makeEngine(makeRows(1));
    h.engine.clickCell({ row: 0, col: 0 });
    h.engine.clipboard.paste({ matrix: [['', 'x']] });
    expect(h.rows()[0]['a']).toBeNull();
    expect(h.rows()[0]['b']).toBe('x');
    expect(h.engine.annotations.invalidCount()).toBe(0);
  });

  it('records the whole paste, materialized rows included, as one undo entry', () => {
    const h = makeEngine(makeRows(2));
    h.engine.clickCell({ row: 1, col: 0 });
    h.engine.clipboard.paste({ matrix: [['x'], ['y'], ['z']] });
    expect(h.rows()).toHaveLength(4);
    expect(h.engine.history.undo()).toBe(true);
    expect(h.rows()).toHaveLength(2);
    expect(h.rows()[1]['a']).toBe('a2');
    expect(h.engine.history.canUndo()).toBe(false);
  });
});

describe('TmGridClipboard paste conversion ladder', () => {
  it('writes raw values verbatim on the typed fast path, never running parse', () => {
    const h = makeEngine([{ id: 1, a: 'a1' }], {
      tenant: 't1',
      columns: [
        {
          key: 'a',
          parse: () => {
            throw new Error('parse must not run on the typed fast path');
          },
        },
      ],
    });
    h.engine.clickCell({ row: 0, col: 0 });
    const result = h.engine.clipboard.paste({
      matrix: [['display']],
      meta: { v: 1, tenant: 't1', locale: 'en', cols: [{ key: 'a', type: 'text' }] },
      rawValues: [[{ value: 'RAW' }]],
    });
    expect(result.cellsWritten).toBe(1);
    expect(h.rows()[0]['a']).toBe('RAW');
  });

  it('falls to parse on a tenant mismatch even when raw values are present', () => {
    const h = makeEngine([{ id: 1, a: 'a1' }], {
      tenant: 't1',
      columns: [{ key: 'a', parse: (text) => `parsed:${text}` }],
    });
    h.engine.clickCell({ row: 0, col: 0 });
    h.engine.clipboard.paste({
      matrix: [['display']],
      meta: { v: 1, tenant: 't2', locale: 'en', cols: [{ key: 'a', type: 'text' }] },
      rawValues: [[{ value: 'RAW' }]],
    });
    expect(h.rows()[0]['a']).toBe('parsed:display');
  });

  it('writes the parsed value on parse success', () => {
    const h = makeEngine(makeRows(1), {
      columns: [{ key: 'a', parse: (text) => text.toUpperCase() }],
    });
    h.engine.clickCell({ row: 0, col: 0 });
    const result = h.engine.clipboard.paste({ matrix: [['hello']] });
    expect(result.errors).toBe(0);
    expect(h.rows()[0]['a']).toBe('HELLO');
  });

  it('records unparseable text as an invalid input with the model cleared', () => {
    const h = makeEngine(makeRows(1));
    h.engine.clickCell({ row: 0, col: 0 });
    const result = h.engine.clipboard.paste({ matrix: [['BAD']] });
    expect(result.errors).toBe(1);
    expect(result.cellsWritten).toBe(0);
    expect(h.rows()[0]['a']).toBeNull();
    expect(h.engine.annotations.invalidInput(1, 'a')).toMatchObject({
      rawText: 'BAD',
      reason: 'parse',
    });
    expect(h.engine.displayText({ row: 0, col: 0 })).toBe('BAD');
  });

  it('batches resolver columns into one deduped request instead of failing', () => {
    const h = makeEngine(
      [
        { id: 1, r: 'x1' },
        { id: 2, r: 'x2' },
        { id: 3, r: 'x3' },
      ],
      { columns: [{ key: 'r', hasResolver: true, parse: () => TM_PARSE_ERROR }] },
    );
    h.engine.clickCell({ row: 0, col: 0 });
    const result = h.engine.clipboard.paste({
      matrix: [['Adam'], ['Bob'], ['Adam']],
      meta: { v: 1, tenant: 'src', locale: 'de' },
    });
    expect(result.errors).toBe(0);
    expect(h.engine.annotations.invalidCount()).toBe(0);
    expect(h.engine.annotations.pendingCount()).toBe(3);
    expect(result.resolutions).toHaveLength(1);
    const request = result.resolutions[0];
    expect(request.columnId).toBe('r');
    expect(request.labels).toEqual(['Adam', 'Bob']);
    expect(request.context.locale).toBe('en');
    expect(request.context.sourceLocale).toBe('de');
    expect(request.context.sourceTenant).toBe('src');
    expect(request.context.signal).toBeInstanceOf(AbortSignal);
    expect(request.context.signal.aborted).toBe(false);
  });

  it('issues one request per resolver column', () => {
    const h = makeEngine([{ id: 1, r1: 'x', r2: 'y' }], {
      columns: [
        { key: 'r1', hasResolver: true, parse: () => TM_PARSE_ERROR },
        { key: 'r2', hasResolver: true, parse: () => TM_PARSE_ERROR },
      ],
    });
    h.engine.clickCell({ row: 0, col: 0 });
    const result = h.engine.clipboard.paste({ matrix: [['A', 'B']] });
    expect(result.resolutions.map((request) => request.columnId)).toEqual(['r1', 'r2']);
    expect(result.resolutions[0].labels).toEqual(['A']);
    expect(result.resolutions[1].labels).toEqual(['B']);
  });
});

describe('TmGridClipboard paste header-row skip', () => {
  it('skips the first row when the source flags it as a header row', () => {
    const h = makeEngine(makeRows(2));
    h.engine.clickCell({ row: 0, col: 0 });
    h.engine.clipboard.paste({
      matrix: [
        ['whatever', 'junk'],
        ['x', 'y'],
      ],
      hasHeaderRow: true,
    });
    expect(h.rows()[0]['a']).toBe('x');
    expect(h.rows()[0]['b']).toBe('y');
    expect(h.rows()[1]['a']).toBe('a2');
  });

  it('skips a first row matching the target headers case-insensitively and trimmed', () => {
    const h = makeEngine(makeRows(2));
    h.engine.clickCell({ row: 0, col: 0 });
    h.engine.clipboard.paste({
      matrix: [
        [' A ', 'B'],
        ['x', 'y'],
      ],
    });
    expect(h.rows()[0]['a']).toBe('x');
    expect(h.rows()[0]['b']).toBe('y');
    expect(h.rows()[1]['a']).toBe('a2');
  });

  it('does not skip when one non-empty first-row cell mismatches its header', () => {
    const h = makeEngine(makeRows(2));
    h.engine.clickCell({ row: 0, col: 0 });
    h.engine.clipboard.paste({
      matrix: [
        ['a', 'nope'],
        ['x', 'y'],
      ],
    });
    expect(h.rows()[0]['a']).toBe('a');
    expect(h.rows()[0]['b']).toBe('nope');
    expect(h.rows()[1]['a']).toBe('x');
  });

  it('never fires the heuristic for a single-column source', () => {
    const h = makeEngine(makeRows(2));
    h.engine.clickCell({ row: 0, col: 0 });
    h.engine.clipboard.paste({ matrix: [['a'], ['x']] });
    expect(h.rows()[0]['a']).toBe('a');
    expect(h.rows()[1]['a']).toBe('x');
  });

  it('does not skip when the source explicitly says the first row is not a header', () => {
    const h = makeEngine(makeRows(2));
    h.engine.clickCell({ row: 0, col: 0 });
    h.engine.clipboard.paste({
      matrix: [
        ['a', 'b'],
        ['x', 'y'],
      ],
      hasHeaderRow: false,
    });
    expect(h.rows()[0]['a']).toBe('a');
    expect(h.rows()[0]['b']).toBe('b');
    expect(h.rows()[1]['a']).toBe('x');
  });
});

describe('TmGridClipboard paste resolution lifecycle', () => {
  const resolverColumns = [{ key: 'r', hasResolver: true, parse: () => TM_PARSE_ERROR }];

  it('applies resolved values, finalizes the entry, and stays one undo op with materialized rows', () => {
    const h = makeEngine([{ id: 1, r: 'old1' }], { columns: resolverColumns });
    h.engine.clickCell({ row: 0, col: 0 });
    const result = h.engine.clipboard.paste({ matrix: [['Adam'], ['Bob']] });
    expect(result.rowsMaterialized).toBe(1);
    expect(h.engine.annotations.pendingCount()).toBe(2);
    const request = result.resolutions[0];
    h.engine.clipboard.applyResolution(
      request.id,
      new Map<string, TmLabelResolution<unknown>>([
        ['Adam', { value: 'A-ID' }],
        ['Bob', { value: 'B-ID' }],
      ]),
    );
    expect(h.engine.annotations.pendingCount()).toBe(0);
    expect(h.rows()[0]['r']).toBe('A-ID');
    expect(h.rows()[1]['r']).toBe('B-ID');
    expect(h.notices).toContainEqual({ kind: 'resolutionComplete', resolved: 2, errors: 0 });
    expect(h.engine.history.undo()).toBe(true);
    expect(h.rows()).toHaveLength(1);
    expect(h.rows()[0]['r']).toBe('old1');
    expect(h.engine.history.canUndo()).toBe(false);
  });

  it('a second paste onto a pending cell supersedes the first — the newer resolution wins', () => {
    // §9.4.5: a later write invalidates an earlier request's token. The
    // paste-clear onto an already-empty pending cell is a value no-op, so the
    // guard must not rely on the write bumping the token on its own.
    const h = makeEngine([{ id: 1, r: null }], { columns: resolverColumns });
    h.engine.clickCell({ row: 0, col: 0 });
    const first = h.engine.clipboard.paste({ matrix: [['Alpha']] });
    expect(h.engine.annotations.pendingCount()).toBe(1);
    const second = h.engine.clipboard.paste({ matrix: [['Beta']] });
    // The FIRST resolution arrives first and must be discarded as stale.
    h.engine.clipboard.applyResolution(
      first.resolutions[0].id,
      new Map<string, TmLabelResolution<unknown>>([['Alpha', { value: 'ALPHA-ID' }]]),
    );
    h.engine.clipboard.applyResolution(
      second.resolutions[0].id,
      new Map<string, TmLabelResolution<unknown>>([['Beta', { value: 'BETA-ID' }]]),
    );
    expect(h.rows()[0]['r']).toBe('BETA-ID');
    expect(h.engine.annotations.pendingCount()).toBe(0);
  });

  it('a delete on a pending cell wins — the late resolution never overwrites the cleared cell', () => {
    const h = makeEngine([{ id: 1, r: 'old' }], { columns: resolverColumns });
    h.engine.clickCell({ row: 0, col: 0 });
    const result = h.engine.clipboard.paste({ matrix: [['Gamma']] });
    expect(h.engine.annotations.pendingCount()).toBe(1);
    // The user clears the pending cell before the resolution lands.
    h.engine.clearSelection();
    expect(h.engine.annotations.pendingCount()).toBe(0);
    h.engine.clipboard.applyResolution(
      result.resolutions[0].id,
      new Map<string, TmLabelResolution<unknown>>([['Gamma', { value: 'GAMMA-ID' }]]),
    );
    expect(h.rows()[0]['r']).toBeNull();
  });

  it('turns notFound, ambiguous, and unanswered labels into invalid inputs with cleared models', () => {
    const h = makeEngine(
      [
        { id: 1, r: 'old1' },
        { id: 2, r: 'old2' },
        { id: 3, r: 'old3' },
      ],
      { columns: resolverColumns },
    );
    h.engine.clickCell({ row: 0, col: 0 });
    const result = h.engine.clipboard.paste({ matrix: [['A'], ['B'], ['C']] });
    h.engine.clipboard.applyResolution(
      result.resolutions[0].id,
      new Map<string, TmLabelResolution<unknown>>([
        ['A', { error: 'notFound' }],
        ['B', { error: 'ambiguous' }],
        // 'C' is missing from the map — treated as notFound.
      ]),
    );
    expect(h.engine.annotations.pendingCount()).toBe(0);
    expect(h.engine.annotations.invalidInput(1, 'r')).toMatchObject({
      rawText: 'A',
      reason: 'notFound',
    });
    expect(h.engine.annotations.invalidInput(2, 'r')).toMatchObject({
      rawText: 'B',
      reason: 'ambiguous',
    });
    expect(h.engine.annotations.invalidInput(3, 'r')).toMatchObject({
      rawText: 'C',
      reason: 'notFound',
    });
    expect(h.rows().map((row) => row['r'])).toEqual([null, null, null]);
    expect(h.notices).toContainEqual({ kind: 'resolutionComplete', resolved: 0, errors: 3 });
  });

  it('discards the late result for a cell written over while pending, applying the rest', () => {
    const h = makeEngine(
      [
        { id: 1, r: 'old1' },
        { id: 2, r: 'old2' },
      ],
      { columns: resolverColumns },
    );
    h.engine.clickCell({ row: 0, col: 0 });
    const result = h.engine.clipboard.paste({ matrix: [['Adam'], ['Adam']] });
    expect(h.engine.annotations.pendingCount()).toBe(2);
    // The user writes one pending cell before the resolution arrives.
    h.engine.applyTransaction([{ rowId: 1, key: 'r', value: 'user' }]);
    h.engine.clipboard.applyResolution(
      result.resolutions[0].id,
      new Map<string, TmLabelResolution<unknown>>([['Adam', { value: 'resolved' }]]),
    );
    expect(h.rows()[0]['r']).toBe('user');
    expect(h.rows()[1]['r']).toBe('resolved');
    expect(h.engine.annotations.pendingCount()).toBe(0);
    expect(h.notices).toContainEqual({ kind: 'resolutionComplete', resolved: 1, errors: 0 });
  });

  it('undo during pending aborts the resolution, restores pre-paste state, and defuses late results', () => {
    const h = makeEngine(
      [
        { id: 1, r: 'old1' },
        { id: 2, r: 'old2' },
      ],
      { columns: resolverColumns },
    );
    h.engine.clickCell({ row: 0, col: 0 });
    const result = h.engine.clipboard.paste({ matrix: [['X'], ['Y']] });
    const request = result.resolutions[0];
    expect(h.engine.history.undo()).toBe(true);
    expect(request.context.signal.aborted).toBe(true);
    expect(h.engine.annotations.pendingCount()).toBe(0);
    expect(h.rows().map((row) => row['r'])).toEqual(['old1', 'old2']);
    // A late resolution for the undone paste is a harmless no-op.
    h.engine.clipboard.applyResolution(
      request.id,
      new Map<string, TmLabelResolution<unknown>>([['X', { value: 'late' }]]),
    );
    expect(h.rows().map((row) => row['r'])).toEqual(['old1', 'old2']);
    expect(h.engine.annotations.invalidCount()).toBe(0);
    expect(h.engine.annotations.pendingCount()).toBe(0);
  });
});

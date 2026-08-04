// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { TM_PARSE_ERROR } from '@tellma/core-ui/contracts';

import { makeEngine, makeRows } from './tm-grid-testing.util';

describe('TmGridEditState', () => {
  describe('openEdit gates', () => {
    it('refuses a per-cell readonly cell', () => {
      const h = makeEngine(makeRows(2), {
        columns: [{ key: 'a', cellReadonly: (row) => row.id === 1 }, { key: 'b' }],
      });
      expect(h.engine.edit.openEdit({ row: 0, col: 0 }, 'edit')).toBe(false);
      expect(h.engine.edit.session()).toBeNull();
      // Sanity: the same column opens on the row the oracle allows.
      expect(h.engine.edit.openEdit({ row: 1, col: 0 }, 'edit')).toBe(true);
    });

    it('refuses a boolean column — the toggle path owns those cells', () => {
      const h = makeEngine([{ id: 1, flag: true }], {
        columns: [{ key: 'flag', type: 'boolean' }],
      });
      expect(h.engine.edit.openEdit({ row: 0, col: 0 }, 'edit')).toBe(false);
      expect(h.engine.edit.session()).toBeNull();
    });

    it('refuses a non-editable column and a non-editable grid', () => {
      const h = makeEngine(makeRows(1), {
        columns: [{ key: 'a', editable: false }, { key: 'b' }],
      });
      expect(h.engine.edit.openEdit({ row: 0, col: 0 }, 'edit')).toBe(false);
      h.editable.set(false);
      expect(h.engine.edit.openEdit({ row: 0, col: 1 }, 'edit')).toBe(false);
      expect(h.engine.edit.session()).toBeNull();
    });

    it('success: the session carries cell, rowId, mode, and seed', () => {
      const h = makeEngine(makeRows(2));
      expect(h.engine.edit.openEdit({ row: 1, col: 1 }, 'enter', 'x')).toBe(true);
      expect(h.engine.edit.session()).toEqual({
        cell: { row: 1, col: 1 },
        rowId: 2,
        mode: 'enter',
        seedText: 'x',
      });
    });
  });

  describe('mode and cancel', () => {
    it('toggleMode flips edit and enter', () => {
      const h = makeEngine(makeRows(1));
      h.engine.edit.openEdit({ row: 0, col: 0 }, 'edit');
      h.engine.edit.toggleMode();
      expect(h.engine.edit.session()?.mode).toBe('enter');
      h.engine.edit.toggleMode();
      expect(h.engine.edit.session()?.mode).toBe('edit');
    });

    it('cancel closes with no history entry and no model change', () => {
      const h = makeEngine(makeRows(1));
      h.engine.edit.openEdit({ row: 0, col: 0 }, 'edit');
      h.engine.edit.cancel();
      expect(h.engine.edit.session()).toBeNull();
      expect(h.engine.history.canUndo()).toBe(false);
      expect(h.rows()[0]['a']).toBe('a1');
    });
  });

  describe('commitText', () => {
    it('parse success writes the value as one cellEdit entry', () => {
      const h = makeEngine(makeRows(1));
      h.engine.edit.openEdit({ row: 0, col: 0 }, 'edit');
      expect(h.engine.edit.commitText('hello')).toBe(true);
      expect(h.engine.edit.session()).toBeNull();
      expect(h.rows()[0]['a']).toBe('hello');
      h.engine.history.undo();
      expect(h.rows()[0]['a']).toBe('a1');
      expect(h.notices).toContainEqual({ kind: 'undoApplied', opKind: 'cellEdit', skippedRows: 0 });
      expect(h.engine.history.canUndo()).toBe(false);
    });

    it('parse error clears the model and records the raw text as an invalid input', () => {
      const h = makeEngine(makeRows(1));
      h.engine.edit.openEdit({ row: 0, col: 0 }, 'edit');
      expect(h.engine.edit.commitText('BAD')).toBe(true);
      expect(h.engine.edit.session()).toBeNull();
      expect(h.rows()[0]['a']).toBeNull();
      expect(h.engine.annotations.invalidInput(1, 'a')).toMatchObject({
        rawText: 'BAD',
        reason: 'parse',
      });
    });

    it('a valid commit over an invalid input clears it; undo restores it', () => {
      const h = makeEngine(makeRows(1));
      h.engine.edit.openEdit({ row: 0, col: 0 }, 'edit');
      h.engine.edit.commitText('BAD');
      h.engine.edit.openEdit({ row: 0, col: 0 }, 'edit');
      h.engine.edit.commitText('ok');
      expect(h.rows()[0]['a']).toBe('ok');
      expect(h.engine.annotations.invalidCount()).toBe(0);
      h.engine.history.undo();
      expect(h.rows()[0]['a']).toBeNull();
      expect(h.engine.annotations.invalidInput(1, 'a')).toMatchObject({
        rawText: 'BAD',
        reason: 'parse',
      });
    });

    it('undo after a parse-error commit restores the prior value and invalid state', () => {
      const h = makeEngine(makeRows(1));
      h.engine.edit.openEdit({ row: 0, col: 0 }, 'edit');
      h.engine.edit.commitText('BAD');
      h.engine.history.undo();
      expect(h.rows()[0]['a']).toBe('a1');
      expect(h.engine.annotations.invalidInput(1, 'a')).toBeUndefined();
    });

    it('applies the column normalization to the parsed value (model = display)', () => {
      const h = makeEngine([{ id: 1, a: 1 }], {
        columns: [
          {
            key: 'a',
            type: 'number',
            parse: (text) => Number(text),
            normalizeValue: (value) => Math.round((value as number) * 100) / 100,
          },
        ],
      });
      h.engine.edit.openEdit({ row: 0, col: 0 }, 'edit');
      expect(h.engine.edit.commitText('1.005678')).toBe(true);
      expect(h.rows()[0]['a']).toBe(1.01);
    });

    it('a normalization rejection clears the model and records reason precision', () => {
      const h = makeEngine([{ id: 1, a: 1 }], {
        columns: [
          {
            key: 'a',
            type: 'number',
            parse: (text) => Number(text),
            normalizeValue: (value) => ((value as number) > 1e15 ? TM_PARSE_ERROR : value),
          },
        ],
      });
      h.engine.edit.openEdit({ row: 0, col: 0 }, 'edit');
      expect(h.engine.edit.commitText('12345678901234567')).toBe(true);
      expect(h.rows()[0]['a']).toBeNull();
      expect(h.engine.annotations.invalidInput(1, 'a')).toMatchObject({
        rawText: '12345678901234567',
        reason: 'precision',
      });
    });

    it('a column without a parse takes the text as-is', () => {
      const h = makeEngine(makeRows(1));
      // makeColumns defaults a parse even for an explicit `parse: undefined`
      // (`??` treats it as absent), so a parse-less column cannot be expressed
      // through the specs — strip the parse off the built column instead.
      h.engine.model.columnAt(0).parse = undefined;
      h.engine.edit.openEdit({ row: 0, col: 0 }, 'edit');
      expect(h.engine.edit.commitText('BAD')).toBe(true);
      expect(h.rows()[0]['a']).toBe('BAD');
      expect(h.engine.annotations.invalidCount()).toBe(0);
    });
  });

  describe('commitValue', () => {
    it('writes the value directly and clears the invalid input', () => {
      const h = makeEngine(makeRows(1));
      h.engine.edit.openEdit({ row: 0, col: 0 }, 'edit');
      h.engine.edit.commitText('BAD');
      h.engine.edit.openEdit({ row: 0, col: 0 }, 'edit');
      expect(h.engine.edit.commitValue(42)).toBe(true);
      expect(h.rows()[0]['a']).toBe(42);
      expect(h.engine.annotations.invalidCount()).toBe(0);
      expect(h.engine.edit.session()).toBeNull();
    });
  });

  describe('placeholder virtual session', () => {
    it('opens with a null rowId on the placeholder row', () => {
      const h = makeEngine(makeRows(3));
      h.engine.clickCell({ row: 3, col: 0 }); // the placeholder row
      expect(h.engine.edit.openEdit({ row: 3, col: 0 }, 'enter', 'h')).toBe(true);
      expect(h.engine.edit.session()?.rowId).toBeNull();
    });

    it('cancel leaves zero trace', () => {
      const h = makeEngine(makeRows(3));
      h.engine.clickCell({ row: 3, col: 0 });
      h.engine.edit.openEdit({ row: 3, col: 0 }, 'enter', 'h');
      h.engine.edit.cancel();
      expect(h.rows()).toHaveLength(3); // no row materialized
      expect(h.engine.history.canUndo()).toBe(false);
      expect(h.engine.model.viewRowCount()).toBe(4);
    });

    it('commit materializes one row plus the write as ONE undo entry', () => {
      const h = makeEngine(makeRows(3));
      h.engine.clickCell({ row: 3, col: 0 });
      h.engine.edit.openEdit({ row: 3, col: 0 }, 'enter', 'h');
      expect(h.engine.edit.commitText('hello')).toBe(true);
      expect(h.rows()).toHaveLength(4); // appended via the factory
      expect(h.rows()[3]['a']).toBe('hello');
      // A fresh placeholder is implicit: view stays data + 1.
      expect(h.engine.model.dataRowCount()).toBe(4);
      expect(h.engine.model.viewRowCount()).toBe(5);
      h.engine.history.undo(); // one undo removes the row entirely
      expect(h.rows()).toHaveLength(3);
      expect(h.engine.history.canUndo()).toBe(false);
    });
  });

  describe('toggleBoolean', () => {
    const setup = () =>
      makeEngine(
        [
          { id: 1, a: 'a1', flag: null },
          { id: 2, a: 'a2', flag: true },
        ],
        {
          columns: [
            { key: 'a' },
            { key: 'flag', type: 'boolean', cellReadonly: (row) => row.id === 2 },
          ],
        },
      );

    it('toggles null → true and true → false, one undo entry each', () => {
      const h = setup();
      expect(h.engine.edit.toggleBoolean({ row: 0, col: 1 })).toBe(true);
      expect(h.rows()[0]['flag']).toBe(true);
      expect(h.engine.edit.toggleBoolean({ row: 0, col: 1 })).toBe(true);
      expect(h.rows()[0]['flag']).toBe(false);
      h.engine.history.undo();
      expect(h.rows()[0]['flag']).toBe(true);
      h.engine.history.undo();
      expect(h.rows()[0]['flag']).toBeNull();
      expect(h.engine.history.canUndo()).toBe(false);
    });

    it('is a no-op on a readonly boolean cell', () => {
      const h = setup();
      expect(h.engine.edit.toggleBoolean({ row: 1, col: 1 })).toBe(false);
      expect(h.rows()[1]['flag']).toBe(true);
      expect(h.engine.history.canUndo()).toBe(false);
    });

    it('materializes the placeholder and writes true as one entry', () => {
      const h = setup();
      expect(h.engine.edit.toggleBoolean({ row: 2, col: 1 })).toBe(true);
      expect(h.rows()).toHaveLength(3);
      expect(h.rows()[2]['flag']).toBe(true);
      h.engine.history.undo();
      expect(h.rows()).toHaveLength(2);
      expect(h.engine.history.canUndo()).toBe(false);
    });
  });

  describe('commitLabel (the editor-commit resolution ladder)', () => {
    /** An entity-like column whose parse handles only the '#N' id form. */
    const entityColumns = (hasResolver: boolean) => [
      {
        key: 'agent',
        type: 'entity' as const,
        hasResolver,
        parse: (text: string) => (/^#\d+$/.test(text) ? Number(text.slice(1)) : TM_PARSE_ERROR),
      },
      { key: 'b' },
    ];

    it('empty text writes the cleared value (the paste ladder empty rung)', () => {
      const h = makeEngine([{ id: 1, agent: 7, b: 'x' }], { columns: entityColumns(true) });
      h.engine.edit.openEdit({ row: 0, col: 0 }, 'edit');
      expect(h.engine.edit.commitLabel('')).toBeNull();
      expect(h.rows()[0]['agent']).toBeNull();
      expect(h.engine.annotations.pendingCount()).toBe(0);
      h.engine.history.undo();
      expect(h.rows()[0]['agent']).toBe(7);
    });

    it('a successful consumer parse commits synchronously with no resolver call', () => {
      const h = makeEngine([{ id: 1, agent: 7, b: 'x' }], { columns: entityColumns(true) });
      h.engine.edit.openEdit({ row: 0, col: 0 }, 'edit');
      expect(h.engine.edit.commitLabel('#42')).toBeNull();
      expect(h.rows()[0]['agent']).toBe(42);
      expect(h.engine.annotations.pendingCount()).toBe(0);
    });

    it('unparseable text on a column WITHOUT a resolver is a definitive invalid input', () => {
      const h = makeEngine([{ id: 1, agent: 7, b: 'x' }], { columns: entityColumns(false) });
      h.engine.edit.openEdit({ row: 0, col: 0 }, 'edit');
      expect(h.engine.edit.commitLabel('Alice')).toBeNull();
      expect(h.rows()[0]['agent']).toBeNull();
      expect(h.engine.annotations.invalidInput(1, 'agent')).toMatchObject({
        rawText: 'Alice',
        reason: 'parse',
      });
    });

    it('a column with neither parse nor resolver records the invalid input — never a raw-text write', () => {
      const h = makeEngine([{ id: 1, agent: 7, b: 'x' }], { columns: entityColumns(false) });
      h.engine.model.columnAt(0).parse = undefined;
      h.engine.edit.openEdit({ row: 0, col: 0 }, 'edit');
      expect(h.engine.edit.commitLabel('Alice')).toBeNull();
      expect(h.rows()[0]['agent']).toBeNull();
      expect(h.engine.annotations.invalidInput(1, 'agent')).toMatchObject({
        rawText: 'Alice',
        reason: 'parse',
      });
    });

    it('the resolver rung clears the cell inside an OPEN entry and returns the addresses', () => {
      const h = makeEngine([{ id: 1, agent: 7, b: 'x' }], { columns: entityColumns(true) });
      h.engine.edit.openEdit({ row: 0, col: 0 }, 'edit');
      const pending = h.engine.edit.commitLabel('Alice');
      expect(pending).toMatchObject({ rowId: 1, columnId: 'agent', columnKey: 'agent' });
      expect(pending?.handle.isOpen).toBe(true);
      expect(h.engine.edit.session()).toBeNull();
      expect(h.rows()[0]['agent']).toBeNull();
    });
  });

  describe('commitEditorLabel (engine end-to-end with the resolver machinery)', () => {
    const setup = (rows = [{ id: 1, agent: 7, b: 'x' }]) =>
      makeEngine(rows, {
        columns: [
          {
            key: 'agent',
            type: 'entity' as const,
            hasResolver: true,
            parse: (text: string) =>
              /^#\d+$/.test(text) ? Number(text.slice(1)) : TM_PARSE_ERROR,
          },
          { key: 'b' },
        ],
      });

    it('a resolved label lands in the SAME entry: one undo restores the pre-edit state', () => {
      const h = setup();
      h.engine.edit.openEdit({ row: 0, col: 0 }, 'edit');
      const request = h.engine.commitEditorLabel('Alice');
      expect(request).not.toBeNull();
      expect(request!.labels).toEqual(['Alice']);
      expect(request!.context.sourceLocale).toBeUndefined();
      expect(request!.context.foreignSource).toBeUndefined();
      expect(h.engine.annotations.pendingCount()).toBe(1);
      h.engine.clipboard.applyResolution(request!.id, new Map([['Alice', { value: 3 }]]));
      expect(h.rows()[0]['agent']).toBe(3);
      expect(h.engine.annotations.pendingCount()).toBe(0);
      expect(h.notices).toContainEqual({ kind: 'resolutionComplete', resolved: 1, errors: 0 });
      h.engine.history.undo();
      expect(h.rows()[0]['agent']).toBe(7);
      expect(h.engine.history.canUndo()).toBe(false);
    });

    it('notFound and ambiguous become invalid inputs with the raw text kept', () => {
      const h = setup();
      h.engine.edit.openEdit({ row: 0, col: 0 }, 'edit');
      const request = h.engine.commitEditorLabel('Alice');
      h.engine.clipboard.applyResolution(request!.id, new Map([['Alice', { error: 'ambiguous' }]]));
      expect(h.rows()[0]['agent']).toBeNull();
      expect(h.engine.annotations.invalidInput(1, 'agent')).toMatchObject({
        rawText: 'Alice',
        reason: 'ambiguous',
      });
    });

    it('a rejected resolver records the retryable resolutionFailed reason', () => {
      const h = setup();
      h.engine.edit.openEdit({ row: 0, col: 0 }, 'edit');
      const request = h.engine.commitEditorLabel('Alice');
      h.engine.clipboard.applyResolution(request!.id, new Map(), { failed: true });
      expect(h.engine.annotations.invalidInput(1, 'agent')).toMatchObject({
        rawText: 'Alice',
        reason: 'resolutionFailed',
      });
    });

    it('undo while the resolution is pending aborts it and restores the value', () => {
      const h = setup();
      h.engine.edit.openEdit({ row: 0, col: 0 }, 'edit');
      const request = h.engine.commitEditorLabel('Alice');
      expect(h.engine.annotations.pendingCount()).toBe(1);
      h.engine.history.undo();
      expect(request!.context.signal.aborted).toBe(true);
      expect(h.engine.annotations.pendingCount()).toBe(0);
      expect(h.rows()[0]['agent']).toBe(7);
      // The late outcome is a no-op (the request was withdrawn).
      h.engine.clipboard.applyResolution(request!.id, new Map([['Alice', { value: 3 }]]));
      expect(h.rows()[0]['agent']).toBe(7);
    });

    it('a manual edit over the pending cell makes the late resolution stale', () => {
      const h = setup();
      h.engine.edit.openEdit({ row: 0, col: 0 }, 'edit');
      const request = h.engine.commitEditorLabel('Alice');
      h.engine.edit.openEdit({ row: 0, col: 0 }, 'edit');
      h.engine.edit.commitLabel('#9'); // the parse rung — a typed commit over the pending cell
      expect(h.rows()[0]['agent']).toBe(9);
      h.engine.clipboard.applyResolution(request!.id, new Map([['Alice', { value: 3 }]]));
      expect(h.rows()[0]['agent']).toBe(9); // the stale outcome never landed
    });

    it('a placeholder session materializes the row; one undo removes it entirely', () => {
      const h = setup();
      h.engine.clickCell({ row: 1, col: 0 }); // the placeholder row
      h.engine.edit.openEdit({ row: 1, col: 0 }, 'enter', 'A');
      const request = h.engine.commitEditorLabel('Alice');
      expect(request).not.toBeNull();
      expect(h.rows()).toHaveLength(2);
      h.engine.clipboard.applyResolution(request!.id, new Map([['Alice', { value: 3 }]]));
      expect(h.rows()[1]['agent']).toBe(3);
      h.engine.history.undo();
      expect(h.rows()).toHaveLength(1);
      expect(h.engine.history.canUndo()).toBe(false);
    });
  });

  describe('relocateSession (via reconcile)', () => {
    it('follows the edited row to its new view index', () => {
      const h = makeEngine(makeRows(3));
      h.engine.edit.openEdit({ row: 1, col: 0 }, 'edit');
      const current = h.rows();
      h.externalChange([current[2], current[0], current[1]]);
      expect(h.engine.edit.session()).toMatchObject({ rowId: 2, cell: { row: 2, col: 0 } });
    });

    it('cancels the session when the edited row is removed', () => {
      const h = makeEngine(makeRows(3));
      h.engine.edit.openEdit({ row: 1, col: 0 }, 'edit');
      const current = h.rows();
      h.externalChange([current[0], current[2]]);
      expect(h.engine.edit.session()).toBeNull();
      expect(h.notices).toContainEqual({ kind: 'editorCancelledRowRemoved' });
    });
  });
});

// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { signal } from '@angular/core';

import { TmGridDataModel } from './tm-grid-data-model';
import type { TmGridWarning } from './tm-grid-host';
import { makeColumns, makeEngine, makeRows, type TestRow } from './tm-grid-testing.util';

/** A bare data model over signal inputs — no engine, no placeholder. */
function makeModel(initialRows: readonly TestRow[]) {
  const rows = signal<readonly TestRow[]>(initialRows);
  const warnings: TmGridWarning[] = [];
  const columns = makeColumns([{ key: 'a' }]);
  const model = new TmGridDataModel<TestRow>({
    rows: () => rows(),
    rowId: (row) => row.id,
    columns: () => columns,
    editable: () => true,
    canAddRows: () => false,
    host: { onWarn: (warning) => warnings.push(warning) },
  });
  return { model, rows, warnings };
}

describe('TmGridDataModel (flat)', () => {
  it('maps the rows array to view rows in model order at level 0', () => {
    const { engine } = makeEngine(makeRows(3));
    const views = engine.model.viewRows();
    expect(views.map((view) => view.id)).toEqual([1, 2, 3]);
    expect(views.map((view) => view.modelIndex)).toEqual([0, 1, 2]);
    expect(views.every((view) => view.level === 0)).toBe(true);
    expect(views.every((view) => view.parentId === null)).toBe(true);
    expect(views.every((view) => !view.expandable && !view.expanded)).toBe(true);
  });

  it('constructs directly over signal inputs without the engine', () => {
    const { model } = makeModel(makeRows(2, ['a']));
    expect(model.viewRows().map((view) => view.id)).toEqual([1, 2]);
    expect(model.hasPlaceholder()).toBe(false); // canAddRows is off
    expect(model.viewRowCount()).toBe(2);
  });

  it('answers viewIndexOfRow / modelIndexOfRow, -1 for absent rows', () => {
    const { engine } = makeEngine(makeRows(3));
    expect(engine.model.viewIndexOfRow(2)).toBe(1);
    expect(engine.model.modelIndexOfRow(3)).toBe(2);
    expect(engine.model.viewIndexOfRow('absent')).toBe(-1);
    expect(engine.model.modelIndexOfRow('absent')).toBe(-1);
  });

  it('answers rowById, undefined for absent rows', () => {
    const harness = makeEngine(makeRows(3));
    expect(harness.engine.model.rowById(2)).toBe(harness.rows()[1]);
    expect(harness.engine.model.rowById('absent')).toBeUndefined();
  });

  it('answers columnAt and columnIndexOf', () => {
    const { engine } = makeEngine(makeRows(1));
    expect(engine.model.columnAt(1).id).toBe('b');
    expect(engine.model.columnAt(9)).toBeUndefined();
    expect(engine.model.columnIndexOf('c')).toBe(2);
    expect(engine.model.columnIndexOf('absent')).toBe(-1);
    expect(engine.model.columnCount()).toBe(3);
  });

  it('counts model rows including rows the view excludes', () => {
    const { engine, rows } = makeEngine(makeRows(3));
    expect(engine.model.modelRowCount()).toBe(3);
    expect(engine.model.modelRowCount()).toBe(rows().length);
  });
});

describe('TmGridDataModel cell values and text', () => {
  it('reads cell values and text through the column oracles', () => {
    const { engine } = makeEngine(makeRows(2));
    expect(engine.model.cellValue({ row: 0, col: 0 })).toBe('a1');
    expect(engine.model.cellText({ row: 1, col: 2 })).toBe('c2');
  });

  it('renders null and undefined values as empty text (null value)', () => {
    const { engine } = makeEngine([{ id: 1, a: null, b: undefined }]);
    expect(engine.model.cellText({ row: 0, col: 0 })).toBe('');
    expect(engine.model.cellText({ row: 0, col: 1 })).toBe('');
    expect(engine.model.cellText({ row: 0, col: 2 })).toBe(''); // property absent
    expect(engine.model.cellValue({ row: 0, col: 0 })).toBeNull();
    expect(engine.model.cellValue({ row: 0, col: 1 })).toBeNull();
  });

  it('renders boolean cells as TRUE/FALSE text', () => {
    const { engine } = makeEngine(
      [
        { id: 1, flag: true },
        { id: 2, flag: false },
        { id: 3, flag: null },
      ],
      { columns: [{ key: 'flag', type: 'boolean' }] },
    );
    expect(engine.model.cellText({ row: 0, col: 0 })).toBe('TRUE');
    expect(engine.model.cellText({ row: 1, col: 0 })).toBe('FALSE');
    expect(engine.model.cellText({ row: 2, col: 0 })).toBe('');
  });

  it('yields null value and empty text on the placeholder row', () => {
    const { engine } = makeEngine(makeRows(1));
    const placeholder = engine.model.placeholderIndex();
    expect(engine.model.cellValue({ row: placeholder, col: 0 })).toBeNull();
    expect(engine.model.cellText({ row: placeholder, col: 0 })).toBe('');
  });
});

describe('TmGridDataModel.isCellEditable', () => {
  it('is editable when the grid, the column, and the cell all allow it', () => {
    const { engine } = makeEngine(makeRows(2));
    expect(engine.model.isCellEditable({ row: 0, col: 0 })).toBe(true);
  });

  it('folds the editable signal — nothing is editable while it is off', () => {
    const harness = makeEngine(makeRows(2));
    harness.editable.set(false);
    expect(harness.engine.model.isCellEditable({ row: 0, col: 0 })).toBe(false);
  });

  it('folds column.editable', () => {
    const { engine } = makeEngine(makeRows(1), {
      columns: [{ key: 'a' }, { key: 'b', editable: false }],
    });
    expect(engine.model.isCellEditable({ row: 0, col: 0 })).toBe(true);
    expect(engine.model.isCellEditable({ row: 0, col: 1 })).toBe(false);
  });

  it('folds the per-cell readonly oracle on data rows', () => {
    const { engine } = makeEngine(makeRows(2), {
      columns: [{ key: 'a' }, { key: 'b', cellReadonly: (row) => row.id === 1 }],
    });
    expect(engine.model.isCellEditable({ row: 0, col: 1 })).toBe(false);
    expect(engine.model.isCellEditable({ row: 1, col: 1 })).toBe(true);
    expect(engine.model.isCellEditable({ row: 0, col: 0 })).toBe(true);
  });

  it('treats placeholder cells as editable whenever the column is', () => {
    const { engine } = makeEngine(makeRows(1), {
      columns: [
        { key: 'a' },
        { key: 'b', editable: false },
        { key: 'c', cellReadonly: () => true }, // no field state exists before materialization
      ],
    });
    const placeholder = engine.model.placeholderIndex();
    expect(engine.model.isCellEditable({ row: placeholder, col: 0 })).toBe(true);
    expect(engine.model.isCellEditable({ row: placeholder, col: 1 })).toBe(false);
    expect(engine.model.isCellEditable({ row: placeholder, col: 2 })).toBe(true);
    expect(engine.model.isCellEditable({ row: 0, col: 2 })).toBe(false); // the data row still folds it
  });

  it('rejects out-of-range cells', () => {
    const { engine } = makeEngine(makeRows(1));
    expect(engine.model.isCellEditable({ row: 5, col: 0 })).toBe(false);
    expect(engine.model.isCellEditable({ row: 0, col: 9 })).toBe(false);
  });
});

describe('TmGridDataModel placeholder accounting', () => {
  it('has a placeholder exactly when editable and canAddRows are both on', () => {
    const harness = makeEngine(makeRows(2));
    expect(harness.engine.model.hasPlaceholder()).toBe(true);
    harness.canAddRows.set(false);
    expect(harness.engine.model.hasPlaceholder()).toBe(false);
    harness.canAddRows.set(true);
    harness.editable.set(false);
    expect(harness.engine.model.hasPlaceholder()).toBe(false);
  });

  it('counts the placeholder into viewRowCount and places it after the data rows', () => {
    const { engine } = makeEngine(makeRows(2));
    expect(engine.model.dataRowCount()).toBe(2);
    expect(engine.model.viewRowCount()).toBe(3);
    expect(engine.model.placeholderIndex()).toBe(2);
    expect(engine.model.isPlaceholder(2)).toBe(true);
    expect(engine.model.isPlaceholder(0)).toBe(false);
    expect(engine.model.rowAt(2)).toBeNull();
    expect(engine.model.viewRows().length).toBe(2); // viewRows excludes the placeholder
  });

  it('reports no placeholder index while the placeholder is absent', () => {
    const { engine } = makeEngine(makeRows(2), { canAddRows: false });
    expect(engine.model.viewRowCount()).toBe(2);
    expect(engine.model.placeholderIndex()).toBe(-1);
    expect(engine.model.isPlaceholder(2)).toBe(false);
  });

  it('removes the placeholder reactively when the editable signal flips off', () => {
    const harness = makeEngine(makeRows(2));
    expect(harness.engine.model.viewRowCount()).toBe(3);
    harness.editable.set(false);
    expect(harness.engine.model.hasPlaceholder()).toBe(false);
    expect(harness.engine.model.viewRowCount()).toBe(2);
    expect(harness.engine.model.placeholderIndex()).toBe(-1);
    expect(harness.engine.model.isPlaceholder(2)).toBe(false);
  });
});

describe('TmGridDataModel duplicate row ids', () => {
  const duplicated = (): TestRow[] => [
    { id: 1, a: 'first' },
    { id: 2, a: 'second' },
    { id: 1, a: 'dup' },
  ];

  it('keeps the first occurrence in the maps and excludes the duplicate from the view', () => {
    const { engine } = makeEngine(duplicated());
    expect(engine.model.viewRows().map((view) => view.id)).toEqual([1, 2]);
    expect(engine.model.rowById(1)?.['a']).toBe('first');
    expect(engine.model.modelIndexOfRow(1)).toBe(0);
    expect(engine.model.dataRowCount()).toBe(2);
    expect(engine.model.modelRowCount()).toBe(3); // the model count keeps the raw array length
  });

  it('warns duplicateRowId exactly once, surviving re-reads and recomputes', () => {
    const harness = makeEngine(duplicated());
    const duplicateWarnings = () =>
      harness.warnings.filter((warning) => warning.kind === 'duplicateRowId');
    expect(duplicateWarnings()).toEqual([{ kind: 'duplicateRowId', rowId: 1 }]);
    // Re-reads do not re-report.
    harness.engine.model.viewRows();
    harness.engine.model.viewRows();
    expect(duplicateWarnings().length).toBe(1);
    // A rows change recomputes the structure; the dedup set still holds.
    harness.externalChange(duplicated());
    harness.engine.model.viewRows();
    expect(duplicateWarnings().length).toBe(1);
  });

  it('deduplicates warnings on a directly constructed model as the rows signal changes', () => {
    const { model, rows, warnings } = makeModel(duplicated());
    model.viewRows(); // first read resolves the structure and reports
    expect(warnings).toEqual([{ kind: 'duplicateRowId', rowId: 1 }]);
    rows.set(duplicated());
    model.viewRows();
    expect(warnings.length).toBe(1);
  });
});

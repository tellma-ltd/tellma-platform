// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

// Shared vitest helpers for the engine specs. NOT exported from the entry
// point — test-only plumbing.

import { signal, type WritableSignal } from '@angular/core';

import { TM_PARSE_ERROR, type TmRowId } from '@tellma/core-ui/contracts';

import { TmGridEngine } from './tm-grid-engine';
import type {
  TmGridEngineOptions,
  TmGridModelWriter,
  TmGridNotice,
  TmGridWarning,
} from './tm-grid-host';
import type { TmGridEngineColumn, TmGridColumnType, TmGridTreeOptions } from './tm-grid-types';

/** The row shape most engine specs use: an id plus arbitrary cell values. */
export interface TestRow {
  id: TmRowId;
  [key: string]: unknown;
}

/** Per-column knobs for `makeColumns`. */
export interface TestColumnSpec {
  readonly key: string;
  readonly type?: TmGridColumnType;
  readonly header?: string;
  readonly editable?: boolean;
  /** Per-cell readonly oracle (defaults to never readonly). */
  readonly cellReadonly?: (row: TestRow) => boolean;
  /** Custom parse; the default trims and fails on the literal 'BAD'. */
  readonly parse?: (text: string) => unknown | typeof TM_PARSE_ERROR;
  readonly hasResolver?: boolean;
  readonly clearedValue?: unknown;
}

/** Builds engine column oracles over `TestRow`s. */
export function makeColumns(specs: readonly TestColumnSpec[]): TmGridEngineColumn<TestRow>[] {
  return specs.map((spec) => ({
    key: spec.key,
    id: spec.key,
    type: spec.type ?? 'text',
    headerLabel: () => spec.header ?? spec.key,
    getValue: (row: TestRow) => row[spec.key],
    getText: (row: TestRow) => {
      const value = row[spec.key];
      if (value === null || value === undefined) {
        return '';
      }
      if ((spec.type ?? 'text') === 'boolean') {
        return value === true ? 'TRUE' : 'FALSE';
      }
      return String(value);
    },
    editable: spec.editable ?? true,
    isCellReadonly: (row: TestRow) => spec.cellReadonly?.(row) ?? false,
    parse: spec.parse ?? ((text: string) => (text === 'BAD' ? TM_PARSE_ERROR : text)),
    hasResolver: spec.hasResolver ?? false,
    clearedValue: spec.clearedValue ?? ((spec.type ?? 'text') === 'boolean' ? false : null),
  }));
}

let nextNewRowId = 1;

/** A writer over a signal-backed array — what the grid layer implements over the field tree. */
export class FakeWriter implements TmGridModelWriter<TestRow> {
  constructor(
    private readonly rows: WritableSignal<readonly TestRow[]>,
    private readonly makeRow: (parentRowId: TmRowId | null) => TestRow = (parent) => ({
      id: `new-${nextNewRowId++}`,
      parentId: parent,
    }),
  ) {}

  setCellValue(rowId: TmRowId, columnKey: string, value: unknown): void {
    this.rows.update((rows) =>
      rows.map((row) => (row.id === rowId ? { ...row, [columnKey]: value } : row)),
    );
  }

  insertNewRows(
    modelIndex: number,
    count: number,
    parentRowId?: TmRowId | null,
  ): ReadonlyArray<{ readonly id: TmRowId; readonly row: TestRow }> {
    const created = Array.from({ length: count }, () => this.makeRow(parentRowId ?? null));
    this.rows.update((rows) => {
      const next = [...rows];
      next.splice(Math.min(Math.max(0, modelIndex), next.length), 0, ...created);
      return next;
    });
    return created.map((row) => ({ id: row.id, row }));
  }

  reinsertRows(rows: ReadonlyArray<{ readonly row: TestRow; readonly modelIndex: number }>): void {
    this.rows.update((current) => {
      const next = [...current];
      for (const { row, modelIndex } of [...rows].sort((a, b) => a.modelIndex - b.modelIndex)) {
        next.splice(Math.min(Math.max(0, modelIndex), next.length), 0, row);
      }
      return next;
    });
  }

  removeRows(rowIds: readonly TmRowId[]): void {
    const ids = new Set(rowIds);
    this.rows.update((rows) => rows.filter((row) => !ids.has(row.id)));
  }

  moveRows(rowIds: readonly TmRowId[], beforeRowId: TmRowId | null): void {
    const ids = new Set(rowIds);
    this.rows.update((rows) => {
      const moved = rowIds
        .map((id) => rows.find((row) => row.id === id))
        .filter((row): row is TestRow => row !== undefined);
      const rest = rows.filter((row) => !ids.has(row.id));
      const at = beforeRowId === null ? rest.length : rest.findIndex((row) => row.id === beforeRowId);
      const index = at === -1 ? rest.length : at;
      return [...rest.slice(0, index), ...moved, ...rest.slice(index)];
    });
  }
}

/** Everything a spec needs from `makeEngine`. */
export interface TestHarness {
  readonly engine: TmGridEngine<TestRow>;
  readonly rows: WritableSignal<readonly TestRow[]>;
  readonly editable: WritableSignal<boolean>;
  readonly canAddRows: WritableSignal<boolean>;
  readonly direction: WritableSignal<'ltr' | 'rtl'>;
  readonly pageSize: WritableSignal<number>;
  readonly notices: TmGridNotice[];
  readonly warnings: TmGridWarning[];
  readonly writer: FakeWriter;
  /** Reconciles after an EXTERNAL rows change (the component-layer effect). */
  externalChange(next: readonly TestRow[]): void;
}

/** Options of {@link makeEngine}. */
export interface MakeEngineOptions {
  readonly columns?: readonly TestColumnSpec[];
  readonly editable?: boolean;
  readonly canAddRows?: boolean;
  readonly readonlyBinding?: boolean;
  readonly tree?: TmGridTreeOptions<TestRow>;
  readonly locale?: string;
  readonly tenant?: string;
  readonly pageSize?: number;
  readonly historyCapacity?: number;
}

/** Constructs a full engine over signal inputs — no TestBed, no DOM. */
export function makeEngine(
  initialRows: readonly TestRow[],
  options: MakeEngineOptions = {},
): TestHarness {
  const rows = signal<readonly TestRow[]>(initialRows);
  const editable = signal(options.editable ?? true);
  const canAddRows = signal(options.canAddRows ?? true);
  const direction = signal<'ltr' | 'rtl'>('ltr');
  const pageSize = signal(options.pageSize ?? 10);
  const notices: TmGridNotice[] = [];
  const warnings: TmGridWarning[] = [];
  const writer = new FakeWriter(rows);
  const engineOptions: TmGridEngineOptions<TestRow> = {
    rows: () => rows(),
    rowId: (row) => row.id,
    columns: () => columns,
    editable: () => editable(),
    canAddRows: () => canAddRows(),
    locale: () => options.locale ?? 'en',
    tenant: () => options.tenant,
    direction: () => direction(),
    pageSize: () => pageSize(),
    tree: options.tree,
    historyCapacity: options.historyCapacity,
    host: {
      writer: options.readonlyBinding ? undefined : writer,
      onNotice: (notice) => notices.push(notice),
      onWarn: (warning) => warnings.push(warning),
    },
  };
  const columns = makeColumns(
    options.columns ?? [{ key: 'a' }, { key: 'b' }, { key: 'c' }],
  );
  const engine = new TmGridEngine<TestRow>(engineOptions);
  return {
    engine,
    rows,
    editable,
    canAddRows,
    direction,
    pageSize,
    notices,
    warnings,
    writer,
    externalChange: (next) => {
      rows.set(next);
      engine.reconcile();
    },
  };
}

/** Shorthand: rows `{ id: 1..n, a: 'a1'.., b: 'b1'.., c: 'c1'.. }`. */
export function makeRows(count: number, keys: readonly string[] = ['a', 'b', 'c']): TestRow[] {
  return Array.from({ length: count }, (_, i) => {
    const row: TestRow = { id: i + 1 };
    for (const key of keys) {
      row[key] = `${key}${i + 1}`;
    }
    return row;
  });
}

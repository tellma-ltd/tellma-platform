// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

// The engine's model writer implemented over the consumer's Signal Forms
// field tree: cell writes go through the CHILD field so the consumer's
// `applyEach` schema validates every write, structural operations
// (insert/remove/reinsert/move) splice a copy of the array through the
// root field's value signal. Constructed only for the `field` binding —
// the `data` binding stays writer-less (the readonly contract).

import { untracked } from '@angular/core';
import type { FieldState, FieldTree } from '@angular/forms/signals';

import type { TmRowId } from '@tellma/core-ui/contracts';
import type { TmGridModelWriter } from '@tellma/core-ui/grid-engine';

/**
 * The field node of the row at `index` under an array field tree, or
 * `undefined` past the end. `FieldTree<T[]>` IS array-indexable
 * (`ReadonlyArrayLike` of row field trees), but the library expresses that
 * through a conditional type TypeScript cannot narrow for a generic `T` —
 * hence the localized cast through `unknown`.
 */
export function ɵtmRowField<T>(tree: FieldTree<T[]>, index: number): FieldTree<T> | undefined {
  return (tree as unknown as ReadonlyArray<FieldTree<T> | undefined>)[index];
}

/**
 * The child field node under a row field for a column key, or `undefined`
 * when the row's value carries no such property (Signal Forms materializes
 * child nodes from the value's own keys). Same localized cast rationale as
 * {@link ɵtmRowField}.
 */
export function ɵtmChildField<T>(
  row: FieldTree<T>,
  key: string,
): FieldTree<unknown> | undefined {
  return (row as unknown as Readonly<Record<string, FieldTree<unknown> | undefined>>)[key];
}

/** Everything the field writer needs from the composition root, lazily. */
export interface ɵTmGridFieldWriterDeps<T> {
  /** The bound field tree (read per operation — the binding may change). */
  field(): FieldTree<T[]> | undefined;
  /** The consumer's new-row factory, when bound. */
  newRow(): ((parent?: T) => T) | undefined;
  /** Reads a row's stable identity. */
  rowId(row: T): TmRowId;
  /** The row's model-array index, or -1 (resolved through the engine model). */
  modelIndexOfRow(rowId: TmRowId): number;
  /** The row object by id (parent resolution for tree inserts). */
  rowById(rowId: TmRowId): T | undefined;
}

/**
 * `TmGridModelWriter` over a Signal Forms `FieldTree<T[]>`. Every method
 * reads the CURRENT tree and rows so external rebinds are always honored;
 * all reads run untracked — writers execute inside event pipelines and
 * history replay, never inside reactive computations.
 */
export class ɵTmGridFieldWriter<T> implements TmGridModelWriter<T> {
  constructor(private readonly deps: ɵTmGridFieldWriterDeps<T>) {}

  /** Writes one cell through the row's child field so validators run. */
  setCellValue(rowId: TmRowId, columnKey: string, value: unknown): void {
    untracked(() => {
      const tree = this.deps.field();
      if (tree === undefined) {
        return;
      }
      const index = this.deps.modelIndexOfRow(rowId);
      if (index === -1) {
        return; // the row vanished; reconcile already handled the fallout
      }
      const rowField = ɵtmRowField(tree, index);
      if (rowField === undefined) {
        return;
      }
      const child = ɵtmChildField(rowField, columnKey);
      if (child !== undefined) {
        child().value.set(value);
        return;
      }
      // The property is absent from the row's value, so no child node exists
      // yet — write through the row field to materialize it. The cast is the
      // generic-`T`-as-object boundary; row values are records by contract.
      (rowField() as FieldState<T>).value.update(
        (row) => ({ ...(row as Record<string, unknown>), [columnKey]: value }) as T,
      );
    });
  }

  /** Creates rows via the consumer factory and splices them into the model. */
  insertNewRows(
    modelIndex: number,
    count: number,
    parentRowId?: TmRowId | null,
  ): ReadonlyArray<{ readonly id: TmRowId; readonly row: T }> {
    return untracked(() => {
      const tree = this.deps.field();
      const factory = this.deps.newRow();
      if (tree === undefined || factory === undefined || count < 1) {
        return [];
      }
      const parentRow =
        parentRowId === undefined || parentRowId === null
          ? undefined
          : this.deps.rowById(parentRowId);
      const created: T[] = [];
      for (let i = 0; i < count; i++) {
        created.push(parentRow === undefined ? factory() : factory(parentRow));
      }
      const current = tree().value();
      const at = Math.max(0, Math.min(modelIndex, current.length));
      const next = [...current.slice(0, at), ...created, ...current.slice(at)];
      tree().value.set(next);
      return created.map((row) => ({ id: this.deps.rowId(row), row }));
    });
  }

  /** Re-splices previously removed row OBJECTS at their recorded indexes. */
  reinsertRows(rows: ReadonlyArray<{ readonly row: T; readonly modelIndex: number }>): void {
    untracked(() => {
      const tree = this.deps.field();
      if (tree === undefined || rows.length === 0) {
        return;
      }
      const next = [...tree().value()];
      // Ascending order restores each row at its original position even as
      // earlier reinsertions grow the array under the later ones.
      for (const { row, modelIndex } of [...rows].sort((a, b) => a.modelIndex - b.modelIndex)) {
        next.splice(Math.max(0, Math.min(modelIndex, next.length)), 0, row);
      }
      tree().value.set(next);
    });
  }

  /** Removes the rows with the given ids. */
  removeRows(rowIds: readonly TmRowId[]): void {
    untracked(() => {
      const tree = this.deps.field();
      if (tree === undefined || rowIds.length === 0) {
        return;
      }
      const ids = new Set<TmRowId>(rowIds);
      const current = tree().value();
      const next = current.filter((row) => !ids.has(this.deps.rowId(row)));
      if (next.length !== current.length) {
        tree().value.set(next);
      }
    });
  }

  /** Re-splices existing rows, in the order given, before `beforeRowId`. */
  moveRows(rowIds: readonly TmRowId[], beforeRowId: TmRowId | null): void {
    untracked(() => {
      const tree = this.deps.field();
      if (tree === undefined || rowIds.length === 0) {
        return;
      }
      const current = tree().value();
      const byId = new Map<TmRowId, T>();
      for (const row of current) {
        byId.set(this.deps.rowId(row), row);
      }
      const moving: T[] = [];
      const movingIds = new Set<TmRowId>();
      for (const id of rowIds) {
        const row = byId.get(id);
        if (row !== undefined) {
          moving.push(row);
          movingIds.add(id);
        }
      }
      if (moving.length === 0) {
        return;
      }
      const next = current.filter((row) => !movingIds.has(this.deps.rowId(row)));
      let at = next.length;
      if (beforeRowId !== null) {
        const anchor = next.findIndex((row) => this.deps.rowId(row) === beforeRowId);
        if (anchor !== -1) {
          at = anchor;
        }
      }
      next.splice(at, 0, ...moving);
      tree().value.set(next);
    });
  }
}

// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import {
  booleanAttribute,
  contentChild,
  contentChildren,
  DestroyRef,
  Directive,
  effect,
  ElementRef,
  inject,
  Injector,
  input,
  isDevMode,
  LOCALE_ID,
  model,
  signal,
  type Signal,
} from '@angular/core';
import { LiveAnnouncer } from '@angular/cdk/a11y';
import { Directionality } from '@angular/cdk/bidi';
import type { FieldTree } from '@angular/forms/signals';

import type { TmCellEdit, TmRowId } from '@tellma/core-ui/contracts';
import { TM_UI_TRANSLATE } from '@tellma/core-ui';
import type { TmMenuItem } from '@tellma/core-ui/menu';

import { TmGridColumn } from '../tm-grid-column';
import { TmGridEmptyDef, TmGridLoadingDef } from '../tm-grid-templates';
import { TmGridStateStore } from '../tm-grid-state-store';
import { ɵTmGridCore } from './grid-core';

/**
 * The shared shell of `tm-grid` (and the future `tm-tree-grid`): declares
 * the inputs, content queries, and public members both components carry,
 * and constructs the composition root (`ɵTmGridCore`) from the host's
 * injected dependencies. Selector-less by design — concrete grids extend
 * it and render `ɵTmGridView`.
 */
@Directive()
export abstract class ɵTmGridBase<T> {
  /**
   * Stable identity of this grid definition — the key column widths (and,
   * with `contentKey`, scroll/selection/undo state) are remembered under.
   * Two grids that are live at the same time must not share a `gridId`.
   */
  readonly gridId = input.required<string>();
  /**
   * Identity of the bound content (an invoice id, …). Scroll, selection,
   * and undo state are remembered per `gridId` + `contentKey`; a different
   * key starts fresh at the origin.
   */
  readonly contentKey = input<string | number | undefined>(undefined);
  /** The readonly rows binding. Exactly one of `data`/`field` must be bound. */
  readonly data = input<readonly T[] | undefined>(undefined);
  /**
   * The editable binding: a Signal Forms field tree over the rows array.
   * Rows come from its value; the grid is editable while `readonly` is off.
   */
  readonly field = input<FieldTree<T[]> | undefined>(undefined);
  /**
   * Reads a row's stable identity. Required: selection stability, undo,
   * view reuse, and state memory all key on it, never on the index.
   */
  readonly rowId = input.required<(row: T) => TmRowId>();
  /** With `field` bound, toggles view/edit of the same screen. */
  readonly readonly = input(false, { transform: booleanAttribute });
  /**
   * The new-row factory. Binding it enables the new-row placeholder and
   * paste-overflow row creation; new rows must carry client-side ids.
   */
  readonly newRow = input<((parent?: T) => T) | undefined>(undefined);
  /** Shows the loading overlay (headers stay rendered) and sets `aria-busy`. */
  readonly loading = input(false, { transform: booleanAttribute });
  /** Enables the find bar (a later milestone renders it). */
  readonly searchable = input(false, { transform: booleanAttribute });
  /** Enables row checkbox selection (a later milestone renders it; readonly grids only). */
  readonly selectable = input(false, { transform: booleanAttribute });
  /** Extra context-menu items appended after the built-ins. */
  readonly extraMenuItems = input<readonly TmMenuItem[]>([]);
  /** Row density. */
  readonly size = input<'sm' | 'md' | 'lg'>('md');

  /** The checked row ids of a `selectable` grid (a later milestone wires the checkboxes). */
  readonly selectedIds = model<ReadonlySet<string | number>>(new Set());

  /** The projected column definitions, in display order. */
  readonly columns = contentChildren(TmGridColumn);
  /** The projected empty-state template, if any. */
  readonly emptyDef = contentChild(TmGridEmptyDef);
  /** The projected loading-state template, if any. */
  readonly loadingDef = contentChild(TmGridLoadingDef);

  /** The composition root the view renders from. */
  protected readonly core: ɵTmGridCore<T>;

  /**
   * Count of distinct cells in error state: held invalid inputs plus the
   * bound field's validation-errored cells. Consumers gate Save buttons on
   * it.
   */
  readonly errorCount: Signal<number>;
  /** Count of cells awaiting async paste resolutions. */
  readonly pendingCount: Signal<number>;

  constructor() {
    const elementRef = inject(ElementRef) as ElementRef<HTMLElement>;
    const injector = inject(Injector);
    const destroyRef = inject(DestroyRef);
    const directionality = inject(Directionality);
    const announcer = inject(LiveAnnouncer);
    const translate = inject(TM_UI_TRANSLATE);
    const store = inject(TmGridStateStore);
    const locale = inject(LOCALE_ID);

    const direction = signal<'ltr' | 'rtl'>(directionality.value === 'rtl' ? 'rtl' : 'ltr');
    const directionSubscription = directionality.change.subscribe((value) =>
      direction.set(value === 'rtl' ? 'rtl' : 'ltr'),
    );
    destroyRef.onDestroy(() => directionSubscription.unsubscribe());

    this.core = new ɵTmGridCore<T>({
      host: elementRef.nativeElement,
      injector,
      destroyRef,
      direction: direction.asReadonly(),
      announcer,
      translate,
      store,
      locale,
      gridId: this.gridId,
      contentKey: this.contentKey,
      data: this.data,
      field: this.field,
      rowId: this.rowId,
      readonlyInput: this.readonly,
      newRow: this.newRow,
      loading: this.loading,
      searchable: this.searchable,
      selectable: this.selectable,
      size: this.size,
      extraMenuItems: this.extraMenuItems,
      // The query token is the generic directive class, so the query signal
      // erases T; the rows these directives read are this grid's rows.
      columns: this.columns as Signal<ReadonlyArray<TmGridColumn<T, unknown>>>,
      emptyDef: this.emptyDef,
      loadingDef: this.loadingDef,
    });
    this.errorCount = this.core.errorCount;
    this.pendingCount = this.core.pendingCount;

    if (isDevMode()) {
      // Exactly one of data/field must be bound — a configuration error,
      // surfaced loudly in dev mode.
      effect(() => {
        const hasData = this.data() !== undefined;
        const hasField = this.field() !== undefined;
        if (hasData === hasField) {
          throw new Error(
            hasData
              ? 'tm-grid: [data] and [field] are both bound — bind exactly one.'
              : 'tm-grid: neither [data] nor [field] is bound — bind exactly one.',
          );
        }
      });
    }
  }

  /**
   * Registers consumer edits as ONE user-undoable operation (programmatic
   * recalculations the user should be able to Ctrl+Z). No-ops in readonly
   * mode.
   */
  applyTransaction(edits: readonly TmCellEdit[], opts?: { label?: string }): void {
    this.core.engine.applyTransaction(edits, opts);
  }

  /**
   * Drops the undo/redo history — the consumer calls it at save/cancel
   * moments and on wholesale reloads.
   */
  clearHistory(): void {
    this.core.clearHistory();
  }

  /** Focuses the grid: the active cell when one exists, else the container. */
  focus(): void {
    this.core.focus();
  }
}

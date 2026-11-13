// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { Directive, inject, TemplateRef } from '@angular/core';

import type { TmRowId } from '@tellma/core-ui/contracts';

/** Template context of a custom cell display (`*tmGridDisplay`). */
export interface TmGridDisplayContext<T = unknown, V = unknown> {
  /** The cell's value. */
  readonly $implicit: V;
  /** The row object. */
  readonly row: T;
  /** The row's identity. */
  readonly rowId: TmRowId;
  /** Whether the cell is currently in error state. */
  readonly invalid: boolean;
  /** Whether the cell rejects writes. */
  readonly readonly: boolean;
}

/**
 * Custom static display DOM for a column's cells. The template renders per
 * visible cell — the costlier path (an embedded view per cell instead of
 * plain text); keep it static and cheap. The column's text representation
 * (what copy exports and find searches) still comes from `format`.
 */
@Directive({ selector: '[tmGridDisplay]' })
export class TmGridDisplayDef<T = unknown, V = unknown> {
  /** The template to render in the cell. */
  readonly template = inject(TemplateRef<TmGridDisplayContext<T, V>>);

  /** Narrows the template context for type-checked bindings. */
  static ngTemplateContextGuard<T, V>(
    _dir: TmGridDisplayDef<T, V>,
    // eslint-disable-next-line @typescript-eslint/no-unused-vars -- type-position-only parameter
    _ctx: unknown,
  ): _ctx is TmGridDisplayContext<T, V> {
    return true;
  }
}

/** Template context of a custom cell editor (`*tmGridEditor`). */
export interface TmGridEditorContext<T = unknown, V = unknown> {
  /** The cell's value when the editor opened. */
  readonly $implicit: V;
  /**
   * The row object, or `undefined` on the new-row placeholder — the row does
   * not materialize until the edit commits, so consumer templates must handle
   * its absence.
   */
  readonly row: T | undefined;
}

/**
 * Custom editor template for a column. The hosted control registers itself
 * through the grid-provided cell-editor host token; the grid then owns its
 * value channel and drives commit/cancel/focus.
 */
@Directive({ selector: '[tmGridEditor]' })
export class TmGridEditorDef<T = unknown, V = unknown> {
  /** The template the grid instantiates when the cell enters editing. */
  readonly template = inject(TemplateRef<TmGridEditorContext<T, V>>);

  /** Narrows the template context for type-checked bindings. */
  static ngTemplateContextGuard<T, V>(
    _dir: TmGridEditorDef<T, V>,
    // eslint-disable-next-line @typescript-eslint/no-unused-vars -- type-position-only parameter
    _ctx: unknown,
  ): _ctx is TmGridEditorContext<T, V> {
    return true;
  }
}

/** Template context of a custom column header (`*tmGridHeader`). */
export interface TmGridHeaderContext {
  /** The column's header text. */
  readonly $implicit: string;
}

/**
 * Rich/interactive header content for a column. Interactive children do
 * not trigger column selection — only presses on the header background or
 * label select the column.
 */
@Directive({ selector: '[tmGridHeader]' })
export class TmGridHeaderDef {
  /** The template to render in the column header. */
  readonly template = inject(TemplateRef<TmGridHeaderContext>);

  /** Narrows the template context for type-checked bindings. */
  // eslint-disable-next-line @typescript-eslint/no-unused-vars -- type-position-only parameter
  static ngTemplateContextGuard(_dir: TmGridHeaderDef, _ctx: unknown): _ctx is TmGridHeaderContext {
    return true;
  }
}

/** Template context of a column header's end content (`*tmGridHeaderEnd`). */
export interface TmGridHeaderEndContext {
  /** The column's header text. */
  readonly $implicit: string;
  /** The column's model key, or `undefined` for an accessor column. */
  readonly key: string | undefined;
  /** The column's current sort direction, or `null` when it is not the sorted column. */
  readonly sort: 'asc' | 'desc' | null;
}

/**
 * Content at a column header's end, after the label and the sort slot:
 * typically one `button[tmGridHeaderAction]` that opens a filter popover.
 * Every column that declares it reserves the slot, so the header's layout
 * never moves when the action's state changes. Declaring it on any column
 * turns on keyboard navigation of the header row, where Enter, Space or
 * Alt+ArrowDown click the action.
 *
 * @example
 * ```html
 * <tm-grid-column key="name" header="Name" sortable>
 *   <ng-template tmGridHeaderEnd>
 *     <button tmGridHeaderAction [active]="filtered()" [tmPopoverTriggerFor]="nameFilter"
 *             aria-label="Filter by name"><tm-icon [svg]="funnel" /></button>
 *   </ng-template>
 * </tm-grid-column>
 * ```
 */
@Directive({ selector: 'ng-template[tmGridHeaderEnd]' })
export class TmGridHeaderEndDef {
  /** The template to render at the header's end. */
  readonly template = inject(TemplateRef<TmGridHeaderEndContext>);

  /** Narrows the template context for type-checked bindings. */
  static ngTemplateContextGuard(
    _dir: TmGridHeaderEndDef,
    // eslint-disable-next-line @typescript-eslint/no-unused-vars -- type-position-only parameter
    _ctx: unknown,
  ): _ctx is TmGridHeaderEndContext {
    return true;
  }
}

/**
 * The header of the grid's row-end column: one `button[tmGridHeaderAction]`
 * (a column editor, say), with no label, sort or resize handle. Declaring it
 * — or {@link TmGridRowEndDef} — adds a narrow column after the last data
 * column that stays at the inline end while the grid scrolls sideways. The
 * header row, this cell included, stays while the grid shows its loading or
 * empty state, so the action is always reachable.
 *
 * @example
 * ```html
 * <tm-grid [data]="rows()">
 *   <ng-template tmGridRowEndHeader>
 *     <button tmGridHeaderAction [tmPopoverTriggerFor]="columns" aria-label="Choose columns">
 *       <tm-icon [svg]="columnsGlyph" />
 *     </button>
 *   </ng-template>
 *   …
 * </tm-grid>
 * ```
 */
@Directive({ selector: 'ng-template[tmGridRowEndHeader]' })
export class TmGridRowEndHeaderDef {
  /** The template to render in the row-end column's header cell. */
  readonly template = inject(TemplateRef<void>);
}

/** Template context of a row-end cell (`*tmGridRowEnd`). */
export interface TmGridRowEndContext<T = unknown> {
  /** The row object. */
  readonly $implicit: T;
  /** The row's identity. */
  readonly rowId: TmRowId;
}

/**
 * Per-row content of the grid's row-end column, such as a row menu button.
 * The column is not a data column: range selection, copy, find, column
 * selection, resizing, sorting and the persisted column state never include
 * it. With this template its cells are grid cells that the inline-end arrow
 * and End reach after the last data column; without it they are empty, and a
 * click on one activates the row as a click on a data cell does.
 */
@Directive({ selector: 'ng-template[tmGridRowEnd]' })
export class TmGridRowEndDef<T = unknown> {
  /** The template to render in each row's row-end cell. */
  readonly template = inject(TemplateRef<TmGridRowEndContext<T>>);

  /** Narrows the template context for type-checked bindings. */
  static ngTemplateContextGuard<T>(
    _dir: TmGridRowEndDef<T>,
    // eslint-disable-next-line @typescript-eslint/no-unused-vars -- type-position-only parameter
    _ctx: unknown,
  ): _ctx is TmGridRowEndContext<T> {
    return true;
  }
}

/** Replaces the grid's built-in empty-state message (`*tmGridEmpty`). */
@Directive({ selector: '[tmGridEmpty]' })
export class TmGridEmptyDef {
  /** The template to render while the grid is bound, loaded, and empty. */
  readonly template = inject(TemplateRef<void>);
}

/** Replaces the grid's built-in loading overlay content (`*tmGridLoading`). */
@Directive({ selector: '[tmGridLoading]' })
export class TmGridLoadingDef {
  /** The template to render while the grid is loading. */
  readonly template = inject(TemplateRef<void>);
}

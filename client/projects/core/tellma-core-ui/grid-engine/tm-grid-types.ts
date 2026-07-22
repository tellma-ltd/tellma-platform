// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import type { SignalLike, TmParseContext, TmParseError, TmRowId } from '@tellma/core-ui/contracts';

/**
 * A cell coordinate in view space: visible-row index × data-column index.
 * Row indices run over the currently visible row sequence (for a tree, the
 * flattened expansion); column indices run over the data columns only —
 * chrome columns (row header, selection checkboxes) are outside this space.
 */
export interface TmRowCol {
  /** Visible-row index (0-based over the visible sequence). */
  readonly row: number;
  /** Data-column index (0-based, display order). */
  readonly col: number;
}

/** What a selection range spans: a cell rectangle, full rows, full columns, or everything. */
export type TmGridRangeKind = 'cells' | 'rows' | 'cols' | 'all';

/**
 * One selection range: a rectangle described by its anchor (where selection
 * started) and focus (the moving corner), both in view space.
 */
export interface TmGridRange {
  /** The fixed corner — where the selection started. */
  readonly anchor: TmRowCol;
  /** The moving corner — where the selection currently extends to. */
  readonly focus: TmRowCol;
  /** What the user selected; `rows` ranges always span every column. */
  readonly kind: TmGridRangeKind;
}

/** A normalized view-space rectangle (all bounds inclusive). */
export interface TmGridRect {
  /** First row (inclusive). */
  readonly top: number;
  /** First column (inclusive). */
  readonly left: number;
  /** Last row (inclusive). */
  readonly bottom: number;
  /** Last column (inclusive). */
  readonly right: number;
}

/** The built-in column types — each a bundle of format/parse/editor defaults. */
export type TmGridColumnType = 'text' | 'number' | 'boolean' | 'date' | 'enum' | 'entity' | 'custom';

/**
 * The engine's per-column oracle. The component layer builds one per column
 * from the column definition, its type defaults, and the bound field's
 * state, then hands the engine the resulting closures — the engine never
 * sees Angular, field trees, or templates.
 */
export interface TmGridEngineColumn<T = unknown> {
  /**
   * The model property (and child-field key) this column writes, or `null`
   * for accessor columns, which are always readonly.
   */
  readonly key: string | null;
  /**
   * Stable identity for maps and snapshots: the key when present, else a
   * generated id stable for the lifetime of the column definition.
   */
  readonly id: string;
  /** The column's built-in type (defaults bundle). */
  readonly type: TmGridColumnType;
  /** The display header label — header-row detection and copy-with-headers read it. */
  readonly headerLabel: SignalLike<string>;
  /** Reads the cell's model value. */
  getValue(row: T): unknown;
  /**
   * The cell's text representation — what copy exports, what find searches,
   * what announcements speak. Formatting (including locale) is baked into
   * the closure by the component layer.
   */
  getText(row: T): string;
  /**
   * Whether this column can ever be written: it has a `key` and an editing
   * path (a parser, an editor, or the boolean toggle). Per-cell readonly
   * state is `isCellReadonly`.
   */
  readonly editable: boolean;
  /**
   * Whether this specific cell rejects writes — folds the column-level
   * readonly setting with the bound field's per-cell disabled/readonly
   * state (the field wins when bound).
   */
  isCellReadonly(row: T): boolean;
  /**
   * Text→value conversion for typed paste and text-editor commits; absent
   * when the column has no synchronous parse path.
   */
  parse?(text: string, ctx: TmParseContext): unknown | TmParseError;
  /**
   * Whether the column has an async label resolver — unresolvable pasted
   * text is collected for one batched resolution call instead of failing.
   */
  readonly hasResolver: boolean;
  /** The column's cleared value — what Delete and error-clearing write. */
  readonly clearedValue: unknown;
}

/** One row of the visible sequence, with its tree placement. */
export interface TmGridRowView<T = unknown> {
  /** The consumer's row object. */
  readonly row: T;
  /** The row's stable identity. */
  readonly id: TmRowId;
  /** The row's index in the consumer's flat model array. */
  readonly modelIndex: number;
  /** Tree depth (0 for roots and every flat-grid row). */
  readonly level: number;
  /** The parent row's id, or `null` for roots. */
  readonly parentId: TmRowId | null;
  /** Whether the row can expand (has, or may lazily load, children). */
  readonly expandable: boolean;
  /** Whether the row is currently expanded. */
  readonly expanded: boolean;
}

/** Tree configuration: how the engine derives hierarchy from the flat row array. */
export interface TmGridTreeOptions<T = unknown> {
  /** Reads a row's parent id; `null`/`undefined` marks a root. */
  parentId(row: T): TmRowId | null | undefined;
  /**
   * The model property that stores the parent id — required for editable
   * trees, where row moves re-parent rows by writing it through the field.
   */
  readonly parentIdKey?: string;
  /**
   * Marks rows whose children may not be loaded yet, so they render an
   * expander before any child exists. Loading itself is the component
   * layer's concern.
   */
  hasChildren?(row: T): boolean;
  /**
   * How deep the tree starts expanded when content first binds: 0 = all
   * collapsed, 1 = roots expanded, … Defaults to fully expanded.
   */
  readonly defaultExpandedDepth?: number;
}

/**
 * A navigation motion. `left`/`right` are physical (arrow keys) and are
 * mapped through the direction signal; `inlineStart`/`inlineEnd` are
 * logical; the rest are axis- or extent-based.
 */
export type TmGridMotion =
  | 'up'
  | 'down'
  | 'left'
  | 'right'
  | 'inlineStart'
  | 'inlineEnd'
  | 'pageUp'
  | 'pageDown'
  | 'rowStart'
  | 'rowEnd'
  | 'gridStart'
  | 'gridEnd';

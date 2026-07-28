// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

/**
 * Grid-facing contracts: the cell editor/display seams every grid-embeddable
 * control implements, the paste-resolution types, and the grid state-memory
 * data types. Pure types plus one runtime sentinel — no Angular, no other
 * `@tellma` packages (enforced by lint).
 */

import type { SignalLike, WritableSignalLike } from './tm-contracts';

/**
 * Implemented by any control mountable as a grid cell editor. The grid
 * drives the editor uniformly through this seam: it owns the value channel,
 * opens/commits/cancels the editing session, and reads the editor's textual
 * content when it needs a string representation of what the user entered.
 *
 * Keyboard input reaches the editor through normal DOM focus and bubbles to
 * the grid, which acts only on keys the editor did not `preventDefault` —
 * an editor that consumes a key (an open dropdown consuming Esc or the
 * vertical arrows) thereby keeps the grid out.
 */
export interface TmCellEditor<T> {
  /** The grid owns the value channel; commit/cancel write/restore through it. */
  readonly value: WritableSignalLike<T>;
  /**
   * The editor's committed-text view of its current content, or `null` when
   * the content is not representable as `T` (the grid records such content
   * as an invalid input instead of writing a value).
   */
  readonly text: SignalLike<string | null>;
  /** Flushes pending text into the value channel and accepts the edit. */
  commit(): void;
  /** Restores the value that was present when the editor opened. */
  cancel(): void;
  /** Focuses the editor's focusable element; text editors place the caret at the end. */
  focus(): void;
  /** Seed for type-to-edit: replaces the content with `text`, caret at the end. */
  seed?(text: string): void;
}

/**
 * Pure display path — no component instance; paints thousands of readonly
 * cells. A contract IMPLEMENTED BY `tm-*` controls (checkbox, select, …) so
 * a grid's built-in column types render control-faithful static cells. It is
 * distinct from a per-column custom display template, which is the
 * consumer-facing DOM override.
 */
export interface TmCellDisplay<T> {
  /** The cell's text representation — what copy exports and find searches. */
  formatValue(value: T, locale: string): string;
  /** Optional token-driven glyph class (a checkbox box, …) for non-text cells. */
  displayClass?(value: T): string;
}

/**
 * Sentinel a column `parse` returns for unparseable text — distinct from a
 * legitimate `null` value, which is a successful parse of an empty cell.
 */
export const TM_PARSE_ERROR: unique symbol = Symbol('TM_PARSE_ERROR');

/** The type of {@link TM_PARSE_ERROR} — the failure branch of a column `parse`. */
export type TmParseError = typeof TM_PARSE_ERROR;

/** Context handed to a column `parse` function. */
export interface TmParseContext {
  /** The grid's active locale. */
  readonly locale: string;
  /** During paste: the copying grid's locale, from clipboard metadata. */
  readonly sourceLocale?: string;
}

/** Context handed to a column's batched label resolver during paste. */
export interface TmPasteContext extends TmParseContext {
  /**
   * The copying grid's tenant id, from clipboard metadata — lets the resolver
   * refuse raw ids that crossed a tenant boundary and re-resolve by label.
   * Tenant ids are unique only within one distribution; compare
   * {@link sourceDistributionKey} too before trusting a match.
   */
  readonly sourceTenantId?: string;
  /** The copying grid's distribution key, from clipboard metadata. */
  readonly sourceDistributionKey?: string;
  /**
   * Aborts when every cell awaiting this resolution has been invalidated
   * (edited over, re-pasted, undone). Honoring it saves a server round
   * trip; late results are discarded regardless.
   */
  readonly signal: AbortSignal;
}

/**
 * One label's outcome from a batched label resolution: the resolved value,
 * or a definitive failure — `notFound` (no match) or `ambiguous` (several
 * matches; the resolver must not guess).
 */
export type TmLabelResolution<V> = { value: V } | { error: 'notFound' | 'ambiguous' };

/**
 * Registration sink a grid provides to the editor views it creates. A token
 * *provided by* a control inside a dynamically created embedded view is not
 * reachable through public query APIs, so discovery is inverted: the grid
 * passes an injector carrying this host to the view, and the editor control
 * registers itself on construction. New controls slot in without grid
 * changes.
 */
export interface TmCellEditorHost {
  /** Called by the mounted control to hand the grid its editor seam. */
  register(editor: TmCellEditor<unknown>): void;
}

/** A grid row's stable identity, produced by the consumer's `rowId` accessor. */
export type TmRowId = string | number;

/**
 * One programmatic cell write, addressed by row identity and column key —
 * the unit of a grid's `applyTransaction`, which registers consumer edits
 * in the user-visible undo history.
 */
export interface TmCellEdit {
  /** Identity of the row to write, per the grid's `rowId` accessor. */
  readonly rowId: TmRowId;
  /** The model property (column key) to write. */
  readonly key: string;
  /** The value to write. */
  readonly value: unknown;
}

// ---------------------------------------------------------------------------
// Grid state-memory data types. The store itself is an injectable service in
// the grid package; these are the shapes it persists per grid definition
// (`gridId`) and per content (`gridId` + `contentKey`).
// ---------------------------------------------------------------------------

/** Persisted column widths in px, keyed by column key. */
export type TmGridColumnWidths = Readonly<Record<string, number>>;

/** A grid's persisted scroll offsets in px. */
export interface TmGridScrollPosition {
  /** Horizontal scroll offset. */
  readonly x: number;
  /** Vertical scroll offset. */
  readonly y: number;
}

/**
 * One selection range persisted by identity so it can be restored after the
 * grid remounts: row endpoints by row id (`null` for column/whole-grid
 * ranges), column endpoints by column key (`null` for row ranges).
 */
export interface TmGridRangeSnapshot {
  /** Row id of the range's anchor, or `null` when the range spans all rows. */
  readonly anchorRowId: TmRowId | null;
  /** Column key of the range's anchor, or `null` when the range spans all columns. */
  readonly anchorColumnKey: string | null;
  /** Row id of the range's focus, or `null` when the range spans all rows. */
  readonly focusRowId: TmRowId | null;
  /** Column key of the range's focus, or `null` when the range spans all columns. */
  readonly focusColumnKey: string | null;
  /** What the user selected: cell rectangle, full rows, full columns, or everything. */
  readonly kind: 'cells' | 'rows' | 'cols' | 'all';
}

/** A grid's persisted selection: all ranges plus the active cell, by identity. */
export interface TmGridSelectionSnapshot {
  /** The selection's ranges; the last one is the active range. */
  readonly ranges: readonly TmGridRangeSnapshot[];
  /** Row id of the active cell, or `null` when no cell was active. */
  readonly activeRowId: TmRowId | null;
  /** Column key of the active cell, or `null` when no cell was active. */
  readonly activeColumnKey: string | null;
  /**
   * The active cell's view-row index at snapshot time — the restore
   * fallback when its row id no longer resolves (clamped to the content
   * present at restore time).
   */
  readonly activeViewRow?: number;
}

/**
 * The per-content state slice a grid snapshots on destroy and restores on
 * remount for the same `gridId` + `contentKey` pair. Restores clamp to the
 * content actually present at restore time; a slice for content that shrank
 * or lost rows restores partially or not at all.
 */
export interface TmGridContentState {
  /** Scroll offsets, clamped to the content extent on restore. */
  readonly scroll?: TmGridScrollPosition;
  /** Selection + active cell by identity; dropped unless every endpoint resolves. */
  readonly selection?: TmGridSelectionSnapshot;
  /**
   * The undo/redo stack snapshot. Opaque: its runtime shape is owned by the
   * grid engine and is not part of this contract; it is held in memory only
   * and never serialized.
   */
  readonly history?: unknown;
  /** Expanded row ids of a tree grid; unknown ids are pruned on restore. */
  readonly expandedRowIds?: ReadonlySet<TmRowId>;
}

// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

/**
 * What a pivot member is: an ordinary member, the phantom that carries a
 * parent's own values beside its children, the "(None)" member that
 * gathers the facts with no value, or the axis total.
 */
export type TmPivotMemberKind = 'member' | 'phantom' | 'none' | 'total';

/**
 * One member of a pivot axis: a row or a column header.
 *
 * Each axis is one tree, listed in pre-order: a member, then its children
 * in display order. A member with no parent, or whose parent is the
 * axis's `total` member, is a first-level member; the `total` member
 * itself always renders last and never collapses.
 */
export interface TmPivotMember {
  /** Unique on its axis; the cell callbacks receive it. */
  readonly id: number;
  /** The member one level up; `null` for the total or a first-level member. */
  readonly parent: number | null;
  /** The member's kind. */
  readonly kind: TmPivotMemberKind;
  /**
   * Display-ready plain text, rendered in a `<bdi>`. A phantom shows it
   * with "(direct)" appended by the pivot.
   */
  readonly label: string;
  /** Rows only: the member's attribute values, parallel to `attributeLabels`. */
  readonly attributes?: readonly string[];
}

/**
 * The data a `tm-pivot` renders: two member trees, the measure names and a
 * cell-text callback the pivot calls only for the cells it renders.
 *
 * Give a new model object whenever the data or its formatting changes (a
 * new result, a language switch): the pivot keys its caches on the
 * model's identity.
 */
export interface TmPivotModel {
  /** The row members, in pre-order. */
  readonly rows: readonly TmPivotMember[];
  /** The column members, in pre-order. */
  readonly columns: readonly TmPivotMember[];
  /** The measure names; at least one. Every column group holds one data column per measure. */
  readonly measures: readonly string[];
  /** The corner's text: the row dimension names. */
  readonly rowLabel: string;
  /** One label per column level, top to bottom; an empty string for a level without one. */
  readonly columnLevelLabels: readonly string[];
  /** The attribute columns' headers. */
  readonly attributeLabels: readonly string[];
  /**
   * A cell's formatted text, by row member id, column member id and
   * measure index; `null` when the cell has no facts, which renders empty.
   */
  text(row: number, column: number, measure: number): string | null;
  /**
   * The measure's widest formatted value in the data, which sizes its
   * columns before they first paint; `null` when it has none.
   */
  widest(measure: number): string | null;
  /**
   * Whether a value cell is a drill target. Absent, every value cell is.
   * A refused cell has no hover look and explains itself on focus and on
   * Enter.
   */
  activatable?(row: number, column: number): boolean;
}

/**
 * The column widths a user has set by resizing, in px. An absent entry
 * keeps the pivot's default width.
 */
export interface TmPivotWidths {
  /** The row-header column. */
  readonly rowHeader?: number;
  /** The attribute columns, by attribute index. */
  readonly attributes?: readonly (number | undefined)[];
  /** The data columns, by measure index: one width serves every column of the measure. */
  readonly measures?: readonly (number | undefined)[];
}

/** An activated value cell: its members, its measure, and whether to open a new tab. */
export interface TmPivotCell {
  /** The row member's id. */
  readonly row: number;
  /** The column member's id. */
  readonly column: number;
  /** The measure's index. */
  readonly measure: number;
  /** Whether the user asked for a new tab (Mod with Enter or the click). */
  readonly newTab: boolean;
}

/** The axis an expand-all or collapse-all applies to. */
export type TmPivotAxis = 'rows' | 'columns';

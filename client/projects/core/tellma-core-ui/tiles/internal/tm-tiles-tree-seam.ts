// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

// The seam tree mode plugs into: `tm-tiles` renders, windows and navigates
// whatever rows are on show, and asks the tree for the rest — which rows
// are on show, each one's place, the arrows' tree moves, the ancestors a
// reveal expands and the chrome a tree tile draws. `TmTilesTree` (in the
// tiles-tree entry point) implements it, so a flat list carries none of
// the tree's code, styles or glyphs.

import type { Signal, Type } from '@angular/core';

import type { TmRowId } from '@tellma/core-ui/contracts';

/** A row on show: the row and its id. */
export interface ɵTmTilesRow<T> {
  /** The row. */
  readonly row: T;
  /** The row's id. */
  readonly id: TmRowId;
}

/** A row's place in a tree of tiles: its line's ARIA and its tile's chrome. */
export interface ɵTmTilesTreePlace {
  /** The 1-based depth. */
  readonly level: number;
  /** Whether the row is expanded; `null` for a row that cannot expand. */
  readonly expanded: boolean | null;
  /**
   * Whether the row's children are loading; `null` when the tree loads no
   * children lazily, so its tiles reserve no loading slot.
   */
  readonly loading: boolean | null;
  /** The 1-based position among its siblings. */
  readonly posInSet: number;
  /** How many siblings the row has, itself included. */
  readonly setSize: number;
}

/**
 * Tree mode as `tm-tiles` sees it: what a tree applied to the tiles answers
 * for them. Every member reads the tree's own signals, so a computed that
 * calls one follows the tree's changes.
 */
export interface ɵTmTilesTree<T> {
  /**
   * The rows on show in display order: the roots and the expanded rows'
   * children. `null` while no parent reader is bound: the tiles are flat.
   */
  readonly rows: Signal<readonly ɵTmTilesRow<T>[] | null>;
  /**
   * The component each tree tile renders at inline-start, before the
   * checkbox: the indent's twisty and the reserved loading slot. Its one
   * input, `place`, takes the row's {@link ɵTmTilesTreePlace}.
   */
  readonly affordance: Type<unknown>;
  /** The place of a row on show. */
  place(id: TmRowId): ɵTmTilesTreePlace;
  /**
   * Right (`forward`, mapped to the direction) or Left on the active row:
   * expands or collapses it in place and answers `null`, or answers the row
   * focus moves to (its first child or its parent), or `'first'` when no
   * row is active.
   */
  arrow(id: TmRowId | null, forward: boolean): TmRowId | 'first' | null;
  /** Expands a row's collapsed ancestors so it shows; `false` when no row has the id. */
  reveal(id: TmRowId): boolean;
  /** Expands or collapses a row that can expand (its twisty was clicked). */
  toggle(id: TmRowId): void;
  /**
   * The nearest ancestor of a row that is on show (`shown` maps the rows on
   * show to their indexes), or `null`: where the active tile goes when its
   * row is collapsed away.
   */
  shownAncestor(id: TmRowId, shown: ReadonlyMap<TmRowId, number>): TmRowId | null;
}

// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { ɵtmPivotHeaderAt, type ɵTmPivotColumns } from './pivot-layout';

/**
 * The pivot's logical grid. Rows: the header rows, then the visible body
 * rows. Columns: the row header, the attribute columns, then the data
 * columns. A header cell may span several of each.
 */
export interface ɵTmPivotGrid {
  /** The column layout (header cells and data columns). */
  readonly columns: ɵTmPivotColumns;
  /** The attribute column count. */
  readonly attributeCount: number;
  /** The visible body row count. */
  readonly bodyRows: number;
}

/** A position in the logical grid. */
export interface ɵTmPivotPosition {
  /** The row: header rows first. */
  readonly row: number;
  /** The column: the row header is 0. */
  readonly column: number;
}

/** The cell covering a position: its first row and column and its spans. */
export interface ɵTmPivotCover {
  /** The cell's first row. */
  readonly row: number;
  /** How many rows it covers. */
  readonly rowSpan: number;
  /** The cell's first column. */
  readonly column: number;
  /** How many columns it covers. */
  readonly columnSpan: number;
}

/** The logical grid's size. */
export function ɵtmPivotGridSize(grid: ɵTmPivotGrid): { readonly rows: number; readonly columns: number } {
  return {
    rows: grid.columns.headerRows + grid.bodyRows,
    columns: 1 + grid.attributeCount + grid.columns.dataCount,
  };
}

/**
 * The cell covering a position. Body cells and the row-header corner of a
 * header row cover one position; an attribute's corner spans every header
 * row; a column header covers its rows and data columns.
 */
export function ɵtmPivotCover(grid: ɵTmPivotGrid, position: ɵTmPivotPosition): ɵTmPivotCover {
  const headerRows = grid.columns.headerRows;
  const single = { row: position.row, rowSpan: 1, column: position.column, columnSpan: 1 };
  if (position.row >= headerRows || position.column === 0) {
    return single;
  }
  if (position.column <= grid.attributeCount) {
    return { row: 0, rowSpan: headerRows, column: position.column, columnSpan: 1 };
  }
  const lead = 1 + grid.attributeCount;
  const cell = ɵtmPivotHeaderAt(grid.columns, position.row, position.column - lead);
  return cell === null
    ? single
    : { row: cell.row, rowSpan: cell.rowSpan, column: lead + cell.start, columnSpan: cell.end - cell.start };
}

/** A keyboard move in the logical grid; left and right are already mapped through the direction. */
export type ɵTmPivotMove =
  | 'left'
  | 'right'
  | 'up'
  | 'down'
  | 'rowStart'
  | 'rowEnd'
  | 'first'
  | 'last'
  | 'pageUp'
  | 'pageDown';

/** Clamps a position into the grid. */
export function ɵtmPivotClamp(grid: ɵTmPivotGrid, position: ɵTmPivotPosition): ɵTmPivotPosition {
  const size = ɵtmPivotGridSize(grid);
  return {
    row: Math.max(0, Math.min(size.rows - 1, position.row)),
    column: Math.max(0, Math.min(size.columns - 1, position.column)),
  };
}

/**
 * Where a move lands from a position, headers included. A horizontal move
 * steps past the covering cell's columns, a vertical one past its rows,
 * keeping the column the user came along, so leaving a wide header goes
 * back to the column the user entered it from. A page moves by
 * `pageRows`; the first and last positions are the grid's corners.
 */
export function ɵtmPivotMoveFrom(
  grid: ɵTmPivotGrid,
  from: ɵTmPivotPosition,
  move: ɵTmPivotMove,
  pageRows: number,
): ɵTmPivotPosition {
  const size = ɵtmPivotGridSize(grid);
  const cover = ɵtmPivotCover(grid, from);
  const last = { row: size.rows - 1, column: size.columns - 1 };
  let target: ɵTmPivotPosition;
  switch (move) {
    case 'left':
      target = { row: from.row, column: cover.column - 1 };
      break;
    case 'right':
      target = { row: from.row, column: cover.column + cover.columnSpan };
      break;
    case 'up':
      target = { row: cover.row - 1, column: from.column };
      break;
    case 'down':
      target = { row: cover.row + cover.rowSpan, column: from.column };
      break;
    case 'rowStart':
      target = { row: from.row, column: 0 };
      break;
    case 'rowEnd':
      target = { row: from.row, column: last.column };
      break;
    case 'first':
      target = { row: 0, column: 0 };
      break;
    case 'last':
      target = last;
      break;
    case 'pageUp':
      target = { row: from.row - Math.max(1, pageRows), column: from.column };
      break;
    case 'pageDown':
      target = { row: from.row + Math.max(1, pageRows), column: from.column };
      break;
  }
  // Off the grid's edge the position stays where it was.
  if (
    (move === 'left' || move === 'right' || move === 'up' || move === 'down') &&
    (target.row < 0 || target.row > last.row || target.column < 0 || target.column > last.column)
  ) {
    return from;
  }
  return ɵtmPivotClamp(grid, target);
}

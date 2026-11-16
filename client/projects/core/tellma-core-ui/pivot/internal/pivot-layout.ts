// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import type { TmPivotMember } from '../tm-pivot-types';

/**
 * The five looks of pivot rows, columns and cells. A data cell takes the
 * stronger of its row's and its column's look.
 */
export type ɵTmPivotStyle = 'total' | 'subtotal-1' | 'subtotal-2' | 'phantom' | 'leaf';

/**
 * How strongly each look wins an intersection: the grand total first, then
 * the two subtotal looks; a phantom is a muted leaf, so it wins only over
 * a plain leaf.
 */
const STRENGTH: Readonly<Record<ɵTmPivotStyle, number>> = {
  total: 4,
  'subtotal-1': 3,
  'subtotal-2': 2,
  phantom: 1,
  leaf: 0,
};

/** The stronger of two looks: the look a data cell takes from its row and its column. */
export function ɵtmPivotStronger(row: ɵTmPivotStyle, column: ɵTmPivotStyle): ɵTmPivotStyle {
  return STRENGTH[row] >= STRENGTH[column] ? row : column;
}

/** A member's look from its kind, its depth and whether it has children. */
function styleOf(member: TmPivotMember, depth: number, hasChildren: boolean): ɵTmPivotStyle {
  if (member.kind === 'total') {
    return 'total';
  }
  if (member.kind === 'phantom') {
    return 'phantom';
  }
  if (hasChildren && depth === 1) {
    return 'subtotal-1';
  }
  if (hasChildren && depth === 2) {
    return 'subtotal-2';
  }
  return 'leaf';
}

/**
 * One axis's member tree, indexed once per model: positions by id, the
 * children of each member in display order, depths, and the total.
 * Members are addressed by their index in the axis's array.
 */
export interface ɵTmPivotAxis {
  /** The members, as given. */
  readonly members: readonly TmPivotMember[];
  /** Each member's index, by id. */
  readonly indexOf: ReadonlyMap<number, number>;
  /** Each member's children's indices, in display order. */
  readonly children: readonly (readonly number[])[];
  /** Each member's parent's index; -1 for a first-level member or the total. */
  readonly parent: Int32Array;
  /** Each member's depth: 1 for a first-level member, 0 for the total. */
  readonly depth: Int32Array;
  /** The first-level members' indices, in display order (the total excluded). */
  readonly firstLevel: readonly number[];
  /** The total member's index, or -1 when the axis has none. */
  readonly total: number;
  /** The deepest member's depth (0 for an axis holding only its total). */
  readonly maxDepth: number;
  /** Shapes the axis could not honour, for a development-mode report. */
  readonly problems: readonly string[];
}

/**
 * Indexes one axis. Ids must be unique; a member whose parent is unknown
 * or itself is treated as a first-level member, a parent cycle is cut at
 * the link that closes it, and a second total is an ordinary first-level
 * member; each such case is listed in `problems`. Children keep the
 * members' array order.
 */
export function ɵtmPivotAxis(members: readonly TmPivotMember[]): ɵTmPivotAxis {
  const count = members.length;
  const problems: string[] = [];
  const indexOf = new Map<number, number>();
  // Section: ids and the total.
  let total = -1;
  for (let index = 0; index < count; index++) {
    const member = members[index];
    if (indexOf.has(member.id)) {
      problems.push(`duplicate member id ${member.id}`);
      continue;
    }
    indexOf.set(member.id, index);
    if (member.kind === 'total') {
      if (total === -1) {
        total = index;
      } else {
        problems.push(`a second total member (id ${member.id})`);
      }
    }
  }
  // Section: parents. A first-level member hangs under nothing or under
  // the total, which renders apart from the tree.
  const parent = new Int32Array(count).fill(-1);
  for (let index = 0; index < count; index++) {
    const member = members[index];
    if (index === total || member.parent === null || indexOf.get(member.id) !== index) {
      continue;
    }
    const parentIndex = indexOf.get(member.parent);
    if (parentIndex === undefined) {
      problems.push(`member ${member.id} names an unknown parent ${member.parent}`);
    } else if (parentIndex === index) {
      problems.push(`member ${member.id} is its own parent`);
    } else if (parentIndex !== total) {
      parent[index] = parentIndex;
    }
  }
  // Section: depths, walking each chain up to a known depth. A walk that
  // meets itself is a cycle: the link that closes it is cut, so the
  // member that closed it moves to the first level.
  const depth = new Int32Array(count).fill(-1);
  if (total !== -1) {
    depth[total] = 0;
  }
  let maxDepth = 0;
  const chain: number[] = [];
  const onChain = new Uint8Array(count);
  for (let index = 0; index < count; index++) {
    if (depth[index] !== -1) {
      continue;
    }
    chain.length = 0;
    let cursor = index;
    while (cursor !== -1 && depth[cursor] === -1 && onChain[cursor] === 0) {
      chain.push(cursor);
      onChain[cursor] = 1;
      cursor = parent[cursor];
    }
    let base: number;
    if (cursor === -1) {
      base = 0;
    } else if (depth[cursor] !== -1) {
      base = depth[cursor];
    } else {
      problems.push(`member ${members[index].id} is inside a parent cycle`);
      parent[chain[chain.length - 1]] = -1;
      base = 0;
    }
    for (let step = chain.length - 1; step >= 0; step--) {
      base += 1;
      depth[chain[step]] = base;
      onChain[chain[step]] = 0;
    }
    maxDepth = Math.max(maxDepth, base);
  }
  // Section: children lists and the first level, in array order.
  const children: number[][] = Array.from({ length: count }, () => []);
  const firstLevel: number[] = [];
  for (let index = 0; index < count; index++) {
    if (index === total || indexOf.get(members[index].id) !== index) {
      continue;
    }
    const parentIndex = parent[index];
    if (parentIndex === -1) {
      firstLevel.push(index);
    } else {
      children[parentIndex].push(index);
    }
  }
  return { members, indexOf, children, parent, depth, firstLevel, total, maxDepth, problems };
}

/** Whether a member has children it can show. */
function expandableAt(axis: ɵTmPivotAxis, index: number): boolean {
  return index !== axis.total && axis.children[index].length > 0;
}

/** The ids of every member that has children: the expansion set of "expand all". */
export function ɵtmPivotExpandableIds(axis: ɵTmPivotAxis): ReadonlySet<number> {
  const ids = new Set<number>();
  for (let index = 0; index < axis.members.length; index++) {
    if (expandableAt(axis, index)) {
      ids.add(axis.members[index].id);
    }
  }
  return ids;
}

/**
 * How many rows, or column groups, a member's expansion reveals: its
 * children, and theirs while expanded. On the column axis an expanded
 * descendant shows as its subtotal column, so the count is the same.
 * Children are pushed one by one, so a member with any number of them
 * never overflows a call's argument limit.
 */
export function ɵtmPivotVisibleUnder(axis: ɵTmPivotAxis, index: number, expanded: ReadonlySet<number>): number {
  let count = 0;
  const stack = [index];
  while (stack.length > 0) {
    const children = axis.children[stack.pop() as number];
    count += children.length;
    for (const child of children) {
      if (axis.children[child].length > 0 && expanded.has(axis.members[child].id)) {
        stack.push(child);
      }
    }
  }
  return count;
}

/** One visible body row, in display order. */
export interface ɵTmPivotRow {
  /** The member's index on the row axis. */
  readonly index: number;
  /** The tree level announced as `aria-level`: 1 for a first-level member and the total. */
  readonly level: number;
  /** The 1-based position among its siblings (the total is the last first-level sibling). */
  readonly posInSet: number;
  /** The sibling count. */
  readonly setSize: number;
  /** Whether the member has children. */
  readonly expandable: boolean;
  /** Whether its children show. */
  readonly expanded: boolean;
  /** The row's look. */
  readonly style: ɵTmPivotStyle;
}

/**
 * The visible rows in compact form: each member, then its children while
 * it is expanded, depth first in display order, and the total last. A
 * parent's row carries its subtotal, so it shows whether or not it is
 * expanded.
 */
export function ɵtmPivotFlattenRows(
  axis: ɵTmPivotAxis,
  expanded: ReadonlySet<number>,
): readonly ɵTmPivotRow[] {
  const rows: ɵTmPivotRow[] = [];
  const hasTotal = axis.total !== -1;
  const topSize = axis.firstLevel.length + (hasTotal ? 1 : 0);
  // An explicit stack of sibling lists keeps a deep tree off the call stack.
  const stack: { readonly siblings: readonly number[]; next: number }[] = [
    { siblings: axis.firstLevel, next: 0 },
  ];
  while (stack.length > 0) {
    const frame = stack[stack.length - 1];
    if (frame.next >= frame.siblings.length) {
      stack.pop();
      continue;
    }
    const position = frame.next++;
    const index = frame.siblings[position];
    const member = axis.members[index];
    const canExpand = expandableAt(axis, index);
    const isExpanded = canExpand && expanded.has(member.id);
    const depth = axis.depth[index];
    rows.push({
      index,
      level: depth,
      posInSet: position + 1,
      setSize: stack.length === 1 ? topSize : frame.siblings.length,
      expandable: canExpand,
      expanded: isExpanded,
      style: styleOf(member, depth, canExpand),
    });
    if (isExpanded) {
      stack.push({ siblings: axis.children[index], next: 0 });
    }
  }
  if (hasTotal) {
    rows.push({
      index: axis.total,
      level: 1,
      posInSet: topSize,
      setSize: topSize,
      expandable: false,
      expanded: false,
      style: 'total',
    });
  }
  return rows;
}

/**
 * One column group: a member's own column (a leaf, or a parent shown
 * collapsed) or an expanded parent's subtotal column after its children.
 * Each group holds one data column per measure.
 */
export interface ɵTmPivotSlot {
  /** The member's index on the column axis. */
  readonly index: number;
  /** Whether this is the member's own column or its subtotal column. */
  readonly kind: 'self' | 'subtotal';
  /** The group's look. */
  readonly style: ɵTmPivotStyle;
}

/** What a header cell stands for. */
export type ɵTmPivotHeaderKind = 'group' | 'member' | 'subtotal' | 'total' | 'measure';

/** One column header cell over a range of data columns. */
export interface ɵTmPivotHeaderCell {
  /** A key that stays the same while the cell stands for the same thing. */
  readonly key: string;
  /** What the cell stands for. */
  readonly kind: ɵTmPivotHeaderKind;
  /** The first header row it occupies (0-based). */
  readonly row: number;
  /** How many header rows it spans. */
  readonly rowSpan: number;
  /** The first data column it covers. */
  readonly start: number;
  /** One past the last data column it covers. */
  readonly end: number;
  /** The member's index on the column axis; -1 for a measure cell. */
  readonly member: number;
  /** A measure cell's measure; -1 otherwise. */
  readonly measure: number;
  /** Whether the member has children. */
  readonly expandable: boolean;
  /** Whether its children show. */
  readonly expanded: boolean;
  /** The measure a handle on this cell resizes; -1 when it has none. */
  readonly resizes: number;
  /** The cell's look. */
  readonly style: ɵTmPivotStyle;
}

/** The column layout: groups, data columns and header cells. */
export interface ɵTmPivotColumns {
  /** The column groups, in display order. */
  readonly slots: readonly ɵTmPivotSlot[];
  /** The measure count: data columns per group. */
  readonly measureCount: number;
  /** The data column count: groups × measures. */
  readonly dataCount: number;
  /** The header rows given to member levels: one per column level, at least one. */
  readonly levelRows: number;
  /** Every header row: the level rows, plus a row of measure names for two or more measures. */
  readonly headerRows: number;
  /** Every header cell, by row, then by start. */
  readonly cells: readonly ɵTmPivotHeaderCell[];
  /** Per header row, every cell that occupies it (spans from above included), by start. */
  readonly rowCells: readonly (readonly ɵTmPivotHeaderCell[])[];
  /** Each column member's own group index, or its subtotal group's when expanded; -1 when hidden. */
  readonly slotOf: Int32Array;
}

/**
 * Lays the columns out in compact form: an expanded member's group holds
 * its children, then its subtotal column; the total comes last. Header
 * rows are one per column level (the deepest member's depth, or the level
 * labels' count when larger, and at least one), plus a row of measure
 * names for two or more measures. A member's header sits on its level's
 * row; a group that does not expand spans down to the last level row.
 */
export function ɵtmPivotColumns(
  axis: ɵTmPivotAxis,
  expanded: ReadonlySet<number>,
  measureCount: number,
  levelLabelCount: number,
): ɵTmPivotColumns {
  const measures = Math.max(1, measureCount);
  const levelRows = Math.max(1, axis.maxDepth, levelLabelCount);
  const headerRows = levelRows + (measures >= 2 ? 1 : 0);
  const slots: ɵTmPivotSlot[] = [];
  const cells: ɵTmPivotHeaderCell[] = [];
  const slotOf = new Int32Array(axis.members.length).fill(-1);
  const measureHandle = measures === 1 ? 0 : -1;
  // Section: groups and member headers, depth first. A frame remembers the
  // first group under an expanded member, to close its header after the
  // children.
  /** One level of the walk: a sibling list and where it stands. */
  interface Frame {
    /** The members walked at this level. */
    readonly siblings: readonly number[];
    /** The next sibling's position. */
    next: number;
    /** The expanded member whose children these are; -1 for the first level. */
    readonly owner: number;
    /** The first group under the owner. */
    readonly firstSlot: number;
  }
  const stack: Frame[] = [{ siblings: axis.firstLevel, next: 0, owner: -1, firstSlot: 0 }];
  while (stack.length > 0) {
    const frame = stack[stack.length - 1];
    if (frame.next < frame.siblings.length) {
      const index = frame.siblings[frame.next++];
      const member = axis.members[index];
      const depth = axis.depth[index];
      const canExpand = expandableAt(axis, index);
      const style = styleOf(member, depth, canExpand);
      if (canExpand && expanded.has(member.id)) {
        stack.push({ siblings: axis.children[index], next: 0, owner: index, firstSlot: slots.length });
        continue;
      }
      const slot = slots.length;
      slots.push({ index, kind: 'self', style });
      slotOf[index] = slot;
      cells.push({
        key: `m${member.id}`,
        kind: 'member',
        row: depth - 1,
        rowSpan: levelRows - depth + 1,
        start: slot * measures,
        end: (slot + 1) * measures,
        member: index,
        measure: -1,
        expandable: canExpand,
        expanded: false,
        resizes: measureHandle,
        style,
      });
      continue;
    }
    // The siblings are done: an expanded member closes with its group
    // header over its children and its subtotal column after them.
    stack.pop();
    const owner = frame.owner;
    if (owner === -1) {
      continue;
    }
    const member = axis.members[owner];
    const depth = axis.depth[owner];
    const style = styleOf(member, depth, true);
    const subtotal = slots.length;
    cells.push({
      key: `g${member.id}`,
      kind: 'group',
      row: depth - 1,
      rowSpan: 1,
      start: frame.firstSlot * measures,
      end: subtotal * measures,
      member: owner,
      measure: -1,
      expandable: true,
      expanded: true,
      resizes: -1,
      style,
    });
    slots.push({ index: owner, kind: 'subtotal', style });
    slotOf[owner] = subtotal;
    cells.push({
      key: `s${member.id}`,
      kind: 'subtotal',
      row: depth - 1,
      rowSpan: levelRows - depth + 1,
      start: subtotal * measures,
      end: (subtotal + 1) * measures,
      member: owner,
      measure: -1,
      expandable: false,
      expanded: false,
      resizes: measureHandle,
      style,
    });
  }
  // Section: the total, last, over every level row.
  if (axis.total !== -1) {
    const slot = slots.length;
    slots.push({ index: axis.total, kind: 'self', style: 'total' });
    slotOf[axis.total] = slot;
    cells.push({
      key: 't',
      kind: 'total',
      row: 0,
      rowSpan: levelRows,
      start: slot * measures,
      end: (slot + 1) * measures,
      member: axis.total,
      measure: -1,
      expandable: false,
      expanded: false,
      resizes: measureHandle,
      style: 'total',
    });
  }
  // Section: the measure names, one per data column.
  if (measures >= 2) {
    for (let slot = 0; slot < slots.length; slot++) {
      for (let measure = 0; measure < measures; measure++) {
        const column = slot * measures + measure;
        cells.push({
          key: `${slots[slot].kind === 'subtotal' ? 's' : 'm'}${axis.members[slots[slot].index].id}.${measure}`,
          kind: 'measure',
          row: levelRows,
          rowSpan: 1,
          start: column,
          end: column + 1,
          member: slots[slot].index,
          measure,
          expandable: false,
          expanded: false,
          resizes: measure,
          style: slots[slot].style,
        });
      }
    }
  }
  // Section: order the cells and index every row they occupy.
  cells.sort((a, b) => a.row - b.row || a.start - b.start);
  const rowCells: ɵTmPivotHeaderCell[][] = Array.from({ length: headerRows }, () => []);
  for (const cell of cells) {
    for (let row = cell.row; row < cell.row + cell.rowSpan; row++) {
      rowCells[row].push(cell);
    }
  }
  for (const row of rowCells) {
    row.sort((a, b) => a.start - b.start);
  }
  return {
    slots,
    measureCount: measures,
    dataCount: slots.length * measures,
    levelRows,
    headerRows,
    cells,
    rowCells,
    slotOf,
  };
}

/**
 * The header cell occupying a header row at a data column, found by a
 * binary search of the row's cells; `null` past the columns.
 */
export function ɵtmPivotHeaderAt(
  columns: ɵTmPivotColumns,
  row: number,
  column: number,
): ɵTmPivotHeaderCell | null {
  const cells = columns.rowCells[row];
  if (cells === undefined) {
    return null;
  }
  let low = 0;
  let high = cells.length - 1;
  while (low <= high) {
    const middle = (low + high) >> 1;
    const cell = cells[middle];
    if (column < cell.start) {
      high = middle - 1;
    } else if (column >= cell.end) {
      low = middle + 1;
    } else {
      return cell;
    }
  }
  return null;
}

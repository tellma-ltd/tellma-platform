// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import type { TmJson, TmJsonObject } from '../json/tm-json';
import type { TmEntityDescriptor } from './tm-entity-descriptors';

// A query's row set as wire objects (spec 0029 §6.2): code that reads rows as
// JSON, such as the display names and the pickers, reads the same camelCase
// members whether a row came from a details read or from a query. Each
// column's path nests by the JSON names of the navigations it crosses.

/** One column of a query's row set. */
export interface TmQueryRowSetColumn {
  /** The column's name, as the select spelled it. */
  readonly name: string;
  /** The column's Queryex type, such as `Numeric` or `String`. */
  readonly type: string;
  /** The digits after the point of a numeric column. */
  readonly scale?: number;
  /** Whether the column's cells may be `null`. */
  readonly nullable: boolean;
  /** The column's path in C# names, such as `['Parent', 'Name2']`; absent for an expression. */
  readonly path?: readonly string[];
  /** Whether the column is a grouping key. */
  readonly groupingKey: boolean;
}

/** A query's rows: typed columns and row-major cells; decimal and `long` cells are decimal text. */
export interface TmQueryRowSet {
  /** The columns, in select order. */
  readonly columns: readonly TmQueryRowSetColumn[];
  /** The rows, each one cell per column. */
  readonly rows: readonly (readonly TmJson[])[];
}

/**
 * A descriptor that also maps C# path names to wire names. Internal: the
 * descriptors `TmEntities.get` builds once the Entities chunk is loaded
 * implement it.
 */
export interface ɵTmJsonNames {
  /**
   * The wire names of a path's segments.
   *
   * @param path - The path in C# names.
   * @returns The JSON names, or `null` when the path does not resolve.
   */
  ɵjsonPath(path: readonly string[]): readonly string[] | null;
}

/**
 * The conventional wire name of a C# name: its first letter in lower case.
 * Internal: the display names read a row by it before the Entities chunk
 * loads.
 *
 * @param name - The C# name.
 * @returns The camelCase name.
 */
export function ɵtmJsonName(name: string): string {
  return name.charAt(0).toLowerCase() + name.slice(1);
}

/**
 * Turns a query's row set into wire objects: each column with a path nests
 * by the JSON names of its segments, `['Parent', 'Name2']` becoming
 * `{ parent: { name2: … } }`; a column without one, an expression, is left
 * out. Cells keep the row set's values.
 *
 * @param root - The descriptor of the entity the query reads.
 * @param set - The row set.
 * @returns One object per row, in order.
 *
 * @example
 * ```ts
 * const centers = tmRowsToObjects(inject(TmEntities).get('gl.Center'), result.rows);
 * const name = centers[0]['name'];
 * ```
 */
export function tmRowsToObjects(root: TmEntityDescriptor, set: TmQueryRowSet): readonly TmJsonObject[] {
  // --- Each column's JSON path, once: the descriptor's names, else the convention ---
  const names = (root as Partial<ɵTmJsonNames>).ɵjsonPath?.bind(root);
  const paths = set.columns.map((column) => column.path && (names?.(column.path) ?? column.path.map(ɵtmJsonName)));

  // --- One object per row, nesting each cell under its path ---
  return set.rows.map((cells) => {
    const row: Record<string, TmJson> = {};
    paths.forEach((path, index) => {
      if (path === undefined || path.length === 0) {
        return;
      }
      let target = row;
      for (const segment of path.slice(0, -1)) {
        const next = target[segment];
        target = (typeof next === 'object' && next !== null && !Array.isArray(next) ? next : (target[segment] = {})) as Record<string, TmJson>;
      }
      target[path[path.length - 1]] = cells[index] ?? null;
    });
    return row;
  });
}

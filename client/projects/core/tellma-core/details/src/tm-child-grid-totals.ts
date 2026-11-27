// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { type TmDecimal, TmDecimals } from '@tellma/core';

// A child grid's totals (spec 0031 §6.4): each numeric column summed over
// the rows exactly, through TmDecimals, never through a double, so a
// thousand lines of money add up to the cent. A decimal and a `long` reach
// the client as decimal text, an integer as a number; an empty cell and a
// half-typed one add nothing.

/**
 * The exact sum of one member over a collection's rows.
 *
 * @param rows - The rows, as the form model holds them.
 * @param member - The member's JSON name, such as `amount`.
 * @param scale - The member's fraction digits, which the sum carries even when every row is empty.
 * @returns The sum, as decimal text.
 *
 * @example
 * ```ts
 * ɵtmChildGridTotal([{ amount: '10.25' }, { amount: '0.75' }, { amount: null }], 'amount', 2); // '11.00'
 * ```
 */
export function ɵtmChildGridTotal(rows: readonly unknown[], member: string, scale: number): TmDecimal {
  // The start carries the scale, so an empty collection shows 0.00 for a money column.
  let total = (scale > 0 ? `0.${'0'.repeat(scale)}` : '0') as TmDecimal;
  for (const row of rows) {
    const value = row !== null && typeof row === 'object' ? (row as Readonly<Record<string, unknown>>)[member] : undefined;
    const addend = TmDecimals.isDecimal(value) ? value : Number.isSafeInteger(value) ? TmDecimals.parse(String(value)) : null;
    if (addend !== null) {
      total = TmDecimals.add(total, addend);
    }
  }
  return total;
}

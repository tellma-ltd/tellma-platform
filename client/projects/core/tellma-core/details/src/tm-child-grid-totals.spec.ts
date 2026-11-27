// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { ɵtmChildGridTotal } from './tm-child-grid-totals';

// A child grid's totals add the rows' values exactly: decimal text through
// TmDecimals, integers as whole numbers, and nothing for an empty or a
// half-typed cell.

describe("a child grid's totals", () => {
  it('add decimal text exactly, where a double would drift', () => {
    const rows = Array.from({ length: 10 }, () => ({ amount: '0.10' }));
    expect(ɵtmChildGridTotal(rows, 'amount', 2)).toBe('1.00');
    expect(ɵtmChildGridTotal([{ amount: '0.1' }, { amount: '0.2' }], 'amount', 2)).toBe('0.30');
  });

  it('keep every digit of amounts beyond what a double holds', () => {
    const rows = [{ amount: '9007199254740993.01' }, { amount: '1.02' }];
    expect(ɵtmChildGridTotal(rows, 'amount', 2)).toBe('9007199254740994.03');
  });

  it('add negative amounts and integers', () => {
    expect(ɵtmChildGridTotal([{ amount: '10.50' }, { amount: '-12.75' }], 'amount', 2)).toBe('-2.25');
    expect(ɵtmChildGridTotal([{ quantity: 3 }, { quantity: 4 }, { quantity: -2 }], 'quantity', 0)).toBe('5');
  });

  it('skip empty, missing and unreadable cells, and rows that are not objects', () => {
    const rows = [{ amount: '1.25' }, { amount: null }, {}, { amount: '' }, { amount: 'abc' }, { amount: 1.5 }, null, 'x'];
    expect(ɵtmChildGridTotal(rows, 'amount', 2)).toBe('1.25');
  });

  it("carry the column's scale over no rows, and widen to a row's longer fraction", () => {
    expect(ɵtmChildGridTotal([], 'amount', 2)).toBe('0.00');
    expect(ɵtmChildGridTotal([], 'quantity', 0)).toBe('0');
    expect(ɵtmChildGridTotal([{ amount: '1.005' }], 'amount', 2)).toBe('1.005');
  });
});

// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { TM_CHART_SPEC_SCHEMA, tmValidateChartSpec } from './tm-chart-schema';
import type { TmChartSpec } from './tm-chart-types';

import golden from './tm-chart-spec.schema.json';

/** A valid two-dimension column chart. */
const COLUMN: TmChartSpec = {
  type: 'column',
  title: 'Monthly sales',
  dimensions: [
    { label: 'Month', members: [{ label: 'Sep 2026' }, { label: 'Oct 2026' }] },
    { label: 'Customer type', members: [{ label: 'Corporate' }, { label: 'Retail' }] },
  ],
  measure: { label: 'Sales', format: { maxDecimals: 0 } },
  rows: [
    [0, 0, 365000],
    [0, 1, 120000],
    [1, 0, 298000],
    [1, 1, null],
  ],
  options: { stack: 'stacked', thresholds: [{ value: 300000, tone: 'positive' }] },
};

/** The spec with some members replaced, as plain JSON. */
function withChanges(changes: Record<string, unknown>): unknown {
  return { ...structuredClone(COLUMN), ...changes };
}

describe('TM_CHART_SPEC_SCHEMA', () => {
  it('matches the committed golden', () => {
    // A change here changes the contract agents and the MCP server write
    // specs against: regenerate the golden and review it like an API golden.
    expect(TM_CHART_SPEC_SCHEMA).toEqual(golden);
  });
});

describe('tmValidateChartSpec', () => {
  it('accepts a valid spec of every type', () => {
    expect(tmValidateChartSpec(COLUMN)).toEqual([]);
    const pie = withChanges({
      type: 'pie',
      dimensions: [COLUMN.dimensions[0]],
      rows: [
        [0, null, 1],
        [1, null, 2],
      ],
    });
    expect(tmValidateChartSpec(pie)).toEqual([]);
    const card = withChanges({
      type: 'card',
      dimensions: [],
      rows: [[null, null, 42]],
      options: undefined,
    });
    expect(tmValidateChartSpec(JSON.parse(JSON.stringify(card)))).toEqual([]);
    const empty = withChanges({ rows: [] });
    expect(tmValidateChartSpec(empty)).toEqual([]);
  });

  it('reports what the schema refuses, with JSON pointers', () => {
    expect(tmValidateChartSpec(null)).toEqual([{ pointer: '', code: 'type' }]);
    const untitled: Record<string, unknown> = { ...COLUMN };
    delete untitled['title'];
    expect(tmValidateChartSpec(untitled)).toEqual([{ pointer: '/title', code: 'required' }]);
    expect(tmValidateChartSpec(withChanges({ colour: 'red' }))).toEqual([
      { pointer: '/colour', code: 'unknown-property' },
    ]);
    expect(tmValidateChartSpec(withChanges({ type: 'radar' }))).toEqual([
      { pointer: '/type', code: 'enum' },
    ]);
    expect(tmValidateChartSpec(withChanges({ rows: [[0, 0]] }))).toEqual([
      { pointer: '/rows/0', code: 'too-few-items' },
    ]);
    expect(tmValidateChartSpec(withChanges({ rows: [[0, 0, 1, 2]] }))).toEqual([
      { pointer: '/rows/0/3', code: 'too-many-items' },
    ]);
    expect(tmValidateChartSpec(withChanges({ rows: [[-1, 0, 1]] }))).toEqual([
      { pointer: '/rows/0/0', code: 'minimum' },
    ]);
    expect(tmValidateChartSpec(withChanges({ rows: [[0.5, 0, 1]] }))).toEqual([
      { pointer: '/rows/0/0', code: 'type' },
    ]);
    expect(
      tmValidateChartSpec(
        withChanges({ measure: { label: 'Sales', format: { maxDecimals: 21 } } }),
      ),
    ).toEqual([{ pointer: '/measure/format/maxDecimals', code: 'maximum' }]);
    expect(
      tmValidateChartSpec(
        withChanges({ dimensions: [...COLUMN.dimensions, COLUMN.dimensions[0]] }),
      ),
    ).toEqual([{ pointer: '/dimensions', code: 'too-many-items' }]);
    expect(tmValidateChartSpec(withChanges({ rows: [[0, 0, Number.NaN]] }))).toEqual([
      { pointer: '/rows/0/2', code: 'type' },
    ]);
  });

  it('checks the dimension count of each type', () => {
    expect(tmValidateChartSpec(withChanges({ type: 'pie' }))).toEqual([
      { pointer: '/dimensions', code: 'dimension-count' },
    ]);
    expect(tmValidateChartSpec(withChanges({ type: 'card', rows: [] }))).toEqual([
      { pointer: '/dimensions', code: 'dimension-count' },
    ]);
    expect(tmValidateChartSpec(withChanges({ dimensions: [], rows: [] }))).toEqual([
      { pointer: '/dimensions', code: 'dimension-count' },
    ]);
  });

  it('checks the rows against the member lists', () => {
    expect(tmValidateChartSpec(withChanges({ rows: [[2, 0, 1]] }))).toEqual([
      { pointer: '/rows/0/0', code: 'member-index' },
    ]);
    expect(tmValidateChartSpec(withChanges({ rows: [[0, null, 1]] }))).toEqual([
      { pointer: '/rows/0/1', code: 'member-index' },
    ]);
    const single = withChanges({ dimensions: [COLUMN.dimensions[0]], rows: [[0, 1, 1]] });
    expect(tmValidateChartSpec(single)).toEqual([{ pointer: '/rows/0/1', code: 'member-index' }]);
    expect(
      tmValidateChartSpec(
        withChanges({
          rows: [
            [0, 0, 1],
            [0, 0, 2],
          ],
        }),
      ),
    ).toEqual([{ pointer: '/rows/1', code: 'duplicate-point' }]);
    const card = withChanges({
      type: 'card',
      dimensions: [],
      rows: [
        [null, null, 1],
        [null, null, 2],
      ],
    });
    expect(tmValidateChartSpec(card)).toEqual([
      { pointer: '/rows', code: 'row-count' },
      { pointer: '/rows/1', code: 'duplicate-point' },
    ]);
  });

  it('checks that parts join to their label, and the axis range', () => {
    const parts = withChanges({
      dimensions: [
        {
          label: 'Account',
          members: [{ label: '1100 Cash', parts: ['1100', 'Cash'] }, { label: 'B' }],
        },
        COLUMN.dimensions[1],
      ],
    });
    expect(tmValidateChartSpec(parts)).toEqual([
      { pointer: '/dimensions/0/members/0/parts', code: 'parts-mismatch' },
    ]);
    // A display name's parts join with the platform's separator.
    const joined = withChanges({
      dimensions: [
        {
          label: 'Account',
          members: [{ label: '1100 - Cash', parts: ['1100', 'Cash'] }, { label: 'B' }],
        },
        COLUMN.dimensions[1],
      ],
    });
    expect(tmValidateChartSpec(joined)).toEqual([]);
    expect(tmValidateChartSpec(withChanges({ options: { min: 10, max: 10 } }))).toEqual([
      { pointer: '/options/max', code: 'min-max' },
    ]);
  });

  it('refuses a bidi control in any text, with the pointer of each', () => {
    const marked = withChanges({
      title: 'Sales\u200F',
      dimensions: [
        {
          label: 'Month',
          members: [{ label: '\u2067Sep\u2069' }, { label: 'Oct', parts: ['O', '\u202Ect'] }],
        },
        COLUMN.dimensions[1],
      ],
      measure: { label: 'Sales\u061C' },
    });
    const problems = tmValidateChartSpec(marked).filter(
      (problem) => problem.code === 'bidi-control',
    );
    expect(problems.map((problem) => problem.pointer)).toEqual([
      '/title',
      '/dimensions/0/members/0/label',
      '/dimensions/0/members/1/parts/1',
      '/measure/label',
    ]);
  });

  it('escapes a JSON pointer’s tokens', () => {
    expect(tmValidateChartSpec(withChanges({ 'a/b~c': 1 }))).toEqual([
      { pointer: '/a~1b~0c', code: 'unknown-property' },
    ]);
  });
});

// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { TestBed } from '@angular/core/testing';
import { TmEntities, type TmColumnInput, type TmPlanRow, type TmSelectPlan } from '@tellma/core';

import { tmEntityRig, type TmEntityRigOptions } from '../../src/entity/tm-entity.testing.util';
import { tmGridColumnFor, type TmGridColumnOptions } from './tm-grid-column-for';
import { tmSelectPlan } from './tm-select-plan';

// The read-only grid columns of a plan's columns: each type's grid type,
// alignment and width, the exact display of decimals, sort, and a form's
// grid read by JSON names. The cells a form's child grid edits are the
// editing entry point's editable columns'.

/** A user who reads everything. */
const SYSTEM: TmEntityRigOptions = { access: { isSystem: true } };

/** A plan made in the rig's app. */
function plan(root: string, keys: readonly TmColumnInput<string>[]): TmSelectPlan {
  return TestBed.runInInjectionContext(() => tmSelectPlan(TestBed.inject(TmEntities).get(root), keys));
}

/** A grid column made in the rig's app. */
function gridColumn(root: string, key: string, options?: TmGridColumnOptions): ReturnType<typeof tmGridColumnFor> {
  return TestBed.runInInjectionContext(() => tmGridColumnFor(plan(root, [key]).columns[0]!, options));
}

describe('tmGridColumnFor', () => {
  it('binds a search grid’s column by its key, sorting when it orders', async () => {
    await tmEntityRig(SYSTEM);
    expect(gridColumn('acme.Customer', 'Code')).toMatchObject({ key: 'Code', type: 'text', sortable: true, readonly: true, width: 200 });
    expect(gridColumn('acme.Customer', 'Image')).toMatchObject({ key: 'Image', type: 'custom', sortable: false, readonly: true, width: 72 });
  });

  it('shows a decimal exactly at its scale, aligned to the physical right, in a form’s grid that does not edit', async () => {
    await tmEntityRig(SYSTEM);
    const column = gridColumn('acme.Customer', 'CreditLimit', { rows: 'model' });
    expect(column).toMatchObject({ key: 'creditLimit', type: 'number', align: 'right', minDecimals: 4, maxDecimals: 4, readonly: true });
    expect(column.format!('1234567890123.5', {})).toBe('1,234,567,890,123.5000');
  });

  it('takes a checkbox for a flag', async () => {
    await tmEntityRig(SYSTEM);
    expect(gridColumn('acme.Customer', 'IsActive')).toMatchObject({ type: 'boolean', readonly: true });
  });

  it('keeps every cell of a form’s grid read-only', async () => {
    await tmEntityRig({ ...SYSTEM, languages: 2 });
    expect(gridColumn('acme.Customer', 'Code', { rows: 'model' }).readonly).toBe(true);
    expect(gridColumn('acme.Customer', 'Name', { rows: 'model' }).readonly).toBe(true);
    expect(gridColumn('core.User', 'Kind', { rows: 'model' }).readonly).toBe(true);
  });

  it('shows a navigation read-only in a form’s grid that does not edit', async () => {
    await tmEntityRig(SYSTEM);
    const currency = gridColumn('acme.Customer', 'Currency', { rows: 'model' });
    expect(currency).toMatchObject({ type: 'text', readonly: true });
    expect(currency.value!({ currencyId: 3, currency: { id: 3 } })).toBe(3);
  });

  it('reads a form model’s row by its JSON names, a read-only group in the active slot', async () => {
    await tmEntityRig({ ...SYSTEM, languages: 2, language: 'ar' });
    const name = gridColumn('acme.Customer', 'Name', { rows: 'model' });
    expect(name).toMatchObject({ type: 'text', readonly: true });
    expect(name.key).toBeUndefined();
    expect(name.value!({ name: 'Acme', name2: 'أكمي' })).toBe('أكمي');
    expect(name.value!({ name: 'Beta', name2: null })).toBe('Beta');
    const code = gridColumn('acme.Customer', 'Currency.Code', { rows: 'model' });
    expect(code.value!({ currency: { code: 'SAR' } })).toBe('SAR');
  });

  it('formats a search grid’s cell as the plan does', async () => {
    await tmEntityRig(SYSTEM);
    const centers = plan('gl.Center', ['Parent']);
    const [row] = centers.rows({
      columns: ['Id', 'Parent.Id', 'Parent.Code'].map((term) => ({ name: term, type: 'String', nullable: true, path: term.split('.'), groupingKey: false })),
      rows: [[5, 2, 'R-1']],
    });
    const column = TestBed.runInInjectionContext(() => tmGridColumnFor(centers.columns[0]!));
    expect(column.format!(2, row as TmPlanRow)).toBe('R-1');
  });
});

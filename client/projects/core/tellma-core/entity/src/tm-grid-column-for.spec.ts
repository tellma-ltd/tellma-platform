// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { computed, Injector } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { TmEntities, TmLocalization, type TmColumnInput, type TmPlanRow } from '@tellma/core';
import { TM_PARSE_ERROR } from '@tellma/core-ui/contracts';

import { tmEntityRig, type TmEntityRigOptions } from '../../src/entity/tm-entity.testing.util';
import { tmSettle } from '../../src/routes/tm-app.testing.util';
import { tmGridColumnFor, type TmGridColumnOptions } from './tm-grid-column-for';
import { tmSelectPlan, type TmSelectPlan } from './tm-select-plan';

// The grid columns of a plan's columns: each type's grid type, alignment
// and width, the exact display and parse of decimals and longs, enum
// options, sort, and which cells a form's child grid edits.

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

/** The parse context of an English grid. */
const EN = { locale: 'en' };

describe('tmGridColumnFor', () => {
  it('binds a search grid’s column by its key, sorting when it orders', async () => {
    await tmEntityRig(SYSTEM);
    expect(gridColumn('acme.Customer', 'Code')).toMatchObject({ key: 'Code', type: 'text', sortable: true, readonly: true, width: 200 });
    expect(gridColumn('acme.Customer', 'Image')).toMatchObject({ key: 'Image', type: 'custom', sortable: false, readonly: true, width: 72 });
  });

  it('shows a decimal exactly at its scale, aligned to the physical right, and parses it to decimal text', async () => {
    await tmEntityRig(SYSTEM);
    const column = gridColumn('acme.Customer', 'CreditLimit', { editable: true });
    expect(column).toMatchObject({ key: 'creditLimit', type: 'number', align: 'right', minDecimals: 4, maxDecimals: 4, readonly: false });
    expect(column.format!('1234567890123.5', {})).toBe('1,234,567,890,123.5000');
    expect(column.parse!('1,500.25', EN)).toBe('1500.25');
    expect(column.parse!('', EN)).toBeNull();
    expect(column.parse!('abc', EN)).toBe(TM_PARSE_ERROR);
    expect(column.parse!('12345678901234567', EN)).toBe(TM_PARSE_ERROR);
  });

  it('shows and parses a long exactly, with no fraction', async () => {
    await tmEntityRig(SYSTEM);
    const column = gridColumn('acme.Customer', 'ExternalReference', { editable: true });
    expect(column).toMatchObject({ type: 'number', maxDecimals: 0 });
    expect(column.format!('9007199254740993', {})).toBe('9,007,199,254,740,993');
    expect(column.parse!('42', EN)).toBe('42');
  });

  it('leaves a plain integer to the grid’s own number display', async () => {
    await tmEntityRig(SYSTEM);
    const column = gridColumn('acme.Currency', 'DecimalPlaces', { editable: true });
    expect(column).toMatchObject({ type: 'number', align: 'right', maxDecimals: 0 });
    expect(column.format).toBeUndefined();
  });

  it('offers an enum’s values in order, by their labels', async () => {
    await tmEntityRig(SYSTEM);
    const column = gridColumn('acme.Customer', 'CustomerType', { editable: true });
    expect(column.type).toBe('enum');
    const options = column.options as readonly { readonly value: string }[];
    expect(options.map((option) => option.value)).toEqual(['Retail', 'wholesale-trade']);
    expect((column.optionLabel as (option: { readonly value: string }) => string)(options[0]!)).toBe('Retail');
    expect((column.optionValue as (option: { readonly value: string }) => string)(options[0]!)).toBe('Retail');
  });

  it('takes a checkbox for a flag', async () => {
    await tmEntityRig(SYSTEM);
    expect(gridColumn('acme.Customer', 'IsActive')).toMatchObject({ type: 'boolean', readonly: true });
  });

  it('keeps server-owned values and instants read-only in a child grid', async () => {
    await tmEntityRig({ ...SYSTEM, languages: 2 });
    expect(gridColumn('acme.Customer', 'IsActive', { editable: true }).readonly).toBe(true);
    expect(gridColumn('acme.Customer', 'CreatedAt', { editable: true })).toMatchObject({ type: 'text', readonly: true });
    expect(gridColumn('acme.Customer', 'Code', { editable: false }).readonly).toBe(true);
  });

  it('picks a navigation’s related row in a child grid through the binding, under its key’s JSON name', async () => {
    const rig = await tmEntityRig({ ...SYSTEM, languages: 2 });
    rig.names.remember('acme.Currency', [{ id: 3, code: 'SAR' }]);
    const currency = gridColumn('acme.Customer', 'Currency', { editable: true });
    expect(currency).toMatchObject({ key: 'currencyId', type: 'entity', readonly: false, width: 200 });
    expect([typeof currency.search, typeof currency.itemLabel, typeof currency.resolvePastedLabels]).toEqual(['function', 'function', 'function']);
    expect(currency.format!(3, {})).toBe('SAR');
    expect(currency.formatParts!(3, {})).toEqual(['SAR']);
    expect([currency.format!(null, {}), currency.formatParts!(null, {})]).toEqual(['', null]);
  });

  it('keeps a navigation read-only in a child grid when the server writes its key or the user may not search its target', async () => {
    await tmEntityRig({ ...SYSTEM });
    expect(gridColumn('acme.Customer', 'CreatedBy', { editable: true })).toMatchObject({ type: 'entity', readonly: true });
    TestBed.resetTestingModule();
    await tmEntityRig({ access: { securables: [{ resource: 'acme.Customer', action: 'Read', hasFilter: false }] } });
    expect(gridColumn('acme.Customer', 'Currency', { editable: true })).toMatchObject({ type: 'entity', readonly: true });
  });

  it('shows a navigation read-only in a form’s grid that does not edit', async () => {
    await tmEntityRig(SYSTEM);
    const currency = gridColumn('acme.Customer', 'Currency', { rows: 'model' });
    expect(currency).toMatchObject({ type: 'text', readonly: true });
    expect(currency.value!({ currencyId: 3, currency: { id: 3 } })).toBe(3);
  });

  it('edits a write-once value in a new row only', async () => {
    await tmEntityRig(SYSTEM);
    const readonly = gridColumn('core.User', 'Kind', { editable: true }).readonly as (row: unknown) => boolean;
    expect(readonly({ id: 5 })).toBe(true);
    expect(readonly({ id: -1 })).toBe(false);
    expect(readonly({ id: null })).toBe(false);
  });

  it('reads a form model’s row by its JSON names, a read-only group in the active slot', async () => {
    await tmEntityRig({ ...SYSTEM, languages: 2, language: 'ar' });
    const name = gridColumn('acme.Customer', 'Name', { rows: 'model' });
    expect(name).toMatchObject({ type: 'text', readonly: true });
    expect(name.key).toBeUndefined();
    expect(name.value!({ name: 'Acme', name2: 'أكمي' })).toBe('أكمي');
    expect(name.value!({ name: 'Beta', name2: null })).toBe('Beta');
    const code = gridColumn('acme.Customer', 'Currency.Code', { editable: true });
    expect(code.value!({ currency: { code: 'SAR' } })).toBe('SAR');
  });

  it('edits a group’s active slot in a child grid, by the slot’s JSON name', async () => {
    await tmEntityRig({ ...SYSTEM, languages: 2, language: 'ar' });
    expect(gridColumn('acme.Customer', 'Name', { editable: true })).toMatchObject({ key: 'name2', type: 'text', readonly: false });
  });

  it('edits slot 1 of a group in a child grid in the first language, and in a one-language tenant', async () => {
    await tmEntityRig({ ...SYSTEM, languages: 2 });
    expect(gridColumn('acme.Customer', 'Name', { editable: true })).toMatchObject({ key: 'name', type: 'text', readonly: false });
    TestBed.resetTestingModule();
    await tmEntityRig(SYSTEM);
    expect(gridColumn('acme.Customer', 'Name', { editable: true })).toMatchObject({ key: 'name', type: 'text', readonly: false });
  });

  it('makes a group’s column again for the new slot after a language switch, when made in a computed', async () => {
    await tmEntityRig({ ...SYSTEM, languages: 2 });
    const column = plan('acme.Customer', ['Name']).columns[0]!;
    const injector = TestBed.inject(Injector);
    const config = computed(() => tmGridColumnFor(column, { editable: true, injector }));
    expect(config().key).toBe('name');
    const l10n = TestBed.inject(TmLocalization);
    l10n.setLanguage('ar');
    // The switch lands once the language's strings have loaded, which can
    // take more than one settle while the whole suite runs.
    await vi.waitFor(async () => {
      await tmSettle();
      expect(l10n.language()).toBe('ar');
    });
    expect(config().key).toBe('name2');
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

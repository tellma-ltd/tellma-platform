// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { Component, computed, Injector, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { form } from '@angular/forms/signals';
import { TmEntities, TmLocalization, type TmColumnInput, type TmSelectPlan } from '@tellma/core';
import { TM_PARSE_ERROR } from '@tellma/core-ui/contracts';
import { TmGrid, TmGridColumn } from '@tellma/core-ui/grid';
import { tmSelectPlan } from '@tellma/core/entity';

import { tmEntityRig, type TmEntityRigOptions } from '../../src/entity/tm-entity.testing.util';
import { tmSettle, tmSettleUntil } from '../../src/routes/tm-app.testing.util';
import { tmEditableGridColumnFor } from './tm-editable-grid-column';

// The grid columns of a form's child grid that edits its cells: the exact
// parse of decimals and longs, enum options, which cells edit, and a
// navigation picked through the entity picker binding.

/** A user who reads everything. */
const SYSTEM: TmEntityRigOptions = { access: { isSystem: true } };

/** A plan made in the rig's app. */
function plan(root: string, keys: readonly TmColumnInput<string>[]): TmSelectPlan {
  return TestBed.runInInjectionContext(() => tmSelectPlan(TestBed.inject(TmEntities).get(root), keys));
}

/** An editable grid column made in the rig's app. */
function gridColumn(root: string, key: string): ReturnType<typeof tmEditableGridColumnFor> {
  return TestBed.runInInjectionContext(() => tmEditableGridColumnFor(plan(root, [key]).columns[0]!));
}

/** The parse context of an English grid. */
const EN = { locale: 'en' };

/** A row of a customer's contacts. */
interface TmContactRow {
  /** The row's id. */
  readonly id: number;
  /** The contact's customer. */
  readonly customerId: number | null;
}

/** A child grid of contacts whose customer column a plan's column binds. */
@Component({
  imports: [TmGrid, TmGridColumn],
  template: `
    <tm-grid gridId="contacts" [field]="lines" [rowId]="rowId" style="block-size: 200px">
      <tm-grid-column
        [key]="customer.key!"
        [type]="customer.type!"
        header="Customer"
        [search]="customer.search"
        [itemId]="customer.itemId"
        [itemLabel]="customer.itemLabel"
        [format]="customer.format"
        [formatParts]="customer.formatParts"
        [width]="200"
      />
    </tm-grid>
  `,
})
class TmContactsHost {
  /** The customer column. */
  readonly customer = gridColumn('acme.CustomerContact', 'Customer');
  /** The rows. */
  readonly model = signal<TmContactRow[]>([{ id: 1, customerId: 7 }]);
  /** The rows' form. */
  readonly lines = form(this.model);
  /** A row's id. */
  readonly rowId = (row: TmContactRow): number => row.id;
}

describe('tmEditableGridColumnFor', () => {
  it('shows a decimal exactly at its scale, aligned to the physical right, and parses it to decimal text', async () => {
    await tmEntityRig(SYSTEM);
    const column = gridColumn('acme.Customer', 'CreditLimit');
    expect(column).toMatchObject({ key: 'creditLimit', type: 'number', align: 'right', minDecimals: 4, maxDecimals: 4, readonly: false });
    expect(column.format!('1234567890123.5', {})).toBe('1,234,567,890,123.5000');
    expect(column.parse!('1,500.25', EN)).toBe('1500.25');
    expect(column.parse!('', EN)).toBeNull();
    expect(column.parse!('abc', EN)).toBe(TM_PARSE_ERROR);
    expect(column.parse!('12345678901234567', EN)).toBe(TM_PARSE_ERROR);
  });

  it('shows and parses a long exactly, with no fraction', async () => {
    await tmEntityRig(SYSTEM);
    const column = gridColumn('acme.Customer', 'ExternalReference');
    expect(column).toMatchObject({ type: 'number', maxDecimals: 0 });
    expect(column.format!('9007199254740993', {})).toBe('9,007,199,254,740,993');
    expect(column.parse!('42', EN)).toBe('42');
  });

  it('leaves a plain integer to the grid’s own number display', async () => {
    await tmEntityRig(SYSTEM);
    const column = gridColumn('acme.Currency', 'DecimalPlaces');
    expect(column).toMatchObject({ type: 'number', align: 'right', maxDecimals: 0 });
    expect(column.format).toBeUndefined();
  });

  it('offers an enum’s values in order, by their labels', async () => {
    await tmEntityRig(SYSTEM);
    const column = gridColumn('acme.Customer', 'CustomerType');
    expect(column.type).toBe('enum');
    const options = column.options as readonly { readonly value: string }[];
    expect(options.map((option) => option.value)).toEqual(['Retail', 'wholesale-trade']);
    expect((column.optionLabel as (option: { readonly value: string }) => string)(options[0]!)).toBe('Retail');
    expect((column.optionValue as (option: { readonly value: string }) => string)(options[0]!)).toBe('Retail');
  });

  it('keeps server-owned values and instants read-only in a child grid', async () => {
    await tmEntityRig({ ...SYSTEM, languages: 2 });
    expect(gridColumn('acme.Customer', 'IsActive').readonly).toBe(true);
    expect(gridColumn('acme.Customer', 'CreatedAt')).toMatchObject({ type: 'text', readonly: true });
  });

  it('picks a navigation’s related row in a child grid through the binding, under its key’s JSON name', async () => {
    const rig = await tmEntityRig({ ...SYSTEM, languages: 2 });
    rig.names.remember('acme.Currency', [{ id: 3, code: 'SAR' }]);
    const currency = gridColumn('acme.Customer', 'Currency');
    expect(currency).toMatchObject({ key: 'currencyId', type: 'entity', readonly: false, width: 200 });
    expect([typeof currency.search, typeof currency.itemLabel, typeof currency.resolvePastedLabels]).toEqual(['function', 'function', 'function']);
    expect(currency.format!(3, {})).toBe('SAR');
    expect(currency.formatParts!(3, {})).toEqual(['SAR']);
    expect([currency.format!(null, {}), currency.formatParts!(null, {})]).toEqual(['', null]);
  });

  it('draws a prefixed display name’s prefix and name each in its own isolate', async () => {
    const rig = await tmEntityRig(SYSTEM);
    rig.names.remember('acme.Customer', [{ id: 7, code: '1101', name: 'Cash' }]);
    expect(gridColumn('acme.CustomerContact', 'Customer').formatParts!(7, {})).toEqual(['1101', 'Cash']);

    const fixture = TestBed.createComponent(TmContactsHost);
    await fixture.whenStable();
    await tmSettle();
    const runs = (fixture.nativeElement as HTMLElement).querySelectorAll('.tm-grid__text > bdi');
    expect(Array.from(runs, (run) => run.textContent)).toEqual(['1101', 'Cash']);
  });

  it('keeps a navigation read-only in a child grid when the server writes its key or the user may not search its target', async () => {
    await tmEntityRig({ ...SYSTEM });
    expect(gridColumn('acme.Customer', 'CreatedBy')).toMatchObject({ type: 'entity', readonly: true });
    TestBed.resetTestingModule();
    await tmEntityRig({ access: { securables: [{ resource: 'acme.Customer', action: 'Read', hasFilter: false }] } });
    expect(gridColumn('acme.Customer', 'Currency')).toMatchObject({ type: 'entity', readonly: true });
  });

  it('edits a write-once value in a new row only', async () => {
    await tmEntityRig(SYSTEM);
    const readonly = gridColumn('core.User', 'Kind').readonly as (row: unknown) => boolean;
    expect(readonly({ id: 5 })).toBe(true);
    expect(readonly({ id: -1 })).toBe(false);
    expect(readonly({ id: null })).toBe(false);
  });

  it('edits a group’s active slot in a child grid, by the slot’s JSON name', async () => {
    await tmEntityRig({ ...SYSTEM, languages: 2, language: 'ar' });
    expect(gridColumn('acme.Customer', 'Name')).toMatchObject({ key: 'name2', type: 'text', readonly: false });
  });

  it('edits slot 1 of a group in a child grid in the first language, and in a one-language tenant', async () => {
    await tmEntityRig({ ...SYSTEM, languages: 2 });
    expect(gridColumn('acme.Customer', 'Name')).toMatchObject({ key: 'name', type: 'text', readonly: false });
    TestBed.resetTestingModule();
    await tmEntityRig(SYSTEM);
    expect(gridColumn('acme.Customer', 'Name')).toMatchObject({ key: 'name', type: 'text', readonly: false });
  });

  it('makes a group’s column again for the new slot after a language switch, when made in a computed', async () => {
    await tmEntityRig({ ...SYSTEM, languages: 2 });
    const column = plan('acme.Customer', ['Name']).columns[0]!;
    const injector = TestBed.inject(Injector);
    const config = computed(() => tmEditableGridColumnFor(column, { injector }));
    expect(config().key).toBe('name');
    const l10n = TestBed.inject(TmLocalization);
    l10n.setLanguage('ar');
    // The switch lands once its loads have, the locale write's chunk among them.
    await tmSettleUntil(() => l10n.language() === 'ar');
    expect(config().key).toBe('name2');
  });
});

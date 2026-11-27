// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { Component, signal } from '@angular/core';
import { TestBed, type ComponentFixture } from '@angular/core/testing';
import { TestbedHarnessEnvironment } from '@angular/cdk/testing/testbed';
import type { HarnessLoader } from '@angular/cdk/testing';
import { disabled, form, FormField, required } from '@angular/forms/signals';
import type { TmId, TmJsonObject, TmQueryRowSet } from '@tellma/core';
import { TmEntityFieldHarness, TmEntityMultiFieldHarness, type TmFakeApiCall } from '@tellma/core-testing';
import { TmFormField } from '@tellma/core-ui/form-field';
import { TmFormFieldHarness } from '@tellma/core-ui-testing';

import { tmEntityRig, type TmEntityRig, type TmEntityRigOptions } from '../../src/entity/tm-entity.testing.util';
import { tmSettle, tmSettleUntil } from '../../src/routes/tm-app.testing.util';
import { TmEntityField } from './tm-entity-field';
import { TmEntityMultiField } from './tm-entity-multi-field';

// The entity fields in a form over the entity registry's test app: a
// search and a pick into the bound field, the row's display name, the
// field's state forwarded to the picker and labelled by tm-form-field, an
// unbound field's model, a required empty list, a list's cap, and the
// read-only field of an entity the user may not search.

/** A user who may do everything. */
const SYSTEM: TmEntityRigOptions = { access: { isSystem: true } };

/** The centers the fake server holds: a root and two children. */
const CENTERS: readonly TmJsonObject[] = [
  { id: 1, code: 'A' },
  { id: 2, code: 'B', parent: { id: 1, code: 'A' } },
  { id: 3, code: 'C', parent: { id: 1, code: 'A' } },
];

/**
 * Answers the centers' query route from {@link CENTERS}, in the select's
 * columns, so the client names and describes them.
 *
 * @param call - The call.
 * @returns The page.
 */
function centers(call: TmFakeApiCall): { rows: TmQueryRowSet } {
  const select = String((call.body as { select: string }).select).split(',');
  const at = (row: TmJsonObject, path: string): unknown =>
    path.split('.').reduce<unknown>((value, segment) => (value as Record<string, unknown> | undefined)?.[segment.charAt(0).toLowerCase() + segment.slice(1)], row) ?? null;
  return {
    rows: {
      columns: select.map((term) => ({ name: term, type: term.endsWith('Id') ? 'Numeric' : 'String', nullable: true, path: term.split('.'), groupingKey: false })),
      rows: CENTERS.map((row) => select.map((term) => at(row, term))) as TmQueryRowSet['rows'],
    },
  };
}

/** The host's starting values and rules, set before it is created. */
let start: { centerId: TmId | null; centerIds: readonly TmId[]; disable: boolean } = { centerId: null, centerIds: [], disable: false };

/** A form of one entity field and one entity list field, each required, and an unbound field. */
@Component({
  imports: [FormField, TmFormField, TmEntityField, TmEntityMultiField],
  template: `
    <tm-form-field label="Center" data-testid="center">
      <tm-entity-field entity="gl.Center" [formField]="form.centerId" />
    </tm-form-field>
    <tm-form-field label="Centers" data-testid="centers">
      <tm-entity-multi-field entity="gl.Center" [formField]="form.centerIds" [maxItems]="maxItems()" />
    </tm-form-field>
    <div data-testid="currency">
      <tm-entity-field entity="acme.Currency" aria-label="Currency" [(value)]="currencyId" />
    </div>
    <div data-testid="customer">
      <tm-entity-field entity="acme.Customer" aria-label="Customer" [(value)]="customerId" />
    </div>
  `,
})
class TmFieldsHost {
  /** The model. */
  readonly model = signal({ centerId: start.centerId, centerIds: start.centerIds });
  /** The form: both fields required, the single one disabled when the test says. */
  readonly form = form(this.model, (path) => {
    required(path.centerId);
    required(path.centerIds);
    if (start.disable) {
      disabled(path.centerId);
    }
  });
  /** The most centers the list field takes; no cap when unset. */
  readonly maxItems = signal<number | undefined>(undefined);
  /** The unbound currency field's value. */
  readonly currencyId = signal<TmId | null>(null);
  /** The unbound customer field's value. */
  readonly customerId = signal<TmId | null>(null);
}

/** A started host. */
interface TmFieldsTest {
  /** The rig. */
  readonly rig: TmEntityRig;
  /** The fixture. */
  readonly fixture: ComponentFixture<TmFieldsHost>;
  /** The host. */
  readonly host: TmFieldsHost;
  /** The harness loader. */
  readonly loader: HarnessLoader;
}

/**
 * Starts the host.
 *
 * @param options - The rig's options; a system user when omitted.
 * @param values - The starting values and rules.
 * @returns The test.
 */
async function startHost(options: TmEntityRigOptions = SYSTEM, values: Partial<typeof start> = {}): Promise<TmFieldsTest> {
  const rig = await tmEntityRig(options);
  rig.api.respond('centers/query', centers);
  start = { centerId: null, centerIds: [], disable: false, ...values };
  const fixture = TestBed.createComponent(TmFieldsHost);
  await fixture.whenStable();
  await tmSettle();
  return { rig, fixture, host: fixture.componentInstance, loader: TestbedHarnessEnvironment.loader(fixture) };
}

/**
 * A harness inside one test id.
 *
 * @param loader - The loader.
 * @param id - The test id.
 * @returns The child loader.
 */
function within(loader: HarnessLoader, id: string): Promise<HarnessLoader> {
  return loader.getChildLoader(`[data-testid="${id}"]`);
}

/**
 * Waits for the views and the reads they started to settle.
 *
 * @param fixture - The fixture.
 */
async function settle(fixture: ComponentFixture<unknown>): Promise<void> {
  await tmSettle();
  await fixture.whenStable();
}

/**
 * Waits for the next frame and the reads it starts: a display name read
 * for an unknown row flushes on a frame, which must not outlive the test.
 *
 * @param fixture - The fixture.
 */
async function frame(fixture: ComponentFixture<unknown>): Promise<void> {
  await new Promise<void>((resolve) => requestAnimationFrame(() => resolve()));
  await settle(fixture);
}

describe('TmEntityField', () => {
  it('picks a row into the bound form field, described by its parent and named by its display name', async () => {
    const { loader, host, fixture } = await startHost();
    const field = await (await within(loader, 'center')).getHarness(TmEntityFieldHarness);
    const picker = await field.getPicker();
    await picker.open();
    await settle(fixture);

    const rows = await picker.getOptions();
    const entities = [];
    for (const row of rows) {
      if (!(await row.isAction())) {
        entities.push([await row.getText(), await row.getDescription(), await row.getLabelParts()]);
      }
    }
    expect(entities).toEqual([
      ['A', null, ['A']],
      ['B', 'A', ['B']],
      ['C', 'A', ['C']],
    ]);

    await picker.selectOptionByLabel('B');
    await settle(fixture);
    expect(host.model().centerId).toBe(2);
    expect(await field.getText()).toBe('B');
  });

  it('shows a chosen row by its display name once read', async () => {
    const { loader, rig, fixture } = await startHost(SYSTEM, { centerId: 3 });
    const field = await (await within(loader, 'center')).getHarness(TmEntityFieldHarness);
    // The read flushes on a frame and its code is in the entity model's chunk,
    // which each read loads first; the picker then shows the name.
    await tmSettleUntil(() => rig.names.label('gl.Center', 3)() !== null, 'the chosen center is named');
    await settle(fixture);
    expect(await field.getText()).toBe('C');
  });

  it('forwards the field’s state to the picker, which tm-form-field labels and marks', async () => {
    const { loader, host, fixture } = await startHost();
    const [formField] = await loader.getAllHarnesses(TmFormFieldHarness);
    const field = await (await within(loader, 'center')).getHarness(TmEntityFieldHarness);
    expect([await formField!.getLabelText(), await formField!.hasRequiredMarker()]).toEqual(['Center *', true]);

    // Leaving the empty required field marks it touched, and so invalid.
    const picker = await field.getPicker();
    await picker.focus();
    await picker.blur();
    await settle(fixture);
    expect(host.form.centerId().touched()).toBe(true);
    expect(await field.isInvalid()).toBe(true);
    expect(await formField!.getErrorText()).not.toBeNull();
  });

  it('holds the form invalid while its text names no row', async () => {
    const { loader, host, fixture } = await startHost(SYSTEM, { centerId: 1 });
    const field = await (await within(loader, 'center')).getHarness(TmEntityFieldHarness);
    await field.search('Nowhere');
    await (await field.getPicker()).blur();
    await settle(fixture);
    // The text clears the value, and the picker's parse error holds the field invalid beside the requirement.
    expect([host.model().centerId, host.form.centerId().errors().map((error) => error.kind)]).toEqual([null, ['parse', 'required']]);
    await frame(fixture);
  });

  it('disables the picker with its field', async () => {
    const { fixture } = await startHost(SYSTEM, { disable: true });
    const input = (fixture.nativeElement as HTMLElement).querySelector<HTMLInputElement>('[data-testid="center"] input')!;
    expect(input.getAttribute('aria-disabled') === 'true' || input.disabled).toBe(true);
  });

  it('chooses into its own value when unbound, labelled by its aria-label', async () => {
    const { loader, host, rig, fixture } = await startHost();
    rig.api.respond('currencies/query', {
      rows: {
        columns: ['Id', 'Code'].map((name) => ({ name, type: name === 'Id' ? 'Numeric' : 'String', nullable: false, path: [name], groupingKey: false })),
        rows: [[1, 'USD'], [2, 'SAR']],
      },
    });
    const field = await (await within(loader, 'currency')).getHarness(TmEntityFieldHarness);
    await field.pick('SAR');
    await settle(fixture);
    expect(host.currencyId()).toBe(2);
    const input = (fixture.nativeElement as HTMLElement).querySelector<HTMLInputElement>('[data-testid="currency"] input')!;
    expect(input.getAttribute('aria-label')).toBe('Currency');
  });

  it('draws a display name’s prefix and name each in its own isolate', async () => {
    const { loader, host, rig, fixture } = await startHost();
    rig.api.respond('customers/query', {
      rows: {
        columns: ['Id', 'Name', 'Code'].map((name) => ({ name, type: name === 'Id' ? 'Numeric' : 'String', nullable: false, path: [name], groupingKey: false })),
        rows: [[7, 'النقد', '1101']],
      },
    });
    const field = await (await within(loader, 'customer')).getHarness(TmEntityFieldHarness);
    const picker = await field.getPicker();
    await picker.open();
    await settle(fixture);
    const [row] = await picker.getOptions();
    expect(await row!.getLabelParts()).toEqual(['1101', 'النقد']);
    await row!.click();
    await settle(fixture);
    expect([host.customerId(), await field.getText()]).toEqual([7, '1101 - النقد']);
  });

  it('is read-only with the chosen row’s name where the user may not search the entity', async () => {
    const { loader, rig, fixture } = await startHost({}, { centerId: 3 });
    rig.names.remember('gl.Center', [{ id: 3, code: 'C' }]);
    await settle(fixture);
    const field = await (await within(loader, 'center')).getHarness(TmEntityFieldHarness);
    expect(await field.isReadonly()).toBe(true);
    expect(await (await field.getPicker()).hasMagnifier()).toBe(false);
    await frame(fixture);
  });
});

describe('TmEntityMultiField', () => {
  it('holds a required, empty list invalid, until a row is picked', async () => {
    const { loader, host, fixture } = await startHost();
    const field = await (await within(loader, 'centers')).getHarness(TmEntityMultiFieldHarness);
    expect(host.form.centerIds().invalid()).toBe(true);
    expect(host.form.centerIds().errors().map((error) => error.kind)).toEqual(['required']);

    await field.pick('A');
    await settle(fixture);
    expect(host.model().centerIds).toEqual([1]);
    expect(host.form.centerIds().invalid()).toBe(false);
    expect(await field.getChipLabels()).toEqual(['A']);

    await field.removeChip('A');
    await settle(fixture);
    expect(host.model().centerIds).toEqual([]);
    expect(host.form.centerIds().invalid()).toBe(true);
  });

  it('forwards its maxItems to the multi-picker, which refuses a pick past it', async () => {
    const { loader, host, fixture } = await startHost();
    host.maxItems.set(1);
    await settle(fixture);
    const field = await (await within(loader, 'centers')).getHarness(TmEntityMultiFieldHarness);
    await field.pick('A');
    await settle(fixture);
    await field.pick('B');
    await settle(fixture);
    expect(host.model().centerIds).toEqual([1]);
    expect(document.querySelector('.tm-entity-picker__notice')?.textContent.trim()).toBe('You can choose up to 1');
  });

  it('stays invalid when a form’s reset clears its errors', async () => {
    const { host, fixture } = await startHost();
    host.form().reset();
    await settle(fixture);
    expect(host.form.centerIds().invalid()).toBe(true);
  });

  it('is read-only with its rows’ names where the user may not search the entity', async () => {
    const { loader, rig, fixture } = await startHost({}, { centerIds: [1, 2] });
    rig.names.remember('gl.Center', [
      { id: 1, code: 'A' },
      { id: 2, code: 'B' },
    ]);
    await settle(fixture);
    const field = await (await within(loader, 'centers')).getHarness(TmEntityMultiFieldHarness);
    expect(await field.isReadonly()).toBe(true);
    expect(await field.getChipLabels()).toEqual(['A', 'B']);
    await frame(fixture);
  });
});

// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { Component, signal } from '@angular/core';
import { TestBed, type ComponentFixture } from '@angular/core/testing';
import { TestbedHarnessEnvironment } from '@angular/cdk/testing/testbed';
import { TmEntities, type TmColumnInput, type TmEntitiesChunk } from '@tellma/core';
import { TM_ENUM_PICKER_THRESHOLD, tmSelectPlan, type TmColumnPlan } from '@tellma/core/entity';
import { TmColumnFilterHarness, tmTestEntities } from '@tellma/core-testing';
import { TM_STRINGS_AR } from '@tellma/locale-ar/strings';

import { tmEntityRig, type TmEntityRig, type TmEntityRigOptions } from '../../src/entity/tm-entity.testing.util';
import { tmSettle } from '../../src/routes/tm-app.testing.util';
import { TmColumnFilter } from './tm-column-filter';

// The column filter control over the recorded entity model, patched with a
// date column, a long integer and two enums either side of the picker
// threshold: each kind's controls, the operator, Apply enabled only for a
// valid value, Enter (and when it is not the control's), Clear, numbers
// held exactly, the large enum's search in Arabic, and digits of another
// script.

/** The value of the patched enums' entry `index`. */
const VALUE = (index: number): string => `v${index}`;

/** The label of a large enum's entry `index` in Arabic: one names a city, the rest are numbered. */
const ARABIC = (index: number): string => (index === 37 ? 'الرياض' : `قيمة ${index}`);

/**
 * The recorded model with a date on customers and two enums: one just
 * below the picker threshold and one at it.
 *
 * @returns The patched chunk.
 */
function chunk(): TmEntitiesChunk {
  const patched = tmTestEntities().chunk;
  const enumOf = (name: string, size: number): unknown => ({
    name,
    values: Array.from({ length: size }, (_, index) => ({ value: VALUE(index), labelKey: `Acme_Enum_${index}`, labelFallback: `Value ${index}` })),
  });
  (patched.enums as unknown as unknown[]).push(enumOf('acme.Mid', TM_ENUM_PICKER_THRESHOLD - 1), enumOf('acme.Large', TM_ENUM_PICKER_THRESHOLD));
  const property = (name: string, type: string, extra: object = {}): unknown => ({
    name,
    json: name[0]!.toLowerCase() + name.slice(1),
    type,
    queryable: true,
    nullable: true,
    labelKey: `Acme_Customer_${name}`,
    labelFallback: name,
    excel: true,
    ownership: 'editable',
    ...extra,
  });
  const customer = patched.entities.find((entity) => entity.name === 'acme.Customer')!;
  (customer.properties as unknown as unknown[]).push(
    property('DueDate', 'date'),
    property('Serial', 'integer', { long: true }),
    property('Mid', 'enum', { enum: 'acme.Mid' }),
    property('Large', 'enum', { enum: 'acme.Large' }),
  );
  return patched;
}

/** A user who reads everything, in the patched model, with the large enum's Arabic labels. */
const SYSTEM: TmEntityRigOptions = {
  access: { isSystem: true },
  chunk: chunk(),
  labels: {
    en: { Acme_Customer_CustomerType: 'Type', Acme_CustomerType_Wholesale: 'Wholesale', IsActive: 'Active' },
    ar: Object.fromEntries(Array.from({ length: TM_ENUM_PICKER_THRESHOLD }, (_, index) => [`Acme_Enum_${index}`, ARABIC(index)])),
  },
};

/** One column's filter, and every value it applied. */
@Component({
  imports: [TmColumnFilter],
  template: `<tm-column-filter [column]="column()" [value]="value()" (applied)="applied.push($event)" />`,
})
class TmColumnFilterHost {
  /** The column filtered. */
  readonly column = signal<TmColumnPlan>(undefined!);
  /** The column's current `f.` value. */
  readonly value = signal<string | null>(null);
  /** The values applied, in order. */
  readonly applied: (string | null)[] = [];
}

/**
 * A customers column's filter, in the rig's app.
 *
 * @param key - The column input.
 * @param value - The current `f.` value; none when omitted.
 * @returns The fixture and the control's harness.
 */
async function filter(key: TmColumnInput<string>, value: string | null = null): Promise<{ fixture: ComponentFixture<TmColumnFilterHost>; harness: TmColumnFilterHarness }> {
  const plan = TestBed.runInInjectionContext(() => tmSelectPlan(TestBed.inject(TmEntities).get('acme.Customer'), [key]));
  const fixture = TestBed.createComponent(TmColumnFilterHost);
  fixture.componentInstance.column.set(plan.columns[0]!);
  fixture.componentInstance.value.set(value);
  await fixture.whenStable();
  return { fixture, harness: await TestbedHarnessEnvironment.loader(fixture).getHarness(TmColumnFilterHarness) };
}

describe('TmColumnFilter', () => {
  describe('text', () => {
    it('applies a value with the chosen operator, by Apply and by Enter', async () => {
      await tmEntityRig(SYSTEM);
      const { fixture, harness } = await filter('Code');
      expect(await harness.getOperatorText()).toBe('Contains');
      expect(await harness.isApplyEnabled()).toBe(false);

      await (await harness.getTextInput())!.setValue("O'Neil, Jr");
      expect(await harness.isApplyEnabled()).toBe(true);
      await harness.apply();
      await harness.selectOperator('Starts with');
      await harness.pressEnter();
      expect(fixture.componentInstance.applied).toEqual(["contains:O'Neil, Jr", "starts-with:O'Neil, Jr"]);
    });

    it('starts from the column’s value, and emits null on Clear', async () => {
      await tmEntityRig(SYSTEM);
      const { fixture, harness } = await filter('Code', 'ne:x');
      expect(await harness.getOperatorText()).toBe('Does not equal');
      expect(await (await harness.getTextInput())!.getValue()).toBe('x');
      await harness.clear();
      expect(fixture.componentInstance.applied).toEqual([null]);
    });

    it('starts empty from a value of another kind', async () => {
      await tmEntityRig(SYSTEM);
      const { harness } = await filter('Code', 'between:1,2');
      expect(await harness.getOperatorText()).toBe('Contains');
      expect(await (await harness.getTextInput())!.getValue()).toBe('');
      expect(await harness.isApplyEnabled()).toBe(false);
    });
  });

  describe('numbers', () => {
    it('takes a range as a labelled pair, in order', async () => {
      await tmEntityRig(SYSTEM);
      const { fixture, harness } = await filter('CreditLimit');
      expect(await harness.getOperatorText()).toBe('Between');
      expect(await harness.getGroupLabel()).not.toBeNull();
      const [from, to] = await harness.getNumberInputs();
      await from!.setText('500');
      await to!.setText('100');
      await from!.blur();
      await to!.blur();
      expect(await harness.isApplyEnabled()).toBe(false);
      await to!.setText('1500.25');
      await to!.blur();
      expect(await harness.isApplyEnabled()).toBe(true);
      await harness.pressEnter();
      expect(fixture.componentInstance.applied).toEqual(['between:500,1500.25']);
    });

    it('applies a long read back from its value exactly, past the digits a number holds', async () => {
      await tmEntityRig(SYSTEM);
      const { fixture, harness } = await filter('Serial', 'eq:1234567890123456');
      expect(await harness.getOperatorText()).toBe('Equals');
      expect(await harness.isApplyEnabled()).toBe(true);
      await harness.apply();
      expect(fixture.componentInstance.applied).toEqual(['eq:1234567890123456']);
    });

    it('applies a decimal read back from its value exactly', async () => {
      await tmEntityRig(SYSTEM);
      const { fixture, harness } = await filter('CreditLimit', 'ge:12345678901234567890.5');
      await harness.apply();
      expect(fixture.componentInstance.applied).toEqual(['ge:12345678901234567890.5']);
    });

    it('refuses typed digits it cannot hold exactly, rather than rounding them', async () => {
      await tmEntityRig(SYSTEM);
      const { harness } = await filter('Serial');
      await harness.selectOperator('Equals');
      const [input] = await harness.getNumberInputs();
      await input!.setText('12345678901234567');
      await input!.blur();
      expect(await harness.isApplyEnabled()).toBe(false);
      expect(await input!.isInvalid()).toBe(true);
      await input!.setText('123456789012345');
      expect(await harness.isApplyEnabled()).toBe(true);
    });

    it('reads the locale’s own digits and writes Latin ones', async () => {
      await tmEntityRig({ ...SYSTEM, language: 'ar', tmStrings: { ar: TM_STRINGS_AR } });
      const { fixture, harness } = await filter('CreditLimit');
      await harness.selectOperator('أكبر من');
      const [input] = await harness.getNumberInputs();
      await input!.setText(String.fromCharCode(0x661, 0x662, 0x660));
      await input!.blur();
      await harness.apply();
      expect(fixture.componentInstance.applied).toEqual(['gt:120']);
    });
  });

  describe('days', () => {
    it('takes a range, and a single day for before and after', async () => {
      await tmEntityRig(SYSTEM);
      const { fixture, harness } = await filter('DueDate', 'between:2026-01-01,2026-01-31');
      expect(await harness.getDateRange()).not.toBeNull();
      expect(await harness.isApplyEnabled()).toBe(true);
      await harness.apply();

      await harness.selectOperator('Before');
      expect(await harness.getDateRange()).toBeNull();
      expect(await harness.getDatePicker()).not.toBeNull();
      expect(await harness.isApplyEnabled()).toBe(false);
      expect(fixture.componentInstance.applied).toEqual(['between:2026-01-01,2026-01-31']);
    });

    it('filters an instant’s days with the same controls', async () => {
      await tmEntityRig(SYSTEM);
      const { fixture, harness } = await filter('CreatedAt', 'after:2026-03-01');
      expect(await harness.getOperatorText()).toBe('After');
      expect(await harness.getDatePicker()).not.toBeNull();
      await harness.apply();
      expect(fixture.componentInstance.applied).toEqual(['after:2026-03-01']);
    });
  });

  describe('enums', () => {
    it('lists a small enum as a labelled group of checkboxes, with no operator select', async () => {
      await tmEntityRig(SYSTEM);
      const { fixture, harness } = await filter('CustomerType');
      expect(await harness.getOperator()).toBeNull();
      expect(await harness.getGroupLabel()).toBe('Type');
      const boxes = await harness.getCheckboxes();
      expect(await Promise.all(boxes.map((box) => box.getLabelText()))).toEqual(['Retail', 'Wholesale']);
      await boxes[1]!.toggle();
      await boxes[0]!.toggle();
      await harness.apply();
      // The enum's order, not the order of the clicks.
      expect(fixture.componentInstance.applied).toEqual(['in:Retail,wholesale-trade']);
    });

    it('offers an enum below the threshold in a multi-select', async () => {
      await tmEntityRig(SYSTEM);
      const { fixture, harness } = await filter('Mid');
      expect(await harness.getEnumPicker()).toBeNull();
      const select = (await harness.getMultiSelect())!;
      await select.open();
      expect(await select.getOptions()).toHaveLength(TM_ENUM_PICKER_THRESHOLD - 1);
      await select.toggle('Value 3');
      await select.close();
      await harness.apply();
      expect(fixture.componentInstance.applied).toEqual(['in:v3']);
    });

    it('searches an enum at the threshold by its labels in the active language', async () => {
      await tmEntityRig({ ...SYSTEM, language: 'ar', tmStrings: { ar: TM_STRINGS_AR } });
      const { fixture, harness } = await filter('Large');
      expect(await harness.getMultiSelect()).toBeNull();
      const picker = (await harness.getEnumPicker())!;
      await picker.search('الرياض');
      expect(await (await picker.getPicker()).getOptionLabels()).toEqual(['الرياض']);
      await picker.pick('الرياض');
      expect(await picker.getChipLabels()).toEqual(['الرياض']);
      await harness.apply();
      expect(fixture.componentInstance.applied).toEqual(['in:v37']);
    });

    it('browses the first twenty of a large enum', async () => {
      await tmEntityRig(SYSTEM);
      const { harness } = await filter('Large');
      const picker = (await (await harness.getEnumPicker())!.getPicker())!;
      await picker.open();
      expect((await picker.getOptionLabels()).slice(0, 20)).toEqual(Array.from({ length: 20 }, (_, index) => `Value ${index}`));
    });

    it('leaves Enter in the picker to the picker', async () => {
      await tmEntityRig(SYSTEM);
      const { fixture, harness } = await filter('Large', 'in:v1');
      expect(await harness.isApplyEnabled()).toBe(true);
      const picker = await (await harness.getEnumPicker())!.getPicker();
      await picker.typeQuery('Value 2');
      const input = (fixture.nativeElement as HTMLElement).querySelector<HTMLInputElement>('tm-entity-multi-picker input')!;
      input.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter', bubbles: true, cancelable: true }));
      await fixture.whenStable();
      expect(fixture.componentInstance.applied).toEqual([]);
    });

    it('drops a value the enum no longer holds', async () => {
      await tmEntityRig(SYSTEM);
      const { fixture, harness } = await filter('CustomerType', 'in:Retail,Retired');
      const boxes = await harness.getCheckboxes();
      expect(await Promise.all(boxes.map((box) => box.isChecked()))).toEqual([true, false]);
      await harness.apply();
      expect(fixture.componentInstance.applied).toEqual(['in:Retail']);
    });
  });

  describe('booleans and navigations', () => {
    it('applies True or False', async () => {
      await tmEntityRig(SYSTEM);
      const { fixture, harness } = await filter('IsActive');
      expect(await harness.getOperator()).toBeNull();
      const radios = (await harness.getRadioGroup())!;
      expect(await radios.getAriaLabel()).toBe('Active');
      expect(await harness.isApplyEnabled()).toBe(false);
      await radios.check('False');
      await harness.apply();
      expect(fixture.componentInstance.applied).toEqual(['is:false']);
    });

    it('picks related rows in an entity field, and applies their ids', async () => {
      const rig: TmEntityRig = await tmEntityRig(SYSTEM);
      rig.api.respond('currencies/query', {
        rows: {
          columns: ['Id', 'Code'].map((name) => ({ name, type: name === 'Id' ? 'Numeric' : 'String', nullable: false, path: [name], groupingKey: false })),
          rows: [[1, 'USD'], [2, 'SAR']],
        },
      });
      const { fixture, harness } = await filter('Currency');
      expect(await harness.getOperator()).toBeNull();
      expect(await harness.isApplyEnabled()).toBe(false);
      const field = (await harness.getEntityField())!;
      await field.pick('SAR');
      await tmSettle();
      expect(await field.getChipLabels()).toEqual(['SAR']);
      await harness.apply();
      expect(fixture.componentInstance.applied).toEqual(['in:2']);
    });
  });

  describe('input methods', () => {
    it('leaves the Enter that ends a composition to the input method', async () => {
      await tmEntityRig(SYSTEM);
      const { fixture, harness } = await filter('Code', 'contains:jed');
      const input = (fixture.nativeElement as HTMLElement).querySelector<HTMLInputElement>('input.tm-column-filter__text')!;
      const composing = new KeyboardEvent('keydown', { key: 'Enter', bubbles: true, cancelable: true, isComposing: true });
      input.dispatchEvent(composing);
      input.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter', keyCode: 229, bubbles: true, cancelable: true }));
      await fixture.whenStable();
      expect(composing.defaultPrevented).toBe(false);
      expect(fixture.componentInstance.applied).toEqual([]);
      await harness.pressEnter();
      expect(fixture.componentInstance.applied).toEqual(['contains:jed']);
    });
  });

  describe('language', () => {
    it('names its operators and buttons in the active language', async () => {
      await tmEntityRig({ ...SYSTEM, language: 'ar', tmStrings: { ar: TM_STRINGS_AR } });
      const { fixture, harness } = await filter('Code');
      expect(await harness.getOperatorText()).toBe('يحتوي على');
      const element: HTMLElement = fixture.nativeElement;
      expect(element.querySelector('.tm-column-filter__operator .tm-select__trigger')?.getAttribute('aria-label')).toBe('المعامل');
      expect([...element.querySelectorAll('.tm-column-filter__actions button')].map((button) => button.textContent?.trim())).toEqual(['مسح', 'تطبيق']);
    });
  });
});

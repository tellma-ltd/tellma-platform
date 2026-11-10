// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { TestbedHarnessEnvironment } from '@angular/cdk/testing/testbed';
import { TestKey } from '@angular/cdk/testing';
import { form, FormField, required } from '@angular/forms/signals';

import { provideTellmaUi, TM_ACTIVE_LOCALE } from '@tellma/core-ui';
import { TmFormField } from '@tellma/core-ui/form-field';
import { TmMultiSelectHarness } from '@tellma/core-ui-testing';

import { TmMultiSelect } from './tm-multi-select';
import { TmOption } from './tm-option';
import { TmOptionGroup } from './tm-option-group';

/** A center type, as the filter that motivated the control lists them. */
interface CenterType {
  /** The stable id the select keys on. */
  readonly id: number;
  /** The visible label. */
  readonly name: string;
}

const TYPES: readonly CenterType[] = [
  { id: 1, name: 'Service' },
  { id: 2, name: 'Operation' },
  { id: 3, name: 'Sale' },
  { id: 4, name: 'Cost' },
];

/** Creates the host, lets the first render settle and returns its harness. */
async function setup<T>(component: new () => T, providers: unknown[] = []) {
  TestBed.configureTestingModule({ providers: [provideTellmaUi(), ...(providers as [])] });
  const fixture = TestBed.createComponent(component);
  await fixture.whenStable();
  await new Promise((resolve) => setTimeout(resolve, 0));
  await fixture.whenStable();
  const loader = TestbedHarnessEnvironment.loader(fixture);
  return { fixture, select: await loader.getHarness(TmMultiSelectHarness) };
}

/** Lets a render and the overlay's macrotask re-measure land. */
async function settle(fixture: { whenStable(): Promise<unknown> }): Promise<void> {
  await fixture.whenStable();
  await new Promise((resolve) => setTimeout(resolve, 0));
  await fixture.whenStable();
}

/** The open panel's active option text, read from the DOM. */
function activeText(): string | undefined {
  return document
    .querySelector('.tm-option__row[data-active="true"] .tm-option__content')
    ?.textContent?.trim();
}

describe('tm-multi-select', () => {
  describe('toggling', () => {
    @Component({
      imports: [TmMultiSelect, TmOption],
      template: `
        <tm-multi-select
          [(value)]="value"
          placeholder="Pick types"
          (selectionChange)="emissions.push($event)"
          (closed)="closes = closes + 1"
        >
          @for (type of types; track type.id) {
            <tm-option [value]="type.id" [label]="type.name">{{ type.name }}</tm-option>
          }
          <tm-option [value]="99" label="Retired" disabled>Retired</tm-option>
        </tm-multi-select>
      `,
    })
    class Host {
      readonly value = signal<readonly number[]>([]);
      readonly types = TYPES;
      readonly emissions: (readonly number[])[] = [];
      closes = 0;
    }

    it('the value follows option order, never click order', async () => {
      const { fixture, select } = await setup(Host);
      const host = fixture.componentInstance;

      await select.toggle('Cost');
      await select.toggle('Operation');
      await select.toggle('Service');
      await fixture.whenStable();

      expect(host.value()).toEqual([1, 2, 4]);
      expect(host.emissions).toEqual([[4], [2, 4], [1, 2, 4]]);
    });

    it('a click toggles and keeps the panel open; a second click deselects', async () => {
      const { fixture, select } = await setup(Host);
      const host = fixture.componentInstance;

      await select.toggle('Sale');
      await fixture.whenStable();
      expect(await select.isOpen()).toBe(true);
      expect(await select.isOptionSelected('Sale')).toBe(true);
      expect(host.value()).toEqual([3]);

      await select.toggle('Sale');
      await fixture.whenStable();
      expect(await select.isOpen()).toBe(true);
      expect(await select.isOptionSelected('Sale')).toBe(false);
      expect(host.value()).toEqual([]);
      expect(host.closes).toBe(0);
    });

    it('Space and Enter toggle the active option and keep the panel open', async () => {
      const { fixture, select } = await setup(Host);
      const host = fixture.componentInstance;

      await select.sendTriggerKeys(TestKey.DOWN_ARROW); // opens on Service
      await settle(fixture);
      expect(activeText()).toBe('Service');
      await select.sendTriggerKeys(' ');
      await fixture.whenStable();
      expect(host.value()).toEqual([1]);

      await select.sendTriggerKeys(TestKey.DOWN_ARROW);
      await select.sendTriggerKeys(TestKey.ENTER);
      await fixture.whenStable();
      expect(host.value()).toEqual([1, 2]);
      expect(await select.isOpen()).toBe(true);

      await select.sendTriggerKeys(TestKey.ENTER); // Operation again: off
      await fixture.whenStable();
      expect(host.value()).toEqual([1]);
      expect(await select.isOpen()).toBe(true);
    });

    it('keys pressed within one render all reach the listbox, in order', async () => {
      const { fixture, select } = await setup(Host);
      const host = fixture.componentInstance;
      host.value.set([3]); // Sale: the panel opens with Sale active
      await select.sendTriggerKeys(TestKey.DOWN_ARROW);
      await settle(fixture);
      expect(activeText()).toBe('Sale');

      // Home then Space with no render in between, as a fast typist or a
      // loaded machine delivers them: Home must still move to Service first.
      const trigger = fixture.nativeElement.querySelector('.tm-select__trigger') as HTMLElement;
      const press = (key: string) =>
        trigger.dispatchEvent(new KeyboardEvent('keydown', { key, bubbles: true }));
      press('Home');
      await Promise.resolve();
      press(' ');
      await settle(fixture);
      expect(host.value()).toEqual([1, 3]);
      expect(activeText()).toBe('Service');
    });

    it('Escape closes the panel and keeps the value', async () => {
      const { fixture, select } = await setup(Host);
      const host = fixture.componentInstance;
      await select.toggle('Service');
      await select.sendTriggerKeys(TestKey.ESCAPE);
      await fixture.whenStable();
      expect(await select.isOpen()).toBe(false);
      expect(host.value()).toEqual([1]);
    });

    it('focus leaving the trigger closes the panel', async () => {
      const { fixture, select } = await setup(Host);
      await select.open();
      await settle(fixture);
      const outside = document.createElement('button');
      document.body.appendChild(outside);
      try {
        outside.focus(); // Tab or an outside press both move focus away
        await settle(fixture);
        expect(await select.isOpen()).toBe(false);
      } finally {
        outside.remove();
      }
    });

    it('a press inside the panel never takes focus from the trigger', async () => {
      const { fixture, select } = await setup(Host);
      await select.open();
      await settle(fixture);
      const row = document.querySelectorAll('.tm-option__row')[1] as HTMLElement;
      const press = new MouseEvent('mousedown', { bubbles: true, cancelable: true });
      row.dispatchEvent(press);
      expect(press.defaultPrevented).toBe(true);
    });

    it('a disabled option neither toggles nor emits', async () => {
      const { fixture, select } = await setup(Host);
      const host = fixture.componentInstance;
      await select.toggle('Retired');
      await fixture.whenStable();
      expect(host.value()).toEqual([]);
      expect(host.emissions).toEqual([]);
      expect(await select.isOpen()).toBe(true);
    });

    it('navigation without a toggle writes nothing', async () => {
      const { fixture, select } = await setup(Host);
      const host = fixture.componentInstance;
      await select.sendTriggerKeys(TestKey.DOWN_ARROW);
      await settle(fixture);
      await select.sendTriggerKeys(TestKey.DOWN_ARROW, TestKey.END, TestKey.HOME);
      await fixture.whenStable();
      expect(host.emissions).toEqual([]);
      expect(activeText()).toBe('Service');
    });

    it('typing on the closed trigger opens at the first match without changing the value', async () => {
      const { fixture, select } = await setup(Host);
      const host = fixture.componentInstance;
      await select.sendTriggerKeys('o'); // Operation
      await settle(fixture);
      await new Promise((resolve) => requestAnimationFrame(() => resolve(null)));
      await fixture.whenStable();
      expect(await select.isOpen()).toBe(true);
      expect(activeText()).toBe('Operation');
      expect(host.value()).toEqual([]);
    });
  });

  describe('the trigger', () => {
    @Component({
      imports: [TmMultiSelect, TmOption],
      template: `
        <tm-multi-select [(value)]="value" aria-label="Types">
          @for (type of types; track type.id) {
            <tm-option [value]="type.id" [label]="type.name">{{ type.name }}</tm-option>
          }
        </tm-multi-select>
      `,
    })
    class Host {
      readonly value = signal<readonly number[]>([]);
      readonly types = TYPES;
    }

    it('shows the localized placeholder when empty, then the labels as one short list', async () => {
      const { fixture, select } = await setup(Host);
      expect(await select.getValueText()).toBe('Select options');
      expect(await select.isPlaceholderShown()).toBe(true);

      fixture.componentInstance.value.set([3, 1]);
      await fixture.whenStable();
      // Option order, joined as a short unit list in English.
      expect(await select.getValueText()).toBe('Service, Sale');
      expect(await select.isPlaceholderShown()).toBe(false);
    });

    it('keeps the full list in the trigger text, ellipsizing only its rendering', async () => {
      const { fixture } = await setup(Host);
      fixture.componentInstance.value.set([1, 2, 3, 4]);
      await fixture.whenStable();
      const value = fixture.nativeElement.querySelector('.tm-select__value') as HTMLElement;
      expect(value.textContent?.trim()).toBe('Service, Operation, Sale, Cost');
      const style = getComputedStyle(value);
      expect(style.whiteSpace).toBe('nowrap');
      expect(style.textOverflow).toBe('ellipsis');
    });

    it('joins the labels in the active locale', async () => {
      const { fixture, select } = await setup(Host, [
        { provide: TM_ACTIVE_LOCALE, useValue: signal('ar').asReadonly() },
      ]);
      fixture.componentInstance.value.set([1, 2]);
      await fixture.whenStable();
      const expected = new Intl.ListFormat('ar', { type: 'unit', style: 'short' }).format([
        'Service',
        'Operation',
      ]);
      expect(await select.getValueText()).toBe(expected);
    });

    it('keeps one line at the field height however many labels are selected', async () => {
      const { fixture } = await setup(Host);
      const trigger = fixture.nativeElement.querySelector('.tm-select__trigger') as HTMLElement;
      const host = fixture.nativeElement.querySelector('tm-multi-select') as HTMLElement;
      host.style.inlineSize = '120px';
      const empty = trigger.getBoundingClientRect().height;
      fixture.componentInstance.value.set([1, 2, 3, 4]);
      await fixture.whenStable();
      expect(trigger.getBoundingClientRect().height).toBe(empty);
    });
  });

  describe('values no option matches', () => {
    @Component({
      imports: [TmMultiSelect, TmOption],
      template: `
        <tm-multi-select [(value)]="value" aria-label="Types" [displayWith]="displayWith()">
          @for (type of types(); track type.id) {
            <tm-option [value]="type.id" [label]="type.name">{{ type.name }}</tm-option>
          }
        </tm-multi-select>
      `,
    })
    class Host {
      readonly value = signal<readonly number[]>([7, 2]);
      readonly types = signal<readonly CenterType[]>(TYPES);
      readonly displayWith = signal<((id: number) => string) | undefined>(undefined);
    }

    it('are kept, after the matched values, through toggles and option turnover', async () => {
      const warn = vi.spyOn(console, 'warn').mockImplementation(() => undefined);
      try {
        const { fixture, select } = await setup(Host);
        const host = fixture.componentInstance;

        await select.toggle('Service');
        await fixture.whenStable();
        expect(host.value()).toEqual([1, 2, 7]);

        // The list narrows while open: Operation disappears and is kept too.
        host.types.set(TYPES.filter((type) => type.id !== 2));
        await settle(fixture);
        expect(host.value()).toEqual([1, 2, 7]);
        await select.toggle('Sale');
        await fixture.whenStable();
        expect(host.value()).toEqual([1, 3, 2, 7]);

        // Ctrl+A selects every option, then clears every option — never the
        // values no option shows.
        const trigger = fixture.nativeElement.querySelector('.tm-select__trigger') as HTMLElement;
        const selectAll = () =>
          trigger.dispatchEvent(
            new KeyboardEvent('keydown', { key: 'a', ctrlKey: true, bubbles: true }),
          );
        selectAll();
        await fixture.whenStable();
        expect(host.value()).toEqual([1, 3, 4, 2, 7]);
        selectAll();
        await fixture.whenStable();
        expect(host.value()).toEqual([2, 7]);
      } finally {
        warn.mockRestore();
      }
    });

    it('show as String(value) with one development warning, or through displayWith', async () => {
      const warn = vi.spyOn(console, 'warn').mockImplementation(() => undefined);
      try {
        const { fixture, select } = await setup(Host);
        expect(await select.getValueText()).toBe('Operation, 7');
        fixture.componentInstance.value.set([7, 2, 8]);
        await fixture.whenStable();
        expect(await select.getValueText()).toBe('Operation, 7, 8');
        const warnings = warn.mock.calls.map((call) => String(call[0]));
        expect(warnings.filter((text) => text.includes('"7"'))).toHaveLength(1);
        expect(warnings.filter((text) => text.includes('"8"'))).toHaveLength(1);

        fixture.componentInstance.displayWith.set((id) => `#${id}`);
        await fixture.whenStable();
        expect(await select.getValueText()).toBe('#2, #7, #8');
      } finally {
        warn.mockRestore();
      }
    });
  });

  describe('options without a label', () => {
    @Component({
      imports: [TmMultiSelect, TmOption],
      template: `
        <tm-multi-select [(value)]="value" aria-label="Types">
          <tm-option value="Service">Service</tm-option>
          <tm-option value="Operation" label="Operation">Operation</tm-option>
          <tm-option value="Sale">Sale</tm-option>
        </tm-multi-select>
      `,
    })
    class Host {
      readonly value = signal<readonly string[]>(['Sale', 'Operation']);
    }

    it('a prepopulated value never shows the placeholder before the panel first renders', async () => {
      const warn = vi.spyOn(console, 'warn').mockImplementation(() => undefined);
      try {
        const { select } = await setup(Host);
        // The panel never opened: the label-less option falls back to its value.
        expect(await select.isPlaceholderShown()).toBe(false);
        expect(await select.getValueText()).toBe('Operation, Sale');
        const warnings = warn.mock.calls.map((call) => String(call[0]));
        expect(warnings.filter((text) => text.includes('"Sale"'))).toHaveLength(1);
        expect(warnings.filter((text) => text.includes('"Operation"'))).toHaveLength(0);
      } finally {
        warn.mockRestore();
      }
    });
  });

  describe('option groups', () => {
    @Component({
      imports: [TmMultiSelect, TmOption, TmOptionGroup],
      template: `
        <tm-multi-select [(value)]="value" aria-label="Types">
          <tm-option-group label="Operating">
            <tm-option [value]="1" label="Service">Service</tm-option>
            <tm-option [value]="2" label="Operation">Operation</tm-option>
          </tm-option-group>
          <tm-option [value]="3" label="Sale">Sale</tm-option>
          <tm-option-group label="Overhead">
            <tm-option [value]="4" label="Cost">Cost</tm-option>
          </tm-option-group>
        </tm-multi-select>
      `,
    })
    class Host {
      readonly value = signal<readonly number[]>([]);
    }

    it('render as named groups between the ungrouped options', async () => {
      const { select } = await setup(Host);
      const groups = await select.getGroups();
      expect(await Promise.all(groups.map((group) => group.getLabel()))).toEqual([
        'Operating',
        'Overhead',
      ]);
      const first = await groups[0].getOptions();
      expect(await Promise.all(first.map((option) => option.getText()))).toEqual([
        'Service',
        'Operation',
      ]);
      const listbox = document.querySelector('[role="listbox"]')!;
      expect(listbox.getAttribute('aria-multiselectable')).toBe('true');
      for (const group of listbox.querySelectorAll('[role="group"]')) {
        const heading = document.getElementById(group.getAttribute('aria-labelledby')!);
        expect(group.contains(heading)).toBe(true);
      }
    });

    it('navigation steps over the headings', async () => {
      const { fixture, select } = await setup(Host);
      await select.sendTriggerKeys(TestKey.DOWN_ARROW);
      await settle(fixture);
      const visited = [activeText()];
      for (let i = 0; i < 3; i++) {
        await select.sendTriggerKeys(TestKey.DOWN_ARROW);
        await fixture.whenStable();
        visited.push(activeText());
      }
      expect(visited).toEqual(['Service', 'Operation', 'Sale', 'Cost']);
    });

    it('the value follows option order across groups', async () => {
      const { fixture, select } = await setup(Host);
      await select.toggle('Cost');
      await select.toggle('Sale');
      await select.toggle('Service');
      await fixture.whenStable();
      expect(fixture.componentInstance.value()).toEqual([1, 3, 4]);
    });
  });

  describe('Signal Forms binding', () => {
    @Component({
      imports: [TmMultiSelect, TmOption, TmFormField, FormField],
      template: `
        <tm-form-field label="Center types" hint="Leave empty for all">
          <tm-multi-select [formField]="f.types">
            @for (type of types; track type.id) {
              <tm-option [value]="type.id" [label]="type.name">{{ type.name }}</tm-option>
            }
          </tm-multi-select>
        </tm-form-field>
      `,
    })
    class Host {
      readonly model = signal<{ types: readonly number[] }>({ types: [] });
      readonly f = form(this.model, (p) => {
        required(p.types);
      });
      readonly types = TYPES;
    }

    it('toggles flow into the form model; the field labels the trigger', async () => {
      const { fixture, select } = await setup(Host);
      await select.toggle('Sale');
      await select.toggle('Service');
      await fixture.whenStable();
      expect(fixture.componentInstance.model().types).toEqual([1, 3]);

      const trigger = fixture.nativeElement.querySelector('.tm-select__trigger') as HTMLElement;
      const labelledBy = trigger.getAttribute('aria-labelledby')!;
      expect(document.getElementById(labelledBy)?.textContent).toContain('Center types');
      expect(trigger.getAttribute('aria-required')).toBe('true');
    });
  });

  describe('readonly and disabled', () => {
    @Component({
      imports: [TmMultiSelect, TmOption],
      template: `
        <tm-multi-select
          [(value)]="value"
          aria-label="Types"
          [readonly]="readonly()"
          [disabled]="disabled()"
        >
          @for (type of types; track type.id) {
            <tm-option [value]="type.id" [label]="type.name">{{ type.name }}</tm-option>
          }
        </tm-multi-select>
      `,
    })
    class Host {
      readonly value = signal<readonly number[]>([2]);
      readonly readonly = signal(false);
      readonly disabled = signal(false);
      readonly types = TYPES;
    }

    it('a readonly multi-select never opens, by click, key or typing, and keeps its value', async () => {
      const { fixture, select } = await setup(Host);
      fixture.componentInstance.readonly.set(true);
      await fixture.whenStable();
      await select.sendTriggerKeys('s');
      await fixture.whenStable();
      expect(await select.isOpen()).toBe(false);

      const trigger = fixture.nativeElement.querySelector('.tm-select__trigger') as HTMLElement;
      expect(trigger.getAttribute('aria-readonly')).toBe('true');
      trigger.focus();
      trigger.click();
      await settle(fixture);
      expect(await select.isOpen()).toBe(false);
      expect(document.querySelector('.tm-select__panel')).toBeNull();
      await select.sendTriggerKeys(TestKey.DOWN_ARROW);
      await settle(fixture);
      expect(await select.isOpen()).toBe(false);
      expect(fixture.componentInstance.value()).toEqual([2]);
    });

    it('a disabled multi-select does not open', async () => {
      const { fixture, select } = await setup(Host);
      fixture.componentInstance.disabled.set(true);
      await fixture.whenStable();
      expect(await select.isDisabled()).toBe(true);
      (fixture.nativeElement.querySelector('.tm-select__trigger') as HTMLElement).click();
      await fixture.whenStable();
      expect(await select.isOpen()).toBe(false);
    });
  });
});

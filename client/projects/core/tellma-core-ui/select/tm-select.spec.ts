// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { TestbedHarnessEnvironment } from '@angular/cdk/testing/testbed';
import { TestKey } from '@angular/cdk/testing';
import { disabled, form, FormField, required } from '@angular/forms/signals';

import { provideTellmaUi } from '@tellma/core-ui';
import { TmFormField } from '@tellma/core-ui/form-field';
import { TmSelectHarness } from '@tellma/core-ui-testing';

import { TmOption } from './tm-option';
import { TmSelect } from './tm-select';

interface Country {
  readonly id: number;
  readonly name: string;
}

const COUNTRIES: readonly Country[] = [
  { id: 1, name: 'Saudi Arabia' },
  { id: 2, name: 'United Arab Emirates' },
  { id: 3, name: 'Ethiopia' },
  { id: 4, name: 'Jordan' },
];

async function setup<T>(component: new () => T) {
  TestBed.configureTestingModule({ providers: [provideTellmaUi()] });
  const fixture = TestBed.createComponent(component);
  await fixture.whenStable();
  await new Promise((resolve) => setTimeout(resolve, 0));
  await fixture.whenStable();
  const loader = TestbedHarnessEnvironment.loader(fixture);
  return { fixture, loader, select: await loader.getHarness(TmSelectHarness) };
}

describe('tm-select (§3.4)', () => {
  // ---- The risk-retirement specs FIRST (DoD 7): value integrity against
  // aria's unmatched-value auto-prune, written before anything else.
  describe('prepopulated/async value integrity (DoD 7)', () => {
    @Component({
      imports: [TmSelect, TmOption],
      template: `
        <tm-select
          [(value)]="value"
          [valueKey]="byId"
          [displayWith]="displayName"
          data-testid="async-select"
        >
          @for (country of options(); track country.id) {
            <tm-option [value]="country">{{ country.name }}</tm-option>
          }
        </tm-select>
      `,
    })
    class AsyncHost {
      // Prepopulated with a FRESH object (an edit screen: same id, different
      // instance) BEFORE any option exists.
      readonly value = signal<Country | undefined>({ id: 3, name: 'Ethiopia' });
      readonly options = signal<readonly Country[]>([]);
      readonly byId = (c: Country) => c.id;
      readonly displayName = (c: Country) => c.name;
    }

    it('a prepopulated value SURVIVES until its option renders; displayWith labels it', async () => {
      const { fixture, select } = await setup(AsyncHost);
      const host = fixture.componentInstance;

      // No options yet: aria's prune must not wipe the model.
      expect(host.value()).toEqual({ id: 3, name: 'Ethiopia' });
      expect(await select.getTriggerText()).toBe('Ethiopia'); // displayWith, no option needed

      // Options arrive (fresh instances — referentially different).
      host.options.set(COUNTRIES);
      await fixture.whenStable();

      // The value survived and the matching option is selected via valueKey.
      expect(host.value()).toEqual({ id: 3, name: 'Ethiopia' });
      const options = await select.getOptions();
      expect(await options[2].isSelected()).toBe(true);
    });

    it('async option turnover never commits, wipes the value, or closes the panel', async () => {
      const { fixture, select } = await setup(AsyncHost);
      const host = fixture.componentInstance;
      host.options.set(COUNTRIES);
      await fixture.whenStable();

      await select.open();
      expect(await select.isOpen()).toBe(true);

      // Turnover: the selected option DISAPPEARS (server refresh narrowed
      // the list) — aria prunes its listbox value, which must NOT read as a
      // user deselection.
      host.options.set(COUNTRIES.filter((c) => c.id !== 3));
      await fixture.whenStable();

      expect(host.value()).toEqual({ id: 3, name: 'Ethiopia' }); // model intact
      expect(await select.isOpen()).toBe(true); // panel not slammed shut

      // The option returns -> selection re-asserts (the one-directional bridge).
      host.options.set(COUNTRIES);
      await fixture.whenStable();
      const options = await select.getOptions();
      expect(await options[2].isSelected()).toBe(true);
    });
  });

  describe('open/close/selection', () => {
    @Component({
      imports: [TmSelect, TmOption],
      template: `
        <tm-select [(value)]="value" placeholder="Pick a country">
          @for (country of countries; track country.id) {
            <tm-option [value]="country.id" [label]="country.name">{{ country.name }}</tm-option>
          }
        </tm-select>
      `,
    })
    class Host {
      readonly value = signal<number | undefined>(undefined);
      readonly countries = COUNTRIES;
    }

    it('shows the placeholder when empty, opens on click, commits on option click', async () => {
      const { fixture, select } = await setup(Host);
      const host = fixture.componentInstance;

      expect(await select.getTriggerText()).toBe('Pick a country');
      expect(await select.isPlaceholderShown()).toBe(true);

      await select.selectOption('Ethiopia');
      await fixture.whenStable();

      expect(host.value()).toBe(3);
      expect(await select.isOpen()).toBe(false);
      expect(await select.getTriggerText()).toBe('Ethiopia'); // projected-option label
      expect(await select.isPlaceholderShown()).toBe(false);
    });

    it('same-value reselection still closes the panel (activation, not valueChange)', async () => {
      const { fixture, select } = await setup(Host);
      fixture.componentInstance.value.set(3);
      await fixture.whenStable();

      await select.selectOption('Ethiopia'); // already selected
      await fixture.whenStable();
      expect(await select.isOpen()).toBe(false);
      expect(fixture.componentInstance.value()).toBe(3);
    });

    it('keyboard: arrows move the active option, Enter commits, focus stays on the trigger', async () => {
      const { fixture, select } = await setup(Host);
      const host = fixture.componentInstance;

      await select.sendTriggerKeys(TestKey.DOWN_ARROW); // opens
      expect(await select.isOpen()).toBe(true);
      await select.sendTriggerKeys(TestKey.DOWN_ARROW, TestKey.DOWN_ARROW);
      await select.sendTriggerKeys(TestKey.ENTER);
      await fixture.whenStable();

      expect(await select.isOpen()).toBe(false);
      expect(host.value()).not.toBeUndefined();
      const trigger = fixture.nativeElement.querySelector('.tm-select__trigger');
      expect(document.activeElement).toBe(trigger);
    });

    it('Escape closes without selecting (stage-1 Esc only, §3.4)', async () => {
      const { fixture, select } = await setup(Host);
      await select.open();
      await select.sendTriggerKeys(TestKey.ESCAPE);
      await fixture.whenStable();
      expect(await select.isOpen()).toBe(false);
      expect(fixture.componentInstance.value()).toBeUndefined();
    });

    it('typeahead finds options via the explicit label input', async () => {
      const { fixture, select } = await setup(Host);
      await select.open();
      await select.sendTriggerKeys('j'); // Jordan
      await fixture.whenStable();
      const options = await select.getOptions();
      expect(await options[3].isActive()).toBe(true);
    });

    it('space inside an active typeahead query searches, never commits', async () => {
      const { fixture, select } = await setup(Host);
      const host = fixture.componentInstance;
      await select.open();

      // One key per stabilization: aria relays trigger keydowns to the
      // listbox through a signal, which holds only the LATEST event — a
      // burst-typed string would lose every char but the last.
      for (const char of 'united arab') {
        await select.sendTriggerKeys(char);
      }
      await fixture.whenStable();

      expect(await select.isOpen()).toBe(true); // the space did not commit/close
      expect(host.value()).toBeUndefined();
      const options = await select.getOptions();
      expect(await options[1].isActive()).toBe(true); // matched across the space

      await select.sendTriggerKeys(TestKey.ENTER);
      await fixture.whenStable();
      expect(host.value()).toBe(2);
    });

    it('space with no typeahead in flight commits the active option', async () => {
      const { fixture, select } = await setup(Host);
      const host = fixture.componentInstance;

      await select.sendTriggerKeys(TestKey.DOWN_ARROW); // opens
      await select.sendTriggerKeys(TestKey.DOWN_ARROW);
      await select.sendTriggerKeys(' ');
      await fixture.whenStable();

      expect(await select.isOpen()).toBe(false);
      expect(host.value()).not.toBeUndefined();
    });
  });

  describe('author-supplied aria-describedby (§2.1)', () => {
    @Component({
      imports: [TmSelect, TmOption],
      template: `
        <span id="ext-desc">External description</span>
        <tm-select aria-describedby="ext-desc" aria-label="Country">
          <tm-option [value]="1" label="One">One</tm-option>
        </tm-select>
      `,
    })
    class Host {}

    it('relocates to the trigger and survives, stripped from the host', async () => {
      const { fixture } = await setup(Host);
      const hostEl = fixture.nativeElement.querySelector('tm-select') as HTMLElement;
      const trigger = hostEl.querySelector('.tm-select__trigger') as HTMLElement;

      expect(trigger.getAttribute('aria-describedby')).toBe('ext-desc');
      expect(hostEl.getAttribute('aria-describedby')).toBeNull();
    });
  });

  describe('disabled options', () => {
    @Component({
      imports: [TmSelect, TmOption],
      template: `
        <tm-select [(value)]="value" (selectionChange)="emissions.push($event)">
          <tm-option [value]="1" label="Enabled">Enabled</tm-option>
          <tm-option [value]="2" label="Blocked" disabled>Blocked</tm-option>
        </tm-select>
      `,
    })
    class Host {
      readonly value = signal<number | undefined>(1);
      readonly emissions: number[] = [];
    }

    it('clicking a disabled option neither commits, closes, nor re-emits', async () => {
      const { fixture, select } = await setup(Host);
      const host = fixture.componentInstance;
      await select.open();

      const disabledRow = document.querySelectorAll('.tm-option__row')[1] as HTMLElement;
      expect(disabledRow.getAttribute('aria-disabled')).toBe('true');
      disabledRow.click();
      await fixture.whenStable();

      expect(await select.isOpen()).toBe(true);
      expect(host.value()).toBe(1);
      expect(host.emissions).toEqual([]);
    });

    it('Enter on a disabled active option (softDisabled navigation) is a no-op', async () => {
      const { fixture, select } = await setup(Host);
      const host = fixture.componentInstance;
      await select.open();

      await select.sendTriggerKeys(TestKey.DOWN_ARROW); // Blocked becomes active
      const options = await select.getOptions();
      expect(await options[1].isActive()).toBe(true);
      await select.sendTriggerKeys(TestKey.ENTER);
      await fixture.whenStable();

      expect(await select.isOpen()).toBe(true);
      expect(host.value()).toBe(1);
      expect(host.emissions).toEqual([]);
    });
  });

  describe('typeahead textContent fallback (§3.4)', () => {
    @Component({
      imports: [TmSelect, TmOption],
      template: `
        <tm-select [(value)]="value">
          @for (country of countries; track country.id) {
            <!-- No label input: derived from the projected text. -->
            <tm-option [value]="country.id">{{ country.name }}</tm-option>
          }
        </tm-select>
      `,
    })
    class Host {
      readonly value = signal<number | undefined>(undefined);
      readonly countries = COUNTRIES;
    }

    it('projected text drives typeahead when no label is provided', async () => {
      const { fixture, select } = await setup(Host);
      await select.open();
      await select.sendTriggerKeys('e'); // Ethiopia
      await fixture.whenStable();
      const options = await select.getOptions();
      expect(await options[2].isActive()).toBe(true);
    });
  });

  describe('Signal Forms binding + precedence (DoD 2/3)', () => {
    @Component({
      imports: [TmSelect, TmOption, TmFormField, FormField],
      template: `
        <tm-form-field label="Country" hint="Where the tenant operates">
          <tm-select [formField]="f.countryId">
            @for (country of countries; track country.id) {
              <tm-option [value]="country.id" [label]="country.name">{{ country.name }}</tm-option>
            }
          </tm-select>
        </tm-form-field>
      `,
    })
    class Host {
      readonly model = signal<{ countryId: number | null }>({ countryId: null });
      readonly f = form(this.model, (p) => {
        required(p.countryId);
      });
      readonly countries = COUNTRIES;
    }

    it('selection flows into the form model; label id reaches aria-labelledby', async () => {
      const { fixture, select } = await setup(Host);
      const host = fixture.componentInstance;

      await select.selectOption('Jordan');
      await fixture.whenStable();
      expect(host.model().countryId).toBe(4);

      // Non-labelable <div> trigger: the field passed its label id (§3.1).
      const trigger = fixture.nativeElement.querySelector('.tm-select__trigger');
      const labelledBy = trigger.getAttribute('aria-labelledby');
      expect(labelledBy).toBeTruthy();
      expect(document.getElementById(labelledBy)?.textContent).toContain('Country');
    });

    @Component({
      imports: [TmSelect, TmOption, FormField],
      template: `
        <tm-select [formField]="f.countryId">
          @for (country of countries; track country.id) {
            <tm-option [value]="country.id">{{ country.name }}</tm-option>
          }
        </tm-select>
      `,
    })
    class DisabledHost {
      readonly model = signal<{ countryId: number | null }>({ countryId: 1 });
      readonly f = form(this.model, (p) => {
        disabled(p.countryId, { when: () => true });
      });
      readonly countries = COUNTRIES;
    }

    it('the bound field is authoritative for disabled; a disabled select does not open', async () => {
      const { fixture, select } = await setup(DisabledHost);
      expect(await select.isDisabled()).toBe(true);
      const trigger = fixture.nativeElement.querySelector(
        '.tm-select__trigger',
      ) as HTMLElement;
      trigger.click();
      await fixture.whenStable();
      expect(await select.isOpen()).toBe(false);
    });
  });

  describe('grid shaping (DoD 11)', () => {
    @Component({
      imports: [TmSelect, TmOption],
      template: `
        <tm-select [(value)]="value">
          @for (country of countries; track country.id) {
            <tm-option [value]="country.id" [label]="country.name">{{ country.name }}</tm-option>
          }
        </tm-select>
      `,
    })
    class Host {
      readonly value = signal<number | undefined>(2);
      readonly countries = COUNTRIES;
    }

    it('cancel() reverts an activation commit to the pre-edit value', async () => {
      const { fixture, select } = await setup(Host);
      const host = fixture.componentInstance; // starts at 2 (external baseline)
      const instance = fixture.debugElement.query(
        (el) => el.componentInstance instanceof TmSelect,
      ).componentInstance as TmSelect<number>;

      await select.selectOption('Jordan');
      await fixture.whenStable();
      expect(host.value()).toBe(4); // the activation committed to the model…

      instance.cancel(); // …but the HOST decides (second Esc, §3.4)
      await fixture.whenStable();
      expect(host.value()).toBe(2); // back to the value before the edit
    });

    it('external writes and commit() move the baseline; cancel() returns to it', async () => {
      const { fixture, select } = await setup(Host);
      const host = fixture.componentInstance;
      const instance = fixture.debugElement.query(
        (el) => el.componentInstance instanceof TmSelect,
      ).componentInstance as TmSelect<number>;

      host.value.set(1); // grid loads a row through the value channel
      await fixture.whenStable();

      await select.selectOption('Jordan');
      await fixture.whenStable();
      instance.commit(); // host accepts the edit — 4 is the new baseline

      await select.selectOption('Ethiopia');
      await fixture.whenStable();
      expect(host.value()).toBe(3);

      instance.cancel();
      await fixture.whenStable();
      expect(host.value()).toBe(4); // the committed Jordan, not the loaded 1
    });
  });
});

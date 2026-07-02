import { Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { TestbedHarnessEnvironment } from '@angular/cdk/testing/testbed';
import { form, FormField, validate } from '@angular/forms/signals';

import { provideTellmaUi } from '@tellma/core-ui';
import { TmFormField } from '@tellma/core-ui/form-field';
import { TmCheckboxHarness, TmFormFieldHarness } from '@tellma/core-ui-testing';

import { TmCheckbox } from './tm-checkbox';

describe('tm-checkbox (§3.3)', () => {
  async function setup<T>(component: new () => T) {
    TestBed.configureTestingModule({ providers: [provideTellmaUi()] });
    const fixture = TestBed.createComponent(component);
    await fixture.whenStable();
    await new Promise((resolve) => setTimeout(resolve, 0));
    await fixture.whenStable();
    const loader = TestbedHarnessEnvironment.loader(fixture);
    return { fixture, loader, checkbox: await loader.getHarness(TmCheckboxHarness) };
  }

  describe('standalone tri-state semantics', () => {
    @Component({
      imports: [TmCheckbox],
      template: `
        <tm-checkbox [(checked)]="checked" [(indeterminate)]="indeterminate">
          Select all rows
        </tm-checkbox>
      `,
    })
    class Host {
      readonly checked = signal(false);
      readonly indeterminate = signal(true);
    }

    it('drives the native .indeterminate IDL property; no manual aria-checked', async () => {
      const { fixture, checkbox } = await setup(Host);
      expect(await checkbox.isIndeterminate()).toBe(true);
      expect(await checkbox.isChecked()).toBe(false);

      // The IDL property is the mechanism — the attribute must NOT exist,
      // and no hand-written aria-checked competes with the browser (§3.3).
      const native = fixture.nativeElement.querySelector('input') as HTMLInputElement;
      expect(native.hasAttribute('indeterminate')).toBe(false);
      expect(native.hasAttribute('aria-checked')).toBe(false);
      expect(native.indeterminate).toBe(true);
    });

    it('a user toggle clears indeterminate (native behavior)', async () => {
      const { fixture, checkbox } = await setup(Host);
      await checkbox.toggle();
      await fixture.whenStable();
      expect(await checkbox.isChecked()).toBe(true);
      expect(await checkbox.isIndeterminate()).toBe(false);
      expect(fixture.componentInstance.indeterminate()).toBe(false);
    });

    it('setting indeterminate does not change checked (independence)', async () => {
      const { fixture, checkbox } = await setup(Host);
      fixture.componentInstance.indeterminate.set(false);
      await fixture.whenStable();
      expect(await checkbox.isChecked()).toBe(false);
      fixture.componentInstance.indeterminate.set(true);
      await fixture.whenStable();
      expect(await checkbox.isChecked()).toBe(false);
    });

    // Space-to-toggle is NATIVE activation behavior that synthetic TestBed
    // keystrokes cannot trigger — it is covered by the Playwright battery
    // with trusted input.
    it('label click toggles', async () => {
      const { fixture, checkbox } = await setup(Host);
      const label = fixture.nativeElement.querySelector('.tm-checkbox__label') as HTMLElement;
      label.click();
      await fixture.whenStable();
      expect(await checkbox.isChecked()).toBe(true);
      expect(await checkbox.getLabelText()).toBe('Select all rows');
    });
  });

  describe('Signal Forms binding (FormCheckboxControl, DoD 2)', () => {
    @Component({
      imports: [TmCheckbox, TmFormField, FormField],
      template: `
        <tm-form-field label="Terms" hint="You must accept to continue">
          <tm-checkbox [formField]="f.accepted">I accept the terms</tm-checkbox>
        </tm-form-field>
      `,
    })
    class Host {
      readonly model = signal({ accepted: false });
      readonly f = form(this.model, (p) => {
        validate(p.accepted, ({ value }) =>
          value() ? undefined : { kind: 'mustAccept', message: 'Please accept the terms' },
        );
      });
    }

    it('binds through checked (no value property) and surfaces errors via the field', async () => {
      const { fixture, loader, checkbox } = await setup(Host);
      const host = fixture.componentInstance;
      const field = await loader.getHarness(TmFormFieldHarness);

      // FormCheckboxControl: `checked` is the channel; `value` must not exist.
      const instance = fixture.debugElement.query(
        (el) => el.componentInstance instanceof TmCheckbox,
      ).componentInstance as TmCheckbox;
      expect('value' in instance).toBe(false);

      await checkbox.toggle();
      await fixture.whenStable();
      expect(host.model().accepted).toBe(true);

      await checkbox.toggle();
      await checkbox.blur();
      await fixture.whenStable();
      expect(host.model().accepted).toBe(false);
      expect(await field.getErrorText()).toBe('Please accept the terms');
    });
  });

  describe('unbound inputs', () => {
    @Component({
      imports: [TmCheckbox],
      template: `<tm-checkbox disabled required [checked]="true">Locked</tm-checkbox>`,
    })
    class Host {}

    it('static attributes apply in non-form usage', async () => {
      const { checkbox } = await setup(Host);
      expect(await checkbox.isDisabled()).toBe(true);
      expect(await checkbox.isRequired()).toBe(true);
      expect(await checkbox.isChecked()).toBe(true);
    });
  });
});

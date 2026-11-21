// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { TestbedHarnessEnvironment } from '@angular/cdk/testing/testbed';
import { TestKey } from '@angular/cdk/testing';
import { disabled, form, FormField, readonly, required } from '@angular/forms/signals';

import { provideTellmaUi } from '@tellma/core-ui';
import { TmFormField } from '@tellma/core-ui/form-field';
import { TmFormFieldHarness, TmRadioGroupHarness } from '@tellma/core-ui-testing';

import { TmRadio } from './tm-radio';
import { TmRadioGroup } from './tm-radio-group';

/** A radio group bound to a Signal Forms field inside a tm-form-field. */
@Component({
  imports: [FormField, TmFormField, TmRadio, TmRadioGroup],
  template: `
    <div [dir]="dir()">
      <tm-form-field label="Payment terms">
        <tm-radio-group [formField]="terms.kind">
          <tm-radio value="cash" label="Cash" />
          <tm-radio value="credit" label="Credit" hint="Due in 30 days" />
          <tm-radio value="barter" label="Barter" disabled />
          <tm-radio value="cheque" label="Cheque" />
        </tm-radio-group>
      </tm-form-field>
    </div>
  `,
})
class BoundHost {
  readonly dir = signal<'ltr' | 'rtl'>('ltr');
  readonly locked = signal(false);
  readonly model = signal<{ kind: string | null }>({ kind: null });
  readonly terms = form(this.model, (path) => {
    required(path.kind);
    readonly(path.kind, () => this.locked());
  });
}

/** A standalone group named by its own aria-label, laid out horizontally. */
@Component({
  imports: [TmRadio, TmRadioGroup],
  template: `
    <tm-radio-group ariaLabel="Size" orientation="horizontal" [(value)]="size" [disabled]="off()">
      <tm-radio [value]="1" label="Small" />
      <tm-radio [value]="2" label="Large" />
    </tm-radio-group>
  `,
})
class StandaloneHost {
  readonly size = signal<number | null>(2);
  readonly off = signal(false);
}

async function setup<T>(component: new () => T) {
  TestBed.configureTestingModule({ providers: [provideTellmaUi()] });
  const fixture = TestBed.createComponent(component);
  await fixture.whenStable();
  const loader = TestbedHarnessEnvironment.loader(fixture);
  return { fixture, loader, group: await loader.getHarness(TmRadioGroupHarness) };
}

describe('tm-radio-group', () => {
  it('writes the checked radio’s value into the bound field', async () => {
    const { fixture, group } = await setup(BoundHost);
    expect(await group.getCheckedLabel()).toBeNull();
    await group.check('Credit');
    expect(fixture.componentInstance.model().kind).toBe('credit');
    expect(await group.getCheckedLabel()).toBe('Credit');
  });

  it('checks the radio a model write names', async () => {
    const { fixture, group } = await setup(BoundHost);
    fixture.componentInstance.model.set({ kind: 'cheque' });
    await fixture.whenStable();
    expect(await group.getCheckedLabel()).toBe('Cheque');
  });

  it('puts the one tab stop on the checked radio, else the first enabled one', async () => {
    const { fixture, group } = await setup(BoundHost);
    const stops = async () =>
      Promise.all((await group.getRadios()).map((radio) => radio.isTabStop()));
    expect(await stops()).toEqual([true, false, false, false]);
    fixture.componentInstance.model.set({ kind: 'cheque' });
    await fixture.whenStable();
    expect(await stops()).toEqual([false, false, false, true]);
  });

  it('moves and checks with the arrows, skipping disabled radios and wrapping', async () => {
    const { fixture, group } = await setup(BoundHost);
    const [cash, credit, , cheque] = await group.getRadios();
    await cash.focus();
    await cash.press(TestKey.DOWN_ARROW);
    expect(fixture.componentInstance.model().kind).toBe('credit');
    expect(await credit.isFocused()).toBe(true);
    await credit.press(TestKey.DOWN_ARROW);
    expect(fixture.componentInstance.model().kind).toBe('cheque');
    expect(await cheque.isFocused()).toBe(true);
    await cheque.press(TestKey.RIGHT_ARROW);
    expect(fixture.componentInstance.model().kind).toBe('cash');
    await cash.press(TestKey.UP_ARROW);
    expect(fixture.componentInstance.model().kind).toBe('cheque');
  });

  it('maps Left and Right to the live direction', async () => {
    const { fixture, group } = await setup(BoundHost);
    fixture.componentInstance.dir.set('rtl');
    await fixture.whenStable();
    const [cash] = await group.getRadios();
    await cash.focus();
    await cash.press(TestKey.LEFT_ARROW);
    expect(fixture.componentInstance.model().kind).toBe('credit');
    const [, credit] = await group.getRadios();
    await credit.press(TestKey.RIGHT_ARROW);
    expect(fixture.componentInstance.model().kind).toBe('cash');
  });

  it('lets focus move but keeps the value while readonly', async () => {
    const { fixture, group } = await setup(BoundHost);
    fixture.componentInstance.model.set({ kind: 'cash' });
    fixture.componentInstance.locked.set(true);
    await fixture.whenStable();
    const [cash, credit] = await group.getRadios();
    await cash.focus();
    await cash.press(TestKey.DOWN_ARROW);
    expect(await credit.isFocused()).toBe(true);
    expect(fixture.componentInstance.model().kind).toBe('cash');
    await credit.check();
    expect(fixture.componentInstance.model().kind).toBe('cash');
    expect(await credit.isChecked()).toBe(false);
    expect(await cash.isChecked()).toBe(true);
  });

  it('is named by the enclosing field’s label and shows its required error once touched', async () => {
    const { fixture, loader, group } = await setup(BoundHost);
    const field = await loader.getHarness(TmFormFieldHarness);
    const labelledby = await group.getAriaLabelledby();
    const label = fixture.nativeElement.querySelector('.tm-form-field__label') as HTMLElement;
    expect(labelledby).toBe(label.id);
    expect(await group.isInvalid()).toBe(false);
    const [cash] = await group.getRadios();
    await cash.focus();
    (fixture.nativeElement.querySelector('input') as HTMLInputElement).blur();
    await fixture.whenStable();
    expect(await group.isInvalid()).toBe(true);
    expect(await field.getErrorTexts()).toEqual(['This field is required']);
  });

  it('links a radio’s hint as its description', async () => {
    const { group } = await setup(BoundHost);
    const [, credit] = await group.getRadios();
    expect(await credit.getHint()).toBe('Due in 30 days');
    const input = document.querySelectorAll<HTMLInputElement>('tm-radio-group input')[1];
    const hint = document.getElementById(input.getAttribute('aria-describedby')!);
    expect(hint?.textContent).toBe('Due in 30 days');
  });

  it('carries its own aria-label standalone, and disables every radio', async () => {
    const { fixture, group } = await setup(StandaloneHost);
    expect(await group.getAriaLabel()).toBe('Size');
    expect(await group.getAriaLabelledby()).toBeNull();
    expect(await group.getCheckedLabel()).toBe('Large');
    fixture.componentInstance.off.set(true);
    await fixture.whenStable();
    const radios = await group.getRadios();
    expect(await Promise.all(radios.map((radio) => radio.isDisabled()))).toEqual([true, true]);
  });

  it('reflects a bound disabled state', async () => {
    @Component({
      imports: [FormField, TmRadio, TmRadioGroup],
      template: `
        <tm-radio-group ariaLabel="Kind" [formField]="f.kind">
          <tm-radio value="a" label="A" />
        </tm-radio-group>
      `,
    })
    class DisabledHost {
      readonly f = form(signal({ kind: 'a' }), (path) => disabled(path.kind));
    }
    const { group } = await setup(DisabledHost);
    const [radio] = await group.getRadios();
    expect(await radio.isDisabled()).toBe(true);
  });
});

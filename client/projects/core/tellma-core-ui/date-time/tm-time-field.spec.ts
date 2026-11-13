// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { TestbedHarnessEnvironment } from '@angular/cdk/testing/testbed';
import { TestKey } from '@angular/cdk/testing';
import { form, FormField } from '@angular/forms/signals';

import { provideTellmaUi, TM_ACTIVE_LOCALE, TM_NOW, TM_TIME_ZONE } from '@tellma/core-ui';
import { TmFormField } from '@tellma/core-ui/form-field';
import { TmFormFieldHarness, TmTimeFieldHarness } from '@tellma/core-ui-testing';

import { TmTimeField } from './tm-time-field';

/** A time field bound to a Signal Forms field inside a tm-form-field. */
@Component({
  imports: [FormField, TmFormField, TmTimeField],
  template: `
    <tm-form-field label="Opens at">
      <tm-time-field [formField]="shop.opens" [step]="step()" />
    </tm-form-field>
  `,
})
class Host {
  readonly step = signal(15);
  readonly model = signal<{ opens: string | null }>({ opens: null });
  readonly shop = form(this.model);
}

async function setup(locale = 'en-US') {
  const activeLocale = signal(locale);
  TestBed.configureTestingModule({
    providers: [
      provideTellmaUi(),
      { provide: TM_ACTIVE_LOCALE, useValue: activeLocale },
      { provide: TM_TIME_ZONE, useValue: signal('Asia/Riyadh') },
      // 2026-11-20 10:52 in Riyadh.
      { provide: TM_NOW, useValue: () => Date.parse('2026-11-20T07:52:00Z') },
    ],
  });
  const fixture = TestBed.createComponent(Host);
  await fixture.whenStable();
  const loader = TestbedHarnessEnvironment.loader(fixture);
  return {
    fixture,
    activeLocale,
    time: await loader.getHarness(TmTimeFieldHarness),
    field: await loader.getHarness(TmFormFieldHarness),
  };
}

describe('tm-time-field', () => {
  it('shows a bound value in the locale and writes typed text as HH:mm', async () => {
    const { fixture, time } = await setup();
    fixture.componentInstance.model.set({ opens: '14:30' });
    await fixture.whenStable();
    expect(await time.getText()).toBe('2:30 PM');
    await time.setText('9');
    expect(fixture.componentInstance.model().opens).toBe('09:00');
    await time.setText('930p');
    expect(fixture.componentInstance.model().opens).toBe('21:30');
    await time.blur();
    expect(await time.getText()).toBe('9:30 PM');
  });

  it('reports unreadable text as the parse error, with an example', async () => {
    const { fixture, time, field } = await setup();
    await time.setText('25:00');
    await time.blur();
    await fixture.whenStable();
    expect(fixture.componentInstance.model().opens).toBeNull();
    expect(
      fixture.componentInstance.shop
        .opens()
        .errors()
        .map((error) => error.kind),
    ).toEqual(['parse']);
    expect(await time.isInvalid()).toBe(true);
    expect(await field.getErrorTexts()).toEqual(['Enter a time like 2:30 PM']);
    // Unreadable text stays for correction.
    expect(await time.getText()).toBe('25:00');
    await time.setText('');
    await fixture.whenStable();
    expect(fixture.componentInstance.shop.opens().errors()).toEqual([]);
  });

  it('steps with Up and Down, wrapping at midnight', async () => {
    const { fixture, time } = await setup('en-GB');
    fixture.componentInstance.model.set({ opens: '23:50' });
    await fixture.whenStable();
    await time.focus();
    await time.press(TestKey.UP_ARROW);
    expect(fixture.componentInstance.model().opens).toBe('00:05');
    expect(await time.getText()).toBe('0:05');
    await time.press(TestKey.DOWN_ARROW);
    await time.press(TestKey.DOWN_ARROW);
    expect(fixture.componentInstance.model().opens).toBe('23:35');
  });

  it('starts stepping from the clock in the display zone, on the step grid', async () => {
    const { fixture, time } = await setup('en-GB');
    await time.focus();
    await time.press(TestKey.UP_ARROW);
    expect(fixture.componentInstance.model().opens).toBe('10:45');
  });

  it('re-renders on a locale switch without touching the value', async () => {
    const { fixture, activeLocale, time } = await setup();
    fixture.componentInstance.model.set({ opens: '14:30' });
    await fixture.whenStable();
    activeLocale.set('ar-SA');
    await fixture.whenStable();
    expect(await time.getText()).toBe('٢:٣٠ م');
    expect(fixture.componentInstance.model().opens).toBe('14:30');
    expect(fixture.componentInstance.shop.opens().dirty()).toBe(false);
  });

  it('is labelled by the field and tabular', async () => {
    const { fixture } = await setup();
    const input = fixture.nativeElement.querySelector('input') as HTMLInputElement;
    expect(input.labels?.[0]?.textContent?.trim()).toBe('Opens at');
    expect(input.getAttribute('dir')).toBe('auto');
    expect(getComputedStyle(input).fontVariantNumeric).toBe('tabular-nums');
  });
});

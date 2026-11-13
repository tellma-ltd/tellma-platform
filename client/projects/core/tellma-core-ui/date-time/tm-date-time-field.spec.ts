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
import { TmDateTimeFieldHarness } from '@tellma/core-ui-testing';

import { TmDateTimeField } from './tm-date-time-field';

/** A date-time field bound to a Signal Forms field, in a zone of the test's choosing. */
@Component({
  imports: [FormField, TmDateTimeField],
  template: `
    <tm-date-time-field label="Starts" [formField]="window.startsAt" [timeZone]="zone()" />
  `,
})
class Host {
  readonly zone = signal<string | undefined>(undefined);
  readonly model = signal<{ startsAt: string | null }>({ startsAt: null });
  readonly window = form(this.model);
}

/** Sets the host up in en-GB, with the display zone and the clock given. */
async function setup(options: { zone?: string; now?: string; value?: string | null } = {}) {
  TestBed.configureTestingModule({
    providers: [
      provideTellmaUi(),
      { provide: TM_ACTIVE_LOCALE, useValue: signal('en-GB') },
      { provide: TM_TIME_ZONE, useValue: signal(options.zone ?? 'Asia/Riyadh') },
      { provide: TM_NOW, useValue: () => Date.parse(options.now ?? '2026-11-20T07:52:00Z') },
    ],
  });
  const fixture = TestBed.createComponent(Host);
  fixture.componentInstance.model.set({ startsAt: options.value ?? null });
  await fixture.whenStable();
  const loader = TestbedHarnessEnvironment.loader(fixture);
  const field = await loader.getHarness(TmDateTimeFieldHarness);
  return {
    fixture,
    field,
    date: await field.getDate(),
    time: await field.getTime(),
    value: () => fixture.componentInstance.model().startsAt,
    state: () => fixture.componentInstance.window.startsAt(),
  };
}

describe('tm-date-time-field', () => {
  it('writes the instant its parts name, with the zone offset and no fraction', async () => {
    const { date, time, value } = await setup();
    await date.setText('20/11/2026');
    await time.setText('14');
    expect(value()).toBe('2026-11-20T14:00:00+03:00');
  });

  it('shows a bound instant’s parts in the zone, and the zone label', async () => {
    const { date, field, time } = await setup({ value: '2026-11-20T11:00:00Z' });
    expect(await date.getText()).toBe('20/11/2026');
    expect(await time.getText()).toBe('14:00');
    expect(await field.getZoneLabel()).toBe('GMT+3');
    expect(await field.getLabel()).toBe('Starts');
  });

  it('labels the zone at the current time while empty, and at the instant once set', async () => {
    // Beirut: GMT+2 in winter, GMT+3 in summer.
    const { date, field, time } = await setup({ zone: 'Asia/Beirut', now: '2026-01-10T08:00:00Z' });
    expect(await field.getZoneLabel()).toBe('GMT+2');
    await date.setText('15/07/2026');
    await time.setText('10:00');
    expect(await field.getZoneLabel()).toBe('GMT+3');
  });

  it('reports one part without the other as dateTimeIncomplete, on the empty part', async () => {
    const { date, field, state, value } = await setup();
    await date.setText('20/11/2026');
    await date.blur();
    expect(value()).toBeNull();
    expect(
      state()
        .errors()
        .map((error) => error.kind),
    ).toEqual(['dateTimeIncomplete']);
    const timeField = await field.getTimeField();
    const dateField = await field.getDateField();
    expect(await timeField.getErrorTexts()).toEqual(['Enter both a date and a time']);
    expect(await dateField.getErrorTexts()).toEqual([]);
    expect(await (await field.getTime()).isInvalid()).toBe(true);
  });

  it('reports a part’s unreadable text as the field’s parse error', async () => {
    const { date, field, state, time, value } = await setup();
    await date.setText('20/11/2026');
    await time.setText('99:99');
    await time.blur();
    await new Promise((resolve) => setTimeout(resolve));
    expect(value()).toBeNull();
    expect(
      state()
        .errors()
        .map((error) => error.kind),
    ).toEqual(['parse']);
    expect(await (await field.getTimeField()).getErrorTexts()).toEqual(['Enter a time like 14:30']);
  });

  it('writes nothing on a blur or a calendar pick that re-reads a bound +00:00 instant with a fraction', async () => {
    const bound = '2026-11-20T11:00:00.0000000+00:00';
    const { date, fixture, state, time, value } = await setup({ zone: 'UTC', value: bound });
    expect(await time.getText()).toBe('11:00');
    // Enter re-reads the time's text; the blur and the pick re-read the date.
    await time.focus();
    await time.press(TestKey.ENTER);
    await time.blur();
    await date.focus();
    await date.blur();
    await date.openPopup();
    await date.selectDay(20);
    await fixture.whenStable();
    expect(value()).toBe(bound);
    expect(state().dirty()).toBe(false);
  });

  it('reads a time inside a forward daylight-saving change as the change itself', async () => {
    const { date, time, value } = await setup({ zone: 'Asia/Beirut' });
    await date.setText('29/03/2026');
    await time.setText('00:30');
    await time.blur();
    expect(value()).toBe('2026-03-29T01:00:00+03:00');
    expect(await time.getText()).toBe('1:00');
  });

  it('reads a repeated time inside a backward change as the earlier instant', async () => {
    const { date, time, value } = await setup({ zone: 'Asia/Beirut' });
    await date.setText('24/10/2026');
    await time.setText('23:30');
    expect(value()).toBe('2026-10-24T23:30:00+03:00');
  });

  it('shows the same instant on the new clock when the zone changes, writing nothing', async () => {
    const bound = '2026-11-20T11:00:00Z';
    const { fixture, field, time, value } = await setup({ zone: 'UTC', value: bound });
    fixture.componentInstance.zone.set('Asia/Riyadh');
    await fixture.whenStable();
    expect(await time.getText()).toBe('14:00');
    expect(await field.getZoneLabel()).toBe('GMT+3');
    expect(value()).toBe(bound);
  });

  it('clears to null when both parts are emptied', async () => {
    const { date, time, value } = await setup({ value: '2026-11-20T11:00:00Z' });
    await date.setText('');
    await time.setText('');
    expect(value()).toBeNull();
  });
});

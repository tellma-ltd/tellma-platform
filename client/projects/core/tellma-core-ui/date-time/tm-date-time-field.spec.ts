// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { TestbedHarnessEnvironment } from '@angular/cdk/testing/testbed';
import { TestKey } from '@angular/cdk/testing';
import { form, FormField, required } from '@angular/forms/signals';

import { provideTellmaUi, TM_ACTIVE_LOCALE, TM_NOW, TM_TIME_ZONE } from '@tellma/core-ui';
import { TmDateTimeFieldHarness } from '@tellma/core-ui-testing';

import { TmDateTimeField } from './tm-date-time-field';

/**
 * A date-time field bound to a Signal Forms field, in a zone of the test's
 * choosing, required when the test asks.
 */
@Component({
  imports: [FormField, TmDateTimeField],
  template: `
    <tm-date-time-field label="Starts" [formField]="window.startsAt" [timeZone]="zone()" />
  `,
})
class Host {
  readonly zone = signal<string | undefined>(undefined);
  readonly isRequired = signal(false);
  readonly model = signal<{ startsAt: string | null }>({ startsAt: null });
  readonly window = form(this.model, (path) => {
    required(path.startsAt, { when: () => this.isRequired() });
  });
}

/** Sets the host up in en-GB, with the display zone and the clock given. */
async function setup(
  options: { zone?: string; now?: string; value?: string | null; required?: boolean } = {},
) {
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
  fixture.componentInstance.isRequired.set(options.required ?? false);
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

  it('shows only the incomplete pair on a required field, never "required" on the filled part', async () => {
    const { date, field, state } = await setup({ required: true });
    await date.setText('20/11/2026');
    await date.blur();
    // The bound field fails both rules; the field shows the gap once, on the empty part.
    expect(
      state()
        .errors()
        .map((error) => error.kind)
        .sort(),
    ).toEqual(['dateTimeIncomplete', 'required']);
    expect(await (await field.getDateField()).getErrorTexts()).toEqual([]);
    expect(await date.isInvalid()).toBe(false);
    expect(await (await field.getTimeField()).getErrorTexts()).toEqual([
      'Enter both a date and a time',
    ]);
    expect(await (await field.getTime()).isInvalid()).toBe(true);
  });

  it('still shows "required" when both parts are empty', async () => {
    const { date, field } = await setup({ required: true });
    await date.focus();
    await date.blur();
    expect(await (await field.getDateField()).getErrorTexts()).toEqual(['This field is required']);
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

  it('writes nothing on a re-read of a bound value with seconds', async () => {
    const bound = '2026-11-20T11:00:30+00:00';
    const { date, fixture, state, time, value } = await setup({ zone: 'UTC', value: bound });
    expect(await time.getText()).toBe('11:00');
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

  it('writes a changed minute of a bound value with seconds, without the seconds', async () => {
    const { time, value } = await setup({ zone: 'UTC', value: '2026-11-20T11:00:30+00:00' });
    await time.setText('11:01');
    expect(value()).toBe('2026-11-20T11:01:00+00:00');
  });

  it('keeps a bound value in the second occurrence of a repeated hour across a zone change', async () => {
    // Beirut repeats 23:00–00:00 on 24 October 2026; this is the later 23:30.
    const bound = '2026-10-24T23:30:00+02:00';
    const { fixture, field, time, value } = await setup({ zone: 'UTC', value: bound });
    expect(await time.getText()).toBe('21:30');
    fixture.componentInstance.zone.set('Asia/Beirut');
    await fixture.whenStable();
    expect(await time.getText()).toBe('23:30');
    expect(await field.getZoneLabel()).toBe('GMT+2');
    await time.focus();
    await time.press(TestKey.ENTER);
    await time.blur();
    await fixture.whenStable();
    expect(value()).toBe(bound);
  });

  it('describes the time input by the zone label', async () => {
    const { fixture, field, time } = await setup({ value: '2026-11-20T11:00:00Z' });
    const ids = ((await time.getDescribedBy()) ?? '').split(' ');
    const host = fixture.nativeElement as HTMLElement;
    const descriptions = ids.map((id) => host.querySelector(`[id="${id}"]`)?.textContent?.trim());
    expect(descriptions).toContain(await field.getZoneLabel());
    expect(await field.getZoneLabel()).toBe('GMT+3');
  });

  it('reserves the zone slot for the widest label, so the parts never move', async () => {
    // No token sheet loads in unit tests: the host carries the geometry.
    const { fixture, date, time } = await setup({ zone: 'Australia/Adelaide' });
    const host = fixture.nativeElement.querySelector('tm-date-time-field') as HTMLElement;
    host.style.cssText = 'inline-size: 480px; --date-time-time-width: 112px; --date-time-gap: 8px';
    const box = (selector: string) =>
      (host.querySelector(selector) as HTMLElement).getBoundingClientRect().toJSON();
    await date.setText('15/01/2026');
    await time.setText('10:00');
    await fixture.whenStable();
    expect(host.querySelector('.tm-date-time-field__zone')?.textContent?.trim()).toBe('GMT+10:30');
    const dateBefore = box('.tm-date-time-field__date');
    const timeBefore = box('.tm-date-time-field__time');
    await date.setText('15/07/2026');
    await fixture.whenStable();
    expect(host.querySelector('.tm-date-time-field__zone')?.textContent?.trim()).toBe('GMT+9:30');
    expect(box('.tm-date-time-field__date')).toEqual(dateBefore);
    expect(box('.tm-date-time-field__time')).toEqual(timeBefore);
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

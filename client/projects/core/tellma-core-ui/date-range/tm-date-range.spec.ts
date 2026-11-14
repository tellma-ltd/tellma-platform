// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { By } from '@angular/platform-browser';
import { TestbedHarnessEnvironment } from '@angular/cdk/testing/testbed';
import { form, FormField } from '@angular/forms/signals';

import { provideTellmaUi, TM_ACTIVE_LOCALE } from '@tellma/core-ui';
import { TmDatePicker } from '@tellma/core-ui/date-picker';
import { TmDateRangeHarness } from '@tellma/core-ui-testing';

import { TmDateRange } from './tm-date-range';
import { tmDateRangeOrder, type TmDateRangeValue } from './tm-date-range-order';

/** A date range bound to a Signal Forms field with the order validator. */
@Component({
  imports: [FormField, TmDateRange],
  template: `
    <tm-date-range
      label="Period"
      [formField]="filter.period"
      [layout]="layout()"
      minDate="2026-01-01"
      maxDate="2026-12-31"
    />
  `,
})
class Host {
  readonly layout = signal<'stacked' | 'inline'>('stacked');
  readonly model = signal<{ period: TmDateRangeValue }>({ period: { from: null, to: null } });
  readonly filter = form(this.model, (path) => tmDateRangeOrder(path.period));
}

async function setup() {
  TestBed.configureTestingModule({
    providers: [provideTellmaUi(), { provide: TM_ACTIVE_LOCALE, useValue: signal('en-GB') }],
  });
  const fixture = TestBed.createComponent(Host);
  await fixture.whenStable();
  const loader = TestbedHarnessEnvironment.loader(fixture);
  const range = await loader.getHarness(TmDateRangeHarness);
  return {
    fixture,
    range,
    from: await range.getFrom(),
    to: await range.getTo(),
    value: () => fixture.componentInstance.model().period,
    state: () => fixture.componentInstance.filter.period(),
  };
}

describe('tm-date-range', () => {
  it('writes each end into the bound value', async () => {
    const { from, range, to, value } = await setup();
    expect(await range.getLabel()).toBe('Period');
    expect(await (await range.getFromField()).getLabelText()).toBe('From');
    expect(await (await range.getToField()).getLabelText()).toBe('To');
    await from.setText('01/03/2026');
    expect(value()).toEqual({ from: '2026-03-01', to: null });
    await to.setText('31/03/2026');
    expect(value()).toEqual({ from: '2026-03-01', to: '2026-03-31' });
  });

  it('shows a bound range', async () => {
    const { fixture, from, to } = await setup();
    fixture.componentInstance.model.set({ period: { from: '2026-02-01', to: '2026-02-28' } });
    await fixture.whenStable();
    expect(await from.getText()).toBe('01/02/2026');
    expect(await to.getText()).toBe('28/02/2026');
  });

  it('reports an out-of-order range on the "to" field', async () => {
    const { fixture, from, range, state, to } = await setup();
    await from.setText('10/03/2026');
    await to.setText('01/03/2026');
    await to.blur();
    await fixture.whenStable();
    expect(
      state()
        .errors()
        .map((error) => error.kind),
    ).toEqual(['dateRangeOrder']);
    expect(await (await range.getToField()).getErrorTexts()).toEqual([
      'The end date must be on or after the start date',
    ]);
    expect(await (await range.getFromField()).getErrorTexts()).toEqual([]);
    expect(await to.isInvalid()).toBe(true);
    expect(await from.isInvalid()).toBe(false);
  });

  it('couples the pickers: each bounds the other within the range’s own bounds', async () => {
    const { fixture } = await setup();
    const [fromPicker, toPicker] = fixture.debugElement
      .queryAll(By.directive(TmDatePicker))
      .map((node) => node.componentInstance as TmDatePicker);
    expect([fromPicker.minDate(), fromPicker.maxDate()]).toEqual(['2026-01-01', '2026-12-31']);
    expect([toPicker.minDate(), toPicker.maxDate()]).toEqual(['2026-01-01', '2026-12-31']);
    fixture.componentInstance.model.set({ period: { from: '2026-03-10', to: '2026-04-20' } });
    await fixture.whenStable();
    expect(fromPicker.maxDate()).toBe('2026-04-20');
    expect(toPicker.minDate()).toBe('2026-03-10');
  });

  it('reports an end’s unreadable text as the range’s parse error, on that end', async () => {
    const { fixture, from, range, state } = await setup();
    await from.setText('not a date');
    await from.blur();
    await fixture.whenStable();
    await new Promise((resolve) => setTimeout(resolve));
    expect(
      state()
        .errors()
        .map((error) => error.kind),
    ).toEqual(['parse']);
    expect(await (await range.getFromField()).getErrorTexts()).toHaveLength(1);
    expect(await (await range.getToField()).getErrorTexts()).toEqual([]);
  });

  it('writes nothing when an end re-reads its bound date', async () => {
    const { fixture, from, state } = await setup();
    fixture.componentInstance.model.set({ period: { from: '2026-02-01', to: '2026-02-28' } });
    await fixture.whenStable();
    const before = fixture.componentInstance.model().period;
    // A blur and a calendar pick of the bound day both re-read the end.
    await from.focus();
    await from.blur();
    await from.openPopup();
    await from.selectDay(1);
    await fixture.whenStable();
    expect(fixture.componentInstance.model().period).toBe(before);
    expect(state().dirty()).toBe(false);
  });

  it('lays the ends side by side inline', async () => {
    const { fixture } = await setup();
    const fields = () =>
      [...fixture.nativeElement.querySelectorAll('tm-form-field')].map((field) =>
        (field as HTMLElement).getBoundingClientRect(),
      );
    const [stackedFrom, stackedTo] = fields();
    expect(stackedTo.top).toBeGreaterThan(stackedFrom.top);
    fixture.componentInstance.layout.set('inline');
    await fixture.whenStable();
    const [inlineFrom, inlineTo] = fields();
    expect(inlineTo.top).toBe(inlineFrom.top);
  });
});

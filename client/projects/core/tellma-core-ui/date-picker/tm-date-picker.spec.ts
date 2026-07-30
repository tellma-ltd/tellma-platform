// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { Component, signal } from '@angular/core';
import { TestBed, type ComponentFixture } from '@angular/core/testing';
import { TestbedHarnessEnvironment } from '@angular/cdk/testing/testbed';
import { TranslocoService } from '@jsverse/transloco';
import { form, FormField, required } from '@angular/forms/signals';

import { provideTellmaUi, TM_CELL_EDITOR_HOST, tmMaxDate, tmMinDate } from '@tellma/core-ui';
import { tmUmalquraCalendar } from '@tellma/core-ui/calendar-umalqura';
import type { TmCellEditor } from '@tellma/core-ui/contracts';
import { TmFormField } from '@tellma/core-ui/form-field';
import type { TmCalendar } from '@tellma/core-ui/l10n';
import { TmDatePickerHarness } from '@tellma/core-ui-testing';

import { TmDatePicker } from './tm-date-picker';

@Component({
  imports: [TmDatePicker, TmFormField, FormField],
  template: `
    <tm-form-field label="Due date" data-testid="ff">
      <tm-date-picker
        [formField]="f.due"
        [minDate]="min()"
        [maxDate]="max()"
        [calendar]="calendar()"
        data-testid="picker"
      />
    </tm-form-field>
  `,
})
class Host {
  readonly model = signal<{ due: string | null }>({ due: null });
  readonly min = signal<string | undefined>(undefined);
  readonly max = signal<string | undefined>(undefined);
  readonly calendar = signal<TmCalendar | undefined>(undefined);
  readonly f = form(this.model, (p) => {
    required(p.due);
    tmMinDate(p.due, '2020-01-01');
    tmMaxDate(p.due, '2030-12-31');
  });
}

async function setup() {
  TestBed.configureTestingModule({
    providers: [provideTellmaUi({ availableLangs: ['en', 'ar'] })],
  });
  const fixture = TestBed.createComponent(Host);
  await fixture.whenStable();
  await new Promise((resolve) => setTimeout(resolve, 0));
  await fixture.whenStable();
  const input = fixture.nativeElement.querySelector(
    '.tm-date-picker__input',
  ) as HTMLInputElement;
  return { fixture, host: fixture.componentInstance, input };
}

async function type(fixture: ComponentFixture<Host>, input: HTMLInputElement, text: string) {
  input.focus();
  input.dispatchEvent(new FocusEvent('focus'));
  input.value = text;
  input.dispatchEvent(new Event('input', { bubbles: true }));
  await fixture.whenStable();
}

async function blur(fixture: ComponentFixture<Host>, input: HTMLInputElement) {
  input.dispatchEvent(new FocusEvent('blur'));
  input.blur();
  await fixture.whenStable();
}

describe('tm-date-picker', () => {
  it('renders the anatomy: input with popup wiring + a non-tab-stop calendar button', async () => {
    const { fixture, input } = await setup();
    expect(input.getAttribute('aria-haspopup')).toBe('dialog');
    expect(input.getAttribute('aria-expanded')).toBe('false');
    expect(input.placeholder).toBe('mm/dd/yyyy'); // en-US field order
    const button = fixture.nativeElement.querySelector(
      '.tm-date-picker__toggle',
    ) as HTMLButtonElement;
    expect(button.tabIndex).toBe(-1);
    expect(button.getAttribute('aria-label')).toBe('Choose date');
    // The field's label targets the internal input.
    const label = fixture.nativeElement.querySelector('label') as HTMLLabelElement;
    expect(label.htmlFor).toBe(input.id);
  });

  it('typed text commits on blur to the ISO model and reformats to display form', async () => {
    const { fixture, host, input } = await setup();
    await type(fixture, input, '3/5/2026');
    expect(host.model().due).toBe('2026-03-05'); // live parse, en-US order
    await blur(fixture, input);
    expect(input.value).toBe('3/5/2026');
    expect(host.model().due).toBe('2026-03-05');
  });

  it('commits on Enter and normalizes the display', async () => {
    const { fixture, host, input } = await setup();
    await type(fixture, input, '2026-03-05'); // ISO fast path
    input.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter', bubbles: true }));
    await fixture.whenStable();
    expect(input.value).toBe('3/5/2026');
    expect(host.model().due).toBe('2026-03-05');
  });

  it('unreadable text keeps the model null and shows the expected-pattern message', async () => {
    const { fixture, host, input } = await setup();
    await type(fixture, input, 'not a date');
    expect(host.model().due).toBeNull();
    await blur(fixture, input);
    expect(input.value).toBe('not a date'); // kept for correction
    const error = fixture.nativeElement.querySelector('.tm-form-field__error') as HTMLElement;
    expect(error.textContent).toContain('Enter a date like');
  });

  it('tmMinDate/tmMaxDate report the framework kinds with localized defaults', async () => {
    const { fixture, input } = await setup();
    await type(fixture, input, '1/1/2019'); // below the schema min
    await blur(fixture, input);
    const error = fixture.nativeElement.querySelector('.tm-form-field__error') as HTMLElement;
    // The bound is shown the way the field shows dates — the ISO value is
    // machine currency, not something to put in front of a user.
    expect(error.textContent).toContain('Enter a date on or after 1/1/2020');
  });

  it('a bound in an error message follows the display calendar', async () => {
    const { fixture, host, input } = await setup();
    host.calendar.set(tmUmalquraCalendar());
    await fixture.whenStable();
    await type(fixture, input, '1/1/1300'); // below the schema min, in Hijri
    await blur(fixture, input);
    const error = fixture.nativeElement.querySelector('.tm-form-field__error') as HTMLElement;
    // 2020-01-01 is 6 Jumada I 1441 AH; the message must not read 2020.
    expect(error.textContent).toContain('1441');
    expect(error.textContent).not.toContain('2020');
  });

  it('hard bounds are parse errors: year 0000 never becomes a value', async () => {
    const { fixture, host, input } = await setup();
    await type(fixture, input, '0000-01-01');
    expect(host.model().due).toBeNull();
    await blur(fixture, input);
    expect(
      (fixture.nativeElement.querySelector('.tm-form-field__error') as HTMLElement).textContent,
    ).toContain('Enter a date like');
  });

  it('canonicalization never corrupts the model — a year the pivot would rewrite', async () => {
    const { fixture, host, input } = await setup();
    // ISO fast path commits year 44; the canonical display is '3/15/44',
    // which a RE-PARSE would pivot into 2044 — the display override must
    // pin the committed value instead.
    await type(fixture, input, '0044-03-15');
    expect(host.model().due).toBe('0044-03-15');
    await blur(fixture, input);
    expect(input.value).toBe('3/15/44');
    expect(host.model().due).toBe('0044-03-15'); // NOT 2044-03-15
  });

  it('the pin survives a pristine Enter and a pristine popup open', async () => {
    const { fixture, host, input } = await setup();
    await type(fixture, input, '0044-03-15');
    await blur(fixture, input);
    expect(input.value).toBe('3/15/44');

    // Enter with NO typing: the unconditional commit must not re-parse
    // its own canonical text (the pivot would rewrite 44 → 2044).
    input.focus();
    input.dispatchEvent(new FocusEvent('focus'));
    input.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter', bubbles: true }));
    await fixture.whenStable();
    expect(host.model().due).toBe('0044-03-15');
    expect(input.value).toBe('3/15/44');

    // Opening the popup commits pending text first — same pin applies.
    (
      fixture.nativeElement.querySelector('.tm-date-picker__toggle') as HTMLButtonElement
    ).click();
    await fixture.whenStable();
    expect(host.model().due).toBe('0044-03-15');
  });

  it('a calendar switch retires the pin: the same text re-reads in the new calendar', async () => {
    const { fixture, host, input } = await setup();
    await type(fixture, input, '0044-03-15');
    await blur(fixture, input);
    expect(host.model().due).toBe('0044-03-15');

    // Switch the display calendar, then type the OLD canonical text: it
    // must be read in the NEW calendar, never pinned to the dead one.
    host.calendar.set(tmUmalquraCalendar());
    await fixture.whenStable();
    await type(fixture, input, '3/15/44');
    expect(host.model().due).not.toBe('0044-03-15');
    await blur(fixture, input);
    expect(host.model().due).not.toBe('0044-03-15');
  });

  it('a locale switch keeps unreadable text for correction', async () => {
    const { fixture, host, input } = await setup();
    await type(fixture, input, 'not a date');
    await blur(fixture, input);
    expect(input.value).toBe('not a date');

    TestBed.inject(TranslocoService).setActiveLang('ar');
    await fixture.whenStable();
    await new Promise((resolve) => setTimeout(resolve, 20));
    await fixture.whenStable();
    expect(input.value).toBe('not a date'); // never silently erased
    expect(host.model().due).toBeNull();
  });

  it('paging past the ISO ceiling clamps focus to the bound; the keyboard stays alive', async () => {
    const { fixture, host, input } = await setup();
    await type(fixture, input, '9999-12-31');
    await blur(fixture, input);
    expect(host.model().due).toBe('9999-12-31');
    (
      fixture.nativeElement.querySelector('.tm-date-picker__toggle') as HTMLButtonElement
    ).click();
    await fixture.whenStable();

    const popup = document.querySelector('.tm-date-popup') as HTMLElement;
    const grid = popup.querySelector('[role="grid"]') as HTMLElement;
    grid.dispatchEvent(new KeyboardEvent('keydown', { key: 'PageDown', bubbles: true }));
    await fixture.whenStable();

    // Focus clamped to the ceiling: a reachable, enabled roving stop.
    const stop = popup.querySelector('[role="gridcell"] [tabindex="0"], [tabindex="0"][role="gridcell"], button[tabindex="0"]');
    expect(stop).not.toBeNull();
    expect((stop as HTMLButtonElement).disabled).toBe(false);

    popup.dispatchEvent(
      new KeyboardEvent('keydown', { key: 'Escape', bubbles: true, cancelable: true }),
    );
    await fixture.whenStable();
    expect(input.getAttribute('aria-expanded')).toBe('false'); // Esc still works
  });

  it('day-stepping past the ISO ceiling clamps instead of throwing', async () => {
    // 9999-12-31 is a Friday and a standard "no end date" sentinel, so
    // ArrowRight/ArrowDown/End all step past the ceiling. Serializing the
    // stepped date first would produce a 5-digit year, which slips through
    // the lexicographic clamp and then throws in the calendar adapter.
    const { fixture, host, input } = await setup();
    await type(fixture, input, '9999-12-31');
    await blur(fixture, input);
    (fixture.nativeElement.querySelector('.tm-date-picker__toggle') as HTMLButtonElement).click();
    await fixture.whenStable();
    const popup = document.querySelector('.tm-date-popup') as HTMLElement;
    const grid = popup.querySelector('[role="grid"]') as HTMLElement;

    for (const key of ['ArrowRight', 'ArrowDown', 'End']) {
      const event = new KeyboardEvent('keydown', { key, bubbles: true, cancelable: true });
      grid.dispatchEvent(event);
      await fixture.whenStable();
      expect(event.defaultPrevented).toBe(true); // consumed, so the page never scrolls
      const stop = popup.querySelector<HTMLButtonElement>('button[tabindex="0"]');
      expect(stop).not.toBeNull();
      expect(stop!.disabled).toBe(false);
    }
    expect(host.model().due).toBe('9999-12-31'); // navigation never commits
  });

  it('opens the popup on Alt+ArrowDown, commits pending text first, and Esc closes it', async () => {
    const { fixture, input } = await setup();
    await type(fixture, input, '3/5/2026');
    input.dispatchEvent(
      new KeyboardEvent('keydown', { key: 'ArrowDown', altKey: true, bubbles: true }),
    );
    await fixture.whenStable();
    expect(input.getAttribute('aria-expanded')).toBe('true');

    const popup = document.querySelector('.tm-date-popup') as HTMLElement;
    expect(popup).not.toBeNull();
    expect(popup.getAttribute('role')).toBe('dialog');
    // Opened on the committed value's month.
    expect(popup.querySelector('.tm-date-popup__view-switch')?.textContent).toContain('March');
    expect(popup.querySelector('[data-tm-day="5"][aria-selected="true"]')).not.toBeNull();

    popup.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true }));
    await fixture.whenStable();
    expect(input.getAttribute('aria-expanded')).toBe('false');
    expect(document.activeElement).toBe(input); // focus returns to the input
  });

  it('selecting a day commits it, closes, and returns focus to the input', async () => {
    const { fixture, host, input } = await setup();
    await type(fixture, input, '3/5/2026');
    await blur(fixture, input);
    (
      fixture.nativeElement.querySelector('.tm-date-picker__toggle') as HTMLButtonElement
    ).click();
    await fixture.whenStable();

    (document.querySelector('.tm-date-popup [data-tm-day="12"]') as HTMLButtonElement).click();
    await fixture.whenStable();
    expect(host.model().due).toBe('2026-03-12');
    expect(input.value).toBe('3/12/2026');
    expect(input.getAttribute('aria-expanded')).toBe('false');
    expect(document.activeElement).toBe(input);
  });

  it('Today and Clear commit today / null', async () => {
    const { fixture, host, input } = await setup();
    (
      fixture.nativeElement.querySelector('.tm-date-picker__toggle') as HTMLButtonElement
    ).click();
    await fixture.whenStable();
    const actions = document.querySelectorAll('.tm-date-popup__action');
    (actions[0] as HTMLButtonElement).click(); // Today
    await fixture.whenStable();
    const now = new Date();
    const todayIso = `${String(now.getFullYear()).padStart(4, '0')}-${String(
      now.getMonth() + 1,
    ).padStart(2, '0')}-${String(now.getDate()).padStart(2, '0')}`;
    expect(host.model().due).toBe(todayIso);

    (
      fixture.nativeElement.querySelector('.tm-date-picker__toggle') as HTMLButtonElement
    ).click();
    await fixture.whenStable();
    (document.querySelectorAll('.tm-date-popup__action')[1] as HTMLButtonElement).click(); // Clear
    await fixture.whenStable();
    expect(host.model().due).toBeNull();
    expect(input.value).toBe('');
  });

  it('the view ladder drills year → month → day, and min/max clamp navigation', async () => {
    const { fixture, host, input } = await setup();
    host.min.set('2026-03-01');
    host.max.set('2026-03-20');
    await type(fixture, input, '3/5/2026');
    await blur(fixture, input);
    (
      fixture.nativeElement.querySelector('.tm-date-picker__toggle') as HTMLButtonElement
    ).click();
    await fixture.whenStable();

    const popup = document.querySelector('.tm-date-popup') as HTMLElement;
    // Out-of-bounds days render disabled.
    expect(popup.querySelector('[data-tm-day="25"]')?.getAttribute('aria-disabled')).toBe('true');
    expect(popup.querySelector('[data-tm-day="12"]')?.getAttribute('aria-disabled')).toBeNull();

    // day → month → year via the header button, then drill back down.
    const switchButton = popup.querySelector('.tm-date-popup__view-switch') as HTMLButtonElement;
    switchButton.click();
    await fixture.whenStable();
    expect(popup.querySelector('[data-tm-month="3"]')).not.toBeNull();
    switchButton.click();
    await fixture.whenStable();
    expect(popup.querySelector('[data-tm-year="2026"]')).not.toBeNull();

    (popup.querySelector('[data-tm-year="2026"]') as HTMLButtonElement).click();
    await fixture.whenStable();
    (popup.querySelector('[data-tm-month="3"]') as HTMLButtonElement).click();
    await fixture.whenStable();
    expect(popup.querySelector('[data-tm-day="5"]')).not.toBeNull(); // back on days
  });

  it('months wholly outside the bounds are disabled, and Today is too', async () => {
    // A month view that let you drill into an out-of-range month landed
    // the roving stop on a disabled day; focus then fell to <body>, where
    // the dialog's Escape handler no longer hears anything.
    const { fixture, host, input } = await setup();
    host.min.set('2026-03-01');
    host.max.set('2026-03-20');
    await type(fixture, input, '3/5/2026');
    await blur(fixture, input);
    (fixture.nativeElement.querySelector('.tm-date-picker__toggle') as HTMLButtonElement).click();
    await fixture.whenStable();

    const popup = document.querySelector('.tm-date-popup') as HTMLElement;
    // Today is outside [2026-03-01, 2026-03-20], so it commits nothing.
    const today = popup.querySelector<HTMLButtonElement>('.tm-date-popup__action')!;
    expect(today.disabled).toBe(true);

    (popup.querySelector('.tm-date-popup__view-switch') as HTMLButtonElement).click();
    await fixture.whenStable();
    const january = popup.querySelector<HTMLButtonElement>('[data-tm-month="1"]')!;
    const march = popup.querySelector<HTMLButtonElement>('[data-tm-month="3"]')!;
    expect(january.disabled).toBe(true); // wholly before the lower bound
    expect(january.getAttribute('aria-disabled')).toBe('true');
    expect(march.disabled).toBe(false); // the bounds live inside it

    march.click();
    await fixture.whenStable();
    expect(popup.contains(document.activeElement)).toBe(true);
    popup.dispatchEvent(
      new KeyboardEvent('keydown', { key: 'Escape', bubbles: true, cancelable: true }),
    );
    await fixture.whenStable();
    expect(input.getAttribute('aria-expanded')).toBe('false');
  });

  it('disabled/readonly also disable the calendar button and popup', async () => {
    @Component({
      imports: [TmDatePicker],
      template: `<tm-date-picker readonly />`,
    })
    class ReadonlyHost {}
    TestBed.configureTestingModule({ providers: [provideTellmaUi()] });
    const fixture = TestBed.createComponent(ReadonlyHost);
    await fixture.whenStable();
    const button = fixture.nativeElement.querySelector(
      '.tm-date-picker__toggle',
    ) as HTMLButtonElement;
    expect(button.disabled).toBe(true);
    button.click();
    await fixture.whenStable();
    expect(document.querySelector('.tm-date-popup')).toBeNull();
  });

  describe('as a grid cell editor', () => {
    it('registers, seeds, exposes raw text, and suppresses its own commit', async () => {
      const registered: TmCellEditor<unknown>[] = [];
      @Component({
        imports: [TmDatePicker],
        template: `<tm-date-picker data-testid="cell" />`,
      })
      class CellHost {}
      TestBed.configureTestingModule({
        providers: [
          provideTellmaUi(),
          {
            provide: TM_CELL_EDITOR_HOST,
            useValue: { register: (e: TmCellEditor<unknown>) => registered.push(e) },
          },
        ],
      });
      const fixture = TestBed.createComponent(CellHost);
      await fixture.whenStable();
      expect(registered).toHaveLength(1);
      const editor = registered[0];
      const input = fixture.nativeElement.querySelector(
        '.tm-date-picker__input',
      ) as HTMLInputElement;

      editor.seed?.('3/5');
      expect(input.value).toBe('3/5');
      expect(editor.text()).toBe('3/5');

      // Grid-hosted: blur never rewrites — the grid owns commit and parse.
      input.focus();
      input.dispatchEvent(new FocusEvent('focus'));
      input.value = '3/5/2026';
      input.dispatchEvent(new Event('input', { bubbles: true }));
      await fixture.whenStable();
      input.dispatchEvent(new FocusEvent('blur'));
      await fixture.whenStable();
      expect(input.value).toBe('3/5/2026');
      expect(editor.text()).toBe('3/5/2026');
    });
  });
});

describe('tm-date-picker (harness)', () => {
  it('drives text, the popup, and every view of the ladder', async () => {
    const { fixture } = await setup();
    const loader = TestbedHarnessEnvironment.loader(fixture);
    const picker = await loader.getHarness(TmDatePickerHarness);

    await picker.setText('3/5/2026');
    await picker.blur();
    expect(await picker.getText()).toBe('3/5/2026');

    // The ladder: day → month → year, then drill back down to a day.
    await picker.openPopup();
    expect(await picker.isPopupOpen()).toBe(true);
    await picker.switchView(); // → month
    await picker.selectMonth(6);
    await picker.selectDay(11);
    expect(await picker.getText()).toBe('6/11/2026');

    await picker.openPopup();
    await picker.switchView(); // → month
    await picker.switchView(); // → year
    await picker.selectYear(2027);
    await picker.selectMonth(6);
    await picker.selectDay(11);
    expect(await picker.getText()).toBe('6/11/2027');

    await picker.openPopup();
    await picker.clear();
    expect(await picker.getText()).toBe('');
  });
});

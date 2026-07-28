// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { Component, signal } from '@angular/core';
import { TestBed, type ComponentFixture } from '@angular/core/testing';
import { TranslocoService } from '@jsverse/transloco';
import { form, FormField, required } from '@angular/forms/signals';

import { provideTellmaUi, TM_CELL_EDITOR_HOST, tmMaxDate, tmMinDate } from '@tellma/core-ui';
import type { TmCellEditor } from '@tellma/core-ui/contracts';
import { TmFormField } from '@tellma/core-ui/form-field';

import { TmDatePicker } from './tm-date-picker';

@Component({
  imports: [TmDatePicker, TmFormField, FormField],
  template: `
    <tm-form-field label="Due date" data-testid="ff">
      <tm-date-picker [formField]="f.due" [minDate]="min()" [maxDate]="max()" data-testid="picker" />
    </tm-form-field>
  `,
})
class Host {
  readonly model = signal<{ due: string | null }>({ due: null });
  readonly min = signal<string | undefined>(undefined);
  readonly max = signal<string | undefined>(undefined);
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
    expect(error.textContent).toContain('Enter a date on or after 2020-01-01');
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

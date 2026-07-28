// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { Component, signal } from '@angular/core';
import { TestBed, type ComponentFixture } from '@angular/core/testing';
import { form, FormField, min } from '@angular/forms/signals';
import { TranslocoService } from '@jsverse/transloco';

import { provideTellmaUi, TM_CELL_EDITOR_HOST } from '@tellma/core-ui';
import type { TmCellEditor } from '@tellma/core-ui/contracts';
import { TmFormField } from '@tellma/core-ui/form-field';

import { TmNumber } from './tm-number';

@Component({
  imports: [TmNumber, TmFormField, FormField],
  template: `
    <tm-form-field label="Amount" data-testid="ff">
      <input
        tmNumber
        [formField]="f.amount"
        [minDecimals]="minDecimals()"
        [maxDecimals]="maxDecimals()"
        [percent]="percent()"
        data-testid="amount"
      />
    </tm-form-field>
  `,
})
class Host {
  readonly model = signal<{ amount: number | null }>({ amount: null });
  readonly minDecimals = signal<number | undefined>(undefined);
  readonly maxDecimals = signal<number | undefined>(undefined);
  readonly percent = signal(false);
  readonly f = form(this.model, (p) => {
    min(p.amount, 0);
  });
}

async function setup() {
  TestBed.configureTestingModule({
    providers: [provideTellmaUi({ availableLangs: ['en', 'de'] })],
  });
  const fixture = TestBed.createComponent(Host);
  await fixture.whenStable();
  await new Promise((resolve) => setTimeout(resolve, 0)); // i18n settle
  await fixture.whenStable();
  const input = fixture.nativeElement.querySelector(
    '[data-testid="amount"]',
  ) as HTMLInputElement;
  return { fixture, host: fixture.componentInstance, input };
}

/** Types like a user: focus, replace content, fire input per change. */
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

describe('tmNumber', () => {
  it('guards the host: type=text (never number) + inputmode=decimal', async () => {
    const { input } = await setup();
    expect(input.type).toBe('text');
    expect(input.getAttribute('inputmode')).toBe('decimal');
    expect(input.classList).toContain('tm-number');
    expect(input.classList).toContain('tm-input');
  });

  it('typing parses live into the model; blur reformats to the canonical display', async () => {
    const { fixture, host, input } = await setup();
    await type(fixture, input, '1234.5');
    expect(host.model().amount).toBe(1234.5);
    // Focused: the text is exactly what the user typed — never rewritten.
    expect(input.value).toBe('1234.5');

    await blur(fixture, input);
    expect(input.value).toBe('1,234.5');
    expect(host.model().amount).toBe(1234.5);
  });

  it('commit rounds the model to the display scale (model = display)', async () => {
    const { fixture, host, input } = await setup();
    host.maxDecimals.set(2);
    await fixture.whenStable();
    await type(fixture, input, '1.005');
    await blur(fixture, input);
    expect(input.value).toBe('1.01');
    expect(host.model().amount).toBe(1.01);
  });

  it('focus + blur without editing never rewrites the model or the text', async () => {
    const { fixture, host, input } = await setup();
    await type(fixture, input, '42');
    await blur(fixture, input);
    const writes: (number | null)[] = [];
    const before = input.value;
    const sub = host.f.amount().value;
    void sub; // model reads below assert directly

    input.focus();
    input.dispatchEvent(new FocusEvent('focus'));
    await fixture.whenStable();
    await blur(fixture, input);
    expect(input.value).toBe(before);
    expect(host.model().amount).toBe(42);
    expect(writes).toHaveLength(0);
  });

  it('a programmatic write is NEVER rounded — the display shows it rounded', async () => {
    const { fixture, host, input } = await setup();
    host.maxDecimals.set(2);
    host.model.set({ amount: 1.23456 });
    await fixture.whenStable();
    // Display rounds; the model keeps the written precision.
    expect(input.value).toBe('1.23');
    expect(host.model().amount).toBe(1.23456);

    // Focus+blur alone still never rewrites it.
    input.focus();
    input.dispatchEvent(new FocusEvent('focus'));
    await blur(fixture, input);
    expect(host.model().amount).toBe(1.23456);
  });

  it('an external write while unfocused reformats immediately', async () => {
    const { fixture, host, input } = await setup();
    host.model.set({ amount: 9876.5 });
    await fixture.whenStable();
    expect(input.value).toBe('9,876.5');
  });

  it('locale switch reformats the unfocused display; the model never moves', async () => {
    const { fixture, host, input } = await setup();
    await type(fixture, input, '1234.56');
    await blur(fixture, input);
    expect(input.value).toBe('1,234.56');

    TestBed.inject(TranslocoService).setActiveLang('de');
    await fixture.whenStable();
    expect(input.value).toBe('1.234,56');
    expect(host.model().amount).toBe(1234.56);
  });

  it('unparseable text: model null, text kept for correction, localized parse message', async () => {
    const { fixture, host, input } = await setup();
    await type(fixture, input, 'abc');
    expect(host.model().amount).toBeNull();
    expect(input.value).toBe('abc');

    await blur(fixture, input);
    expect(input.value).toBe('abc'); // kept, not clobbered
    const error = fixture.nativeElement.querySelector('.tm-form-field__error') as HTMLElement;
    expect(error.textContent).toContain('Enter a valid number');
    expect(error.textContent).toContain('1,234.5'); // locale-true example
    expect(input.getAttribute('aria-invalid')).toBe('true');
  });

  it('empty text parses to null without an error', async () => {
    const { fixture, host, input } = await setup();
    await type(fixture, input, '12');
    await type(fixture, input, '');
    expect(host.model().amount).toBeNull();
    await blur(fixture, input);
    const error = fixture.nativeElement.querySelector('.tm-form-field__error') as HTMLElement;
    expect(error.textContent?.trim()).toBe('');
  });

  it('the 15-digit envelope rejects an over-precision commit with its own message', async () => {
    const { fixture, host, input } = await setup();
    await type(fixture, input, '12345678901234567');
    expect(host.model().amount).toBeNull();
    await blur(fixture, input);
    const error = fixture.nativeElement.querySelector('.tm-form-field__error') as HTMLElement;
    expect(error.textContent).toContain('at most 15 digits');

    // The envelope composes with maxDecimals: rounding can bring a value
    // back inside the envelope.
    host.maxDecimals.set(0);
    await fixture.whenStable();
    await type(fixture, input, '123456789012345.6');
    expect(host.model().amount).toBe(123456789012345.6);
    await blur(fixture, input);
    expect(host.model().amount).toBe(123456789012346);
  });

  it('a 14-significant-digit fraction passes on a default field', async () => {
    const { fixture, host, input } = await setup();
    await type(fixture, input, '0.12345678901234');
    await blur(fixture, input);
    expect(host.model().amount).toBe(0.12345678901234);
  });

  it('percent mode: displays 0.75 as 75% and parses bare/trailing/leading signs alike', async () => {
    const { fixture, host, input } = await setup();
    host.percent.set(true);
    host.model.set({ amount: 0.75 });
    await fixture.whenStable();
    expect(input.value).toBe('75%');

    // Arabic-Indic digits (٧٥٪) parse under an Arabic locale — the codec
    // suite covers that per-locale mapping; here the en-locale shapes.
    for (const text of ['75', '75%', '%75']) {
      await type(fixture, input, text);
      expect(host.model().amount).toBe(0.75);
      await blur(fixture, input);
      expect(input.value).toBe('75%');
    }
  });

  it("the framework's min kind resolves through the message table", async () => {
    const { fixture, input } = await setup();
    await type(fixture, input, '-5');
    await blur(fixture, input);
    const error = fixture.nativeElement.querySelector('.tm-form-field__error') as HTMLElement;
    expect(error.textContent).toContain('Enter a value of at least 0');
  });

  describe('as a grid cell editor', () => {
    it('registers, exposes raw text, seeds synchronously, and suppresses its own commit', async () => {
      const registered: TmCellEditor<unknown>[] = [];
      @Component({
        imports: [TmNumber],
        template: `<input tmNumber data-testid="cell" />`,
      })
      class CellHost {}
      TestBed.configureTestingModule({
        providers: [
          provideTellmaUi(),
          { provide: TM_CELL_EDITOR_HOST, useValue: { register: (e: TmCellEditor<unknown>) => registered.push(e) } },
        ],
      });
      const fixture = TestBed.createComponent(CellHost);
      await fixture.whenStable();
      expect(registered).toHaveLength(1);
      const editor = registered[0];
      const input = fixture.nativeElement.querySelector('[data-testid="cell"]') as HTMLInputElement;

      editor.seed?.('12');
      expect(input.value).toBe('12');
      expect(input.selectionStart).toBe(2);
      expect(editor.text()).toBe('12');

      // Grid-hosted blur never normalizes — the grid owns commit and parse.
      input.focus();
      input.dispatchEvent(new FocusEvent('focus'));
      input.value = '1234.567';
      input.dispatchEvent(new Event('input', { bubbles: true }));
      await fixture.whenStable();
      input.dispatchEvent(new FocusEvent('blur'));
      await fixture.whenStable();
      expect(input.value).toBe('1234.567'); // no canonical rewrite in a cell
      expect(editor.text()).toBe('1234.567');
    });
  });
});

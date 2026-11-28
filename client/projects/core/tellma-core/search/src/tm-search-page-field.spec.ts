// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { TmLocalization } from '@tellma/core';
import { provideTellmaUi, TM_FORM_FIELD_DEFAULTS, TmBreakpoints, type TmBreakpoint, type TmControlSize } from '@tellma/core-ui';
import { TmFormField } from '@tellma/core-ui/form-field';
import type { TmGridDriverTarget } from '@tellma/core-ui/grid';
import { TmInput } from '@tellma/core-ui/input';
import { tmEmitCss, tmTokensDefault } from '@tellma/core-ui-tokens';

import { TmSearchPageField } from './tm-search-page-field';

// The search page's field draws the form field's chrome itself, around its
// magnifier, input and clear button, so a page that shows it never loads
// the form field and the input. These specs pin that chrome to a form
// field's box with an input in it, at every size step: the box, the type,
// and the glow that focus puts on the whole box.

/** The box properties the field copies from a form field's box. */
const BOX = [
  'block-size',
  'padding-inline-start',
  'padding-inline-end',
  'border-top-width',
  'border-top-style',
  'border-top-color',
  'border-top-left-radius',
  'background-color',
  'color',
  'font-family',
  'column-gap',
] as const;

/** The properties focus changes on the whole box. */
const FOCUS = ['border-top-color', 'box-shadow', 'outline-style', 'outline-width', 'outline-offset'] as const;

/**
 * The given computed properties of an element.
 *
 * @param element - The element.
 * @param names - The properties.
 * @returns Each property's value, by name.
 */
function style(element: Element, names: readonly string[]): Record<string, string> {
  const computed = getComputedStyle(element);
  return Object.fromEntries(names.map((name) => [name, computed.getPropertyValue(name)]));
}

/** A driver with no results behind it: the chrome is all these specs read. */
const DRIVER: TmGridDriverTarget = {
  activeRowId: signal(null),
  controlsId: signal('results'),
  activeDescendantId: signal(null),
  pageStep: signal(1),
  moveActiveRow: () => undefined,
  activateRow: () => false,
};

/** The search page's field beside a form field with an input, both at the workspace's size step. */
@Component({
  imports: [TmSearchPageField, TmFormField, TmInput],
  template: `
    <tm-search-page-field [driver]="driver" titleId="title" />
    <tm-form-field label="Reference">
      <input tmInput />
      <!-- The size step's type, as the input's global rule reads it. -->
      <span class="probe" style="font-size: var(--tm-field-font-size)"></span>
    </tm-form-field>
  `,
})
class Host {
  readonly driver = DRIVER;
}

/** The token sheet the unit page does not load; the chrome reads it. */
let tokenSheet: HTMLStyleElement;

beforeAll(() => {
  tokenSheet = document.createElement('style');
  tokenSheet.textContent = tmEmitCss(tmTokensDefault);
  document.head.appendChild(tokenSheet);
});

afterAll(() => tokenSheet.remove());

/**
 * Renders the field and the form field at a size step, on a wide screen.
 *
 * @param size - The workspace's size step.
 * @returns The field's box and input, and the form field's box, input and type probe.
 */
async function render(size: TmControlSize) {
  const wide: Partial<TmBreakpoints> = { current: signal<TmBreakpoint>('xl').asReadonly(), atLeast: () => signal(true).asReadonly() };
  TestBed.configureTestingModule({
    providers: [
      provideTellmaUi(),
      // The field's strings, by key: the chrome is all these specs read.
      { provide: TmLocalization, useValue: { ɵtext: (key: string) => () => key } },
      { provide: TM_FORM_FIELD_DEFAULTS, useValue: { size, requiredMarker: '*' } },
      { provide: TmBreakpoints, useValue: wide },
    ],
  });
  const fixture = TestBed.createComponent(Host);
  fixture.autoDetectChanges();
  await fixture.whenStable();
  const root = fixture.nativeElement as HTMLElement;
  const field = root.querySelector<HTMLElement>('tm-search-page-field')!;
  return {
    field,
    fieldInput: field.querySelector<HTMLInputElement>('input')!,
    box: root.querySelector<HTMLElement>('.tm-form-field__box')!,
    boxInput: root.querySelector<HTMLInputElement>('tm-form-field input')!,
    probe: root.querySelector<HTMLElement>('.probe')!,
  };
}

describe('the search page’s field', () => {
  for (const size of ['sm', 'md', 'lg'] as const) {
    it(`wears a form field's box, type and focus glow (${size})`, async () => {
      const { field, fieldInput, box, boxInput, probe } = await render(size);
      expect(field.classList.contains(`tm-search-page-field--${size}`) || size === 'md').toBe(true);
      expect(style(field, BOX)).toEqual(style(box, BOX));
      expect(getComputedStyle(fieldInput).fontSize).toBe(getComputedStyle(probe).fontSize);

      fieldInput.focus();
      const focusedField = style(field, FOCUS);
      boxInput.focus();
      expect(focusedField).toEqual(style(box, FOCUS));
      expect(focusedField['box-shadow']).not.toBe('none');
    });
  }
});

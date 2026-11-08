// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { Component, signal } from '@angular/core';
import { TestBed, type ComponentFixture } from '@angular/core/testing';
import { page } from 'vitest/browser';

import type { TmCellEditorHost } from '@tellma/core-ui/contracts';
import { provideTellmaUi, TM_CELL_EDITOR_HOST, type TmControlSize } from '@tellma/core-ui';
import { TmButton } from '@tellma/core-ui/button';
import { TmInput } from '@tellma/core-ui/input';
import { TmOption, TmSelect } from '@tellma/core-ui/select';

import { TmFormField } from './tm-form-field';

/** Resizes the test frame and waits for its media queries to see the width. */
async function resizeTo(width: number): Promise<void> {
  await page.viewport(width, 800);
  await vi.waitUntil(() => window.innerWidth === width);
  await new Promise((resolve) => requestAnimationFrame(() => resolve(undefined)));
}

/** Every control on the size ladder, sized by an optional explicit size. */
@Component({
  imports: [TmButton, TmFormField, TmInput, TmSelect, TmOption],
  template: `
    <button tmButton data-testid="button" [size]="size()">Save</button>
    <tm-form-field data-testid="field" label="Name" [size]="size()">
      <input tmInput />
    </tm-form-field>
    <tm-select data-testid="select" aria-label="Pick" [size]="size()">
      <tm-option value="a">A</tm-option>
    </tm-select>
    <tm-form-field data-testid="field-with-select" label="Kind">
      <tm-select data-testid="select-in-field">
        <tm-option value="a">A</tm-option>
      </tm-select>
    </tm-form-field>
  `,
})
class Host {
  readonly size = signal<TmControlSize | undefined>(undefined);
}

/** A select editing a grid cell (the grid provides TM_CELL_EDITOR_HOST). */
@Component({
  imports: [TmSelect, TmOption],
  template: `
    <tm-select data-testid="cell-select" aria-label="Cell">
      <tm-option value="a">A</tm-option>
    </tm-select>
  `,
})
class CellHost {}

/** Ignores registrations: only the editor's size is under test. */
class SilentCellHost implements TmCellEditorHost {
  register(): void {
    // nothing to record
  }
}

/** The size class each control carries, or 'none' when it carries none. */
function sizes(fixture: ComponentFixture<unknown>): Record<string, string> {
  const of = (testId: string, prefix: string): string => {
    const element = fixture.nativeElement.querySelector(`[data-testid="${testId}"]`) as HTMLElement;
    for (const size of ['sm', 'md', 'lg']) {
      if (element.classList.contains(`${prefix}--${size}`)) {
        return size;
      }
    }
    return 'none';
  };
  return {
    button: of('button', 'tm-button'),
    field: of('field', 'tm-form-field'),
    select: of('select', 'tm-select'),
    fieldWithSelect: of('field-with-select', 'tm-form-field'),
    selectInField: of('select-in-field', 'tm-select'),
  };
}

describe('touch sizes below the sm breakpoint', () => {
  afterEach(async () => {
    await resizeTo(1280);
  });

  async function render(): Promise<ComponentFixture<Host>> {
    TestBed.configureTestingModule({ providers: [provideTellmaUi()] });
    const fixture = TestBed.createComponent(Host);
    await fixture.whenStable();
    return fixture;
  }

  it('renders unsized controls at the workspace default at 768 px', async () => {
    await resizeTo(768);
    const fixture = await render();
    expect(sizes(fixture)).toEqual({
      button: 'sm',
      field: 'sm',
      select: 'sm',
      fieldWithSelect: 'sm',
      // A select inside a field inherits the field's ladder: no class of its own.
      selectInField: 'none',
    });
  });

  it('renders unsized controls at lg at 390 px', async () => {
    await resizeTo(390);
    const fixture = await render();
    expect(sizes(fixture)).toEqual({
      button: 'lg',
      field: 'lg',
      select: 'lg',
      fieldWithSelect: 'lg',
      selectInField: 'none',
    });
  });

  it('lets an explicit size win at 390 px', async () => {
    await resizeTo(390);
    const fixture = await render();
    fixture.componentInstance.size.set('sm');
    await fixture.whenStable();
    const resolved = sizes(fixture);
    expect([resolved['button'], resolved['field'], resolved['select']]).toEqual(['sm', 'sm', 'sm']);
  });

  it('re-sizes unsized controls when the viewport crosses 576 px', async () => {
    await resizeTo(768);
    const fixture = await render();
    expect(sizes(fixture)['field']).toBe('sm');
    await resizeTo(390);
    await fixture.whenStable();
    expect(sizes(fixture)['field']).toBe('lg');
    expect(sizes(fixture)['button']).toBe('lg');
  });

  it('keeps a select editing a grid cell at the workspace default', async () => {
    await resizeTo(390);
    TestBed.configureTestingModule({
      providers: [
        provideTellmaUi(),
        { provide: TM_CELL_EDITOR_HOST, useValue: new SilentCellHost() },
      ],
    });
    const fixture = TestBed.createComponent(CellHost);
    await fixture.whenStable();
    const select = fixture.nativeElement.querySelector(
      '[data-testid="cell-select"]',
    ) as HTMLElement;
    expect(select.classList).toContain('tm-select--sm');
    expect(select.classList).not.toContain('tm-select--lg');
  });
});

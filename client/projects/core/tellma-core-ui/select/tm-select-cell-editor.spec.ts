// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { TestbedHarnessEnvironment } from '@angular/cdk/testing/testbed';

import type { TmCellEditor, TmCellEditorHost } from '@tellma/core-ui/contracts';
import { provideTellmaUi, TM_CELL_EDITOR_HOST } from '@tellma/core-ui';
import { TmSelectHarness } from '@tellma/core-ui-testing';

import { TmOption } from './tm-option';
import { TmSelect } from './tm-select';

/** Records what a grid cell would receive through TM_CELL_EDITOR_HOST. */
class RecordingCellHost implements TmCellEditorHost {
  editor: TmCellEditor<unknown> | null = null;
  register(editor: TmCellEditor<unknown>): void {
    this.editor = editor;
  }
}

interface Country {
  readonly id: number;
  readonly name: string;
}

const COUNTRIES: readonly Country[] = [
  { id: 1, name: 'Saudi Arabia' },
  { id: 2, name: 'United Arab Emirates' },
  { id: 3, name: 'Ethiopia' },
  { id: 4, name: 'Jordan' },
];

@Component({
  imports: [TmSelect, TmOption],
  template: `
    <tm-select [(value)]="value">
      @for (country of countries; track country.id) {
        <tm-option [value]="country.id" [label]="country.name">{{ country.name }}</tm-option>
      }
    </tm-select>
  `,
})
class Host {
  readonly value = signal<number | undefined>(undefined);
  readonly countries = COUNTRIES;
}

describe('tm-select as TmCellEditor (§6.3)', () => {
  async function setup() {
    const cellHost = new RecordingCellHost();
    TestBed.configureTestingModule({
      providers: [provideTellmaUi(), { provide: TM_CELL_EDITOR_HOST, useValue: cellHost }],
    });
    const fixture = TestBed.createComponent(Host);
    await fixture.whenStable();
    await new Promise((resolve) => setTimeout(resolve, 0));
    await fixture.whenStable();
    const loader = TestbedHarnessEnvironment.loader(fixture);
    const select = await loader.getHarness(TmSelectHarness);
    return {
      fixture,
      cellHost,
      select,
      instance: cellHost.editor as TmSelect<number>,
    };
  }

  it('registers itself with the provided TM_CELL_EDITOR_HOST on construction', async () => {
    const { cellHost } = await setup();
    expect(cellHost.editor).toBeInstanceOf(TmSelect);
  });

  it('text resolves the label of the current value, and empty string when empty', async () => {
    const { fixture, instance } = await setup();
    expect(instance.text()).toBe('');
    fixture.componentInstance.value.set(3);
    await fixture.whenStable();
    expect(instance.text()).toBe('Ethiopia');
  });

  it('open() opens the panel programmatically', async () => {
    const { fixture, instance, select } = await setup();
    expect(await select.isOpen()).toBe(false);
    instance.open();
    await fixture.whenStable();
    await new Promise((resolve) => setTimeout(resolve, 0));
    await fixture.whenStable();
    expect(await select.isOpen()).toBe(true);
  });

  it('seed opens the panel and moves the active option to the first typeahead match without committing', async () => {
    const { fixture, instance, select } = await setup();
    instance.seed('e');
    await fixture.whenStable();
    await new Promise((resolve) => setTimeout(resolve, 0));
    await fixture.whenStable();

    expect(await select.isOpen()).toBe(true);
    const options = await select.getOptions();
    expect(await options[2].isActive()).toBe(true); // Ethiopia
    // Seeding never mutates the model — committing stays an explicit activation.
    expect(fixture.componentInstance.value()).toBeUndefined();
  });

  it('cancel restores the value present at open (the grid Esc path)', async () => {
    const { fixture, instance, select } = await setup();
    fixture.componentInstance.value.set(1);
    await fixture.whenStable();

    await select.selectOption('Jordan');
    await fixture.whenStable();
    expect(fixture.componentInstance.value()).toBe(4);

    instance.cancel();
    await fixture.whenStable();
    expect(fixture.componentInstance.value()).toBe(1);
  });
});

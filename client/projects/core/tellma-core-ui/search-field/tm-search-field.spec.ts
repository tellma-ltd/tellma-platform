// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { Component, signal, viewChild } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { TestbedHarnessEnvironment } from '@angular/cdk/testing/testbed';
import { TestKey } from '@angular/cdk/testing';

import { provideTellmaUi } from '@tellma/core-ui';
import { TmSearchFieldHarness } from '@tellma/core-ui-testing';

import { TmSearchField } from './tm-search-field';

/** A search field bound by `[(value)]`, recording the emitted queries. */
@Component({
  imports: [TmSearchField],
  template: `
    <tm-search-field
      ariaLabel="Search customers"
      [(value)]="query"
      [invalid]="invalid()"
      [placeholder]="placeholder()"
      [debounce]="0"
      (search)="emitted.push($event)"
    />
  `,
})
class Host {
  readonly query = signal('');
  readonly invalid = signal(false);
  readonly placeholder = signal<string | undefined>(undefined);
  readonly emitted: string[] = [];
  readonly field = viewChild.required(TmSearchField);
}

async function setup() {
  TestBed.configureTestingModule({ providers: [provideTellmaUi()] });
  const fixture = TestBed.createComponent(Host);
  await fixture.whenStable();
  const loader = TestbedHarnessEnvironment.loader(fixture);
  return { fixture, search: await loader.getHarness(TmSearchFieldHarness) };
}

describe('tm-search-field', () => {
  it('names the input, defaults the placeholder, and keeps the clear slot while empty', async () => {
    const { fixture, search } = await setup();
    expect(await search.getAriaLabel()).toBe('Search customers');
    expect(await search.getPlaceholder()).toBe('Search');
    expect(await search.isClearVisible()).toBe(false);
    const clear = fixture.nativeElement.querySelector('.tm-search-field__clear') as HTMLElement;
    expect(clear.getBoundingClientRect().width).toBeGreaterThan(0);
    expect(clear.tabIndex).toBe(-1);
    fixture.componentInstance.placeholder.set('Find a customer');
    await fixture.whenStable();
    expect(await search.getPlaceholder()).toBe('Find a customer');
  });

  it('updates the value per keystroke and emits the query', async () => {
    const { fixture, search } = await setup();
    await search.setValue('jed');
    expect(fixture.componentInstance.query()).toBe('jed');
    expect(fixture.componentInstance.emitted.at(-1)).toBe('jed');
    expect(await search.isClearVisible()).toBe(true);
  });

  it('clears, emits and refocuses from the clear button, which does not move the text', async () => {
    const { fixture, search } = await setup();
    await search.setValue('jed');
    const input = fixture.nativeElement.querySelector('input') as HTMLInputElement;
    const textEdge = input.getBoundingClientRect().right;
    await search.clear();
    expect(await search.getValue()).toBe('');
    expect(fixture.componentInstance.query()).toBe('');
    expect(fixture.componentInstance.emitted.at(-1)).toBe('');
    expect(await search.isFocused()).toBe(true);
    expect(await search.isClearVisible()).toBe(false);
    expect(input.getBoundingClientRect().right).toBe(textEdge);
  });

  it('clears on Escape', async () => {
    const { fixture, search } = await setup();
    await search.setValue('jed');
    await search.press(TestKey.ESCAPE);
    expect(fixture.componentInstance.query()).toBe('');
  });

  it('marks an invalid query at once', async () => {
    const { fixture, search } = await setup();
    expect(await search.isInvalid()).toBe(false);
    fixture.componentInstance.invalid.set(true);
    await fixture.whenStable();
    expect(await search.isInvalid()).toBe(true);
    const host = fixture.nativeElement.querySelector('tm-search-field') as HTMLElement;
    expect(host.classList).toContain('tm-search-field--invalid');
  });

  it('focuses and selects on request', async () => {
    const { fixture, search } = await setup();
    await search.setValue('jed');
    (document.activeElement as HTMLElement | null)?.blur();
    fixture.componentInstance.field().focus({ select: true });
    const input = fixture.nativeElement.querySelector('input') as HTMLInputElement;
    expect(document.activeElement).toBe(input);
    expect([input.selectionStart, input.selectionEnd]).toEqual([0, 3]);
  });
});

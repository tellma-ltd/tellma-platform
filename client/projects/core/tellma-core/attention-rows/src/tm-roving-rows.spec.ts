// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { Directionality } from '@angular/cdk/bidi';
import { ChangeDetectionStrategy, Component, signal } from '@angular/core';
import { type ComponentFixture, TestBed } from '@angular/core/testing';

import { TmRovingRows } from './tm-roving-rows';

// The attention lists' roving rows: one tab stop, the arrows, Home and End
// between rows, Left and Right between a row's controls (mirrored in a
// right-to-left page), Delete handed to the owner, and focus kept in the
// list when the focused row leaves, wherever rows inserted meanwhile.

/** A list of rows with a link and a Clear each. */
@Component({
  imports: [TmRovingRows],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <h2 #heading tabindex="-1">Rows</h2>
    <ul tmRovingRows [fallback]="heading" (rowDelete)="deleted($event)">
      @for (id of ids(); track id) {
        <li data-tm-row [attr.data-tm-id]="id">
          <a data-tm-row-control [attr.href]="'#' + id">Row {{ id }}</a>
          <button type="button" data-tm-row-control>Clear {{ id }}</button>
        </li>
      }
    </ul>
  `,
})
class Host {
  /** The rows' ids. */
  readonly ids = signal([1, 2, 3]);
  /** The ids whose Delete key was pressed. */
  readonly deletes: string[] = [];

  /** Records a row's Delete key. */
  deleted(row: HTMLElement): void {
    this.deletes.push(row.dataset['tmId'] ?? '');
  }
}

/** Renders the host in a direction. */
async function setup(dir: 'ltr' | 'rtl' = 'ltr'): Promise<ComponentFixture<Host>> {
  TestBed.configureTestingModule({ providers: [{ provide: Directionality, useValue: { value: dir, change: { subscribe: () => ({}) } } }] });
  const fixture = TestBed.createComponent(Host);
  document.body.appendChild(fixture.nativeElement as HTMLElement);
  await fixture.whenStable();
  return fixture;
}

/** A control by its text. */
function control(fixture: ComponentFixture<Host>, text: string): HTMLElement {
  return [...(fixture.nativeElement as HTMLElement).querySelectorAll<HTMLElement>('[data-tm-row-control]')].find(
    (candidate) => candidate.textContent?.trim() === text,
  )!;
}

/** Presses a key on the focused element. */
function press(key: string): void {
  document.activeElement?.dispatchEvent(new KeyboardEvent('keydown', { key, bubbles: true, cancelable: true }));
}

/** The controls that take Tab. */
function stops(fixture: ComponentFixture<Host>): string[] {
  return [...(fixture.nativeElement as HTMLElement).querySelectorAll<HTMLElement>('[data-tm-row-control]')]
    .filter((candidate) => candidate.tabIndex === 0)
    .map((candidate) => candidate.textContent?.trim() ?? '');
}

describe('tmRovingRows', () => {
  afterEach(() => TestBed.resetTestingModule());

  it('is one tab stop, the first row first, then wherever focus last rested', async () => {
    const fixture = await setup();
    expect(stops(fixture)).toEqual(['Row 1']);
    control(fixture, 'Clear 2').focus();
    expect(stops(fixture)).toEqual(['Clear 2']);
  });

  it('moves between rows by the arrows, Home and End, stopping at the ends', async () => {
    const fixture = await setup();
    control(fixture, 'Row 1').focus();
    press('ArrowDown');
    expect(document.activeElement?.textContent?.trim()).toBe('Row 2');
    press('End');
    expect(document.activeElement?.textContent?.trim()).toBe('Row 3');
    press('ArrowDown');
    expect(document.activeElement?.textContent?.trim()).toBe('Row 3');
    press('Home');
    expect(document.activeElement?.textContent?.trim()).toBe('Row 1');
    press('ArrowUp');
    expect(document.activeElement?.textContent?.trim()).toBe('Row 1');
  });

  it("moves between a row's controls by Left and Right, mirrored right to left", async () => {
    const ltr = await setup();
    control(ltr, 'Row 2').focus();
    press('ArrowRight');
    expect(document.activeElement?.textContent?.trim()).toBe('Clear 2');
    press('ArrowLeft');
    expect(document.activeElement?.textContent?.trim()).toBe('Row 2');
    TestBed.resetTestingModule();

    const rtl = await setup('rtl');
    control(rtl, 'Row 2').focus();
    press('ArrowLeft');
    expect(document.activeElement?.textContent?.trim()).toBe('Clear 2');
  });

  it("hands a row's Delete to its owner, and keeps focus in the list as the row leaves", async () => {
    const fixture = await setup();
    control(fixture, 'Row 2').focus();
    press('Delete');
    expect(fixture.componentInstance.deletes).toEqual(['2']);

    fixture.componentInstance.ids.set([1, 3]);
    await fixture.whenStable();
    expect(document.activeElement?.textContent?.trim()).toBe('Row 3');

    // The last row leaving moves focus to the fallback.
    fixture.componentInstance.ids.set([]);
    await fixture.whenStable();
    expect(document.activeElement?.tagName).toBe('H2');
  });

  it('moves focus to the row in its place when rows inserted above the focused row before it left', async () => {
    const fixture = await setup();
    fixture.componentInstance.ids.set([1, 2, 3, 4]);
    await fixture.whenStable();
    control(fixture, 'Row 3').focus();

    // A row arrives at the top, then the focused row leaves: focus moves to the row now in its place.
    fixture.componentInstance.ids.set([5, 1, 2, 3, 4]);
    await fixture.whenStable();
    fixture.componentInstance.ids.set([5, 1, 2, 4]);
    await fixture.whenStable();
    expect(document.activeElement?.textContent?.trim()).toBe('Row 4');
  });
});

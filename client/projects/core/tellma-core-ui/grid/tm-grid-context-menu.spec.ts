// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { Component, signal } from '@angular/core';
import { TestBed, type ComponentFixture } from '@angular/core/testing';

import { provideTellmaUi } from '@tellma/core-ui';

import { ɵTM_GRID_DEFERRED, type ɵTmGridDeferredLoader, type ɵTmGridDeferredParts } from './internal/grid-view';
import { TmGrid } from './tm-grid';
import { TmGridColumn } from './tm-grid-column';

// The context menu comes with the grid's deferred chunk, which loads after
// the first render: a right-click before it arrives opens the menu once it does, a failed load drops that
// open so the next right-click loads again, and an open the user walked away
// from (Escape, a press elsewhere, focus leaving the grid) never shows.

/** A spec row. */
interface Row {
  /** The row's id. */
  readonly id: number;
  /** Its name. */
  readonly name: string;
}

/** A plain grid of a few rows. */
@Component({
  imports: [TmGrid, TmGridColumn],
  template: `
    <tm-grid gridId="spec-grid-menu" [data]="rows()" [rowId]="rowId" style="block-size: 300px">
      <tm-grid-column key="name" header="Name" [width]="160" />
    </tm-grid>
    <button type="button" class="outside">Outside</button>
  `,
})
class MenuHost {
  /** The rows. */
  readonly rows = signal<readonly Row[]>(Array.from({ length: 5 }, (_, id) => ({ id, name: `Item ${id}` })));
  /** Reads a row's id. */
  readonly rowId = (row: Row): number => row.id;
}

/** One load of the deferred chunk, which the test settles. */
interface Load {
  /** Settles the load with the deferred parts. */
  readonly resolve: (parts: ɵTmGridDeferredParts) => void;
  /** Fails the load. */
  readonly reject: (error: unknown) => void;
}

/**
 * Lets the fixture settle, a macrotask included.
 *
 * @param fixture - The fixture.
 */
async function stable(fixture: ComponentFixture<unknown>): Promise<void> {
  await fixture.whenStable();
  await new Promise((resolve) => setTimeout(resolve, 0));
  await fixture.whenStable();
}

/**
 * Lets the view's effects and renders run while a load the test holds keeps
 * the fixture from settling.
 */
function tick(): Promise<void> {
  return new Promise((resolve) => setTimeout(resolve, 20));
}

/** Resolves once the browser is idle, after the view's own idle request. */
function idle(): Promise<void> {
  return new Promise((resolve) => requestIdleCallback(() => resolve()));
}

/**
 * The deferred chunk's real parts.
 *
 * @returns The parts.
 */
async function realParts(): Promise<ɵTmGridDeferredParts> {
  const module = await import('./internal/grid-deferred');
  return { contextMenu: module.ɵTmGridContextMenu, touchHandles: module.ɵTmGridTouchHandles };
}

/**
 * Mounts the host with a loader whose loads the test settles, and waits until
 * the view's idle load has asked for the chunk (a load in flight keeps the
 * fixture unstable, as a deferred block's does).
 *
 * @returns The fixture, the scroller, a data cell and the loads asked for.
 */
async function setup(): Promise<{
  fixture: ComponentFixture<MenuHost>;
  scroller: HTMLElement;
  cell: HTMLElement;
  loads: Load[];
}> {
  const loads: Load[] = [];
  const loader: ɵTmGridDeferredLoader = () => new Promise((resolve, reject) => loads.push({ resolve, reject }));
  TestBed.configureTestingModule({ providers: [provideTellmaUi(), { provide: ɵTM_GRID_DEFERRED, useValue: loader }] });
  const fixture = TestBed.createComponent(MenuHost);
  await stable(fixture);
  await idle();
  await tick();
  const scroller = (fixture.nativeElement as HTMLElement).querySelector('.tm-grid__scroller') as HTMLElement;
  const cell = scroller.querySelector('[data-tm-cell]') as HTMLElement;
  return { fixture, scroller, cell, loads };
}

/**
 * A right-click on an element.
 *
 * @param target - The element.
 * @returns The event, to read whether the grid prevented it.
 */
function rightClick(target: Element): MouseEvent {
  const rect = target.getBoundingClientRect();
  const event = new MouseEvent('contextmenu', { bubbles: true, cancelable: true, clientX: rect.left + 2, clientY: rect.top + 2 });
  target.dispatchEvent(event);
  return event;
}

/** Whether a menu panel is open. */
function menuOpen(): boolean {
  return document.querySelector('.tm-menu__panel') !== null;
}

describe('tm-grid context menu loading', () => {
  it('asks for the chunk once the browser is idle, and opens on a right-click made before it arrives', async () => {
    const { fixture, cell, loads } = await setup();
    expect(loads.length).toBe(1);

    expect(rightClick(cell).defaultPrevented).toBe(true);
    await tick();
    expect(menuOpen()).toBe(false);
    expect(loads.length).toBe(1);

    loads[0]!.resolve(await realParts());
    await stable(fixture);
    expect(menuOpen()).toBe(true);
  });

  it('drops the open when the chunk fails, and loads again on the next right-click', async () => {
    const { fixture, cell, loads } = await setup();
    rightClick(cell);
    loads[0]!.reject(new TypeError('Failed to fetch dynamically imported module'));
    await stable(fixture);
    expect(menuOpen()).toBe(false);

    // The next right-click is the grid's again: it asks for the chunk anew and opens on arrival.
    expect(rightClick(cell).defaultPrevented).toBe(true);
    await tick();
    expect(loads.length).toBe(2);
    loads[1]!.resolve(await realParts());
    await stable(fixture);
    expect(menuOpen()).toBe(true);
  });

  const cancels: readonly (readonly [string, (scroller: HTMLElement, outside: HTMLElement) => void])[] = [
    ['Escape', (scroller) => scroller.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true, cancelable: true }))],
    ['a press elsewhere', (_, outside) => outside.dispatchEvent(new PointerEvent('pointerdown', { bubbles: true, cancelable: true }))],
    ['focus leaving the grid', (scroller, outside) => scroller.dispatchEvent(new FocusEvent('focusout', { bubbles: true, relatedTarget: outside }))],
  ];
  for (const [name, cancel] of cancels) {
    it(`never opens a waiting menu after ${name}`, async () => {
      const { fixture, scroller, cell, loads } = await setup();
      const outside = (fixture.nativeElement as HTMLElement).querySelector('.outside') as HTMLElement;
      rightClick(cell);
      cancel(scroller, outside);
      loads[0]!.resolve(await realParts());
      await stable(fixture);
      expect(menuOpen()).toBe(false);

      // The menu is here now, so the next right-click opens it at once.
      rightClick(cell);
      await stable(fixture);
      expect(menuOpen()).toBe(true);
    });
  }

  describe('on a coarse pointer', () => {
    beforeEach(() => {
      const real = window.matchMedia.bind(window);
      vi.spyOn(window, 'matchMedia').mockImplementation((query: string) =>
        query === '(pointer: coarse)'
          ? ({ matches: true, media: query, addEventListener: () => undefined, removeEventListener: () => undefined } as unknown as MediaQueryList)
          : real(query),
      );
    });
    afterEach(() => vi.restoreAllMocks());

    it('asks for the chunk at once, and mounts the selection handles from it', async () => {
      const loads: Load[] = [];
      const loader: ɵTmGridDeferredLoader = () => new Promise((resolve, reject) => loads.push({ resolve, reject }));
      TestBed.configureTestingModule({ providers: [provideTellmaUi(), { provide: ɵTM_GRID_DEFERRED, useValue: loader }] });
      const fixture = TestBed.createComponent(MenuHost);
      // No idle wait: the first render's effects ask for the chunk.
      await tick();
      expect(loads.length).toBe(1);
      const host = fixture.nativeElement as HTMLElement;
      expect(host.querySelector('tm-grid-touch-handles')).toBeNull();

      loads[0]!.resolve(await realParts());
      await stable(fixture);
      expect(host.querySelector('.tm-grid__scroller tm-grid-touch-handles')).not.toBeNull();
    });
  });
});

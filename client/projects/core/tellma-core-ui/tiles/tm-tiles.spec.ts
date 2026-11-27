// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { LiveAnnouncer } from '@angular/cdk/a11y';
import { Dir } from '@angular/cdk/bidi';
import type { ModifierKeys } from '@angular/cdk/testing';
import { TestbedHarnessEnvironment } from '@angular/cdk/testing/testbed';
import { Component, signal, viewChild } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { By } from '@angular/platform-browser';

import { provideTellmaUi } from '@tellma/core-ui';
import type { TmRowId } from '@tellma/core-ui/contracts';
import type { TmGridRowActivation } from '@tellma/core-ui/grid';
import { TM_BLOB_FETCHER, TmImage } from '@tellma/core-ui/image';
import { tmIsApplePlatform } from '@tellma/core-ui/keys';
import { TmTilesTree } from '@tellma/core-ui/tiles-tree';
import { tmEmitCss, tmTokensDefault } from '@tellma/core-ui-tokens';
import { TmTilesHarness } from '@tellma/core-ui-testing';

import { TmTiles } from './tm-tiles';
import { TmTileContent, TmTilesEmpty } from './tm-tiles-templates';
import type { TmTileLayout } from './tm-tiles-types';

/** A row of the test tiles. */
interface Product {
  readonly id: number;
  readonly name: string;
  readonly price: number;
  readonly active: boolean;
  readonly parentId?: number | null;
  readonly lazy?: boolean;
}

/** `count` flat products, ids from 1. */
function products(count: number): Product[] {
  return Array.from({ length: count }, (_, i) => ({
    id: i + 1,
    name: `Product ${i + 1}`,
    price: (i + 1) * 10,
    active: i % 2 === 0,
  }));
}

/** A promise and the functions that settle it. */
function deferred(): { promise: Promise<void>; resolve: () => void; reject: () => void } {
  let resolve!: () => void;
  let reject!: () => void;
  const promise = new Promise<void>((res, rej) => {
    resolve = res;
    reject = rej;
  });
  return { promise, resolve, reject };
}

/** The platform's Mod key, as harness modifiers. */
const MOD: ModifierKeys = tmIsApplePlatform() ? { meta: true } : { control: true };

// The host applies tree mode (TmTilesTree) to every tiles it renders: with
// no parent reader bound the tiles stay flat, so the same host exercises
// both modes, and the flat tests prove the tree adds nothing until it binds.
@Component({
  imports: [Dir, TmTiles, TmTilesTree, TmTileContent, TmTilesEmpty],
  template: `
    <div [dir]="dir()" [style.inline-size.px]="width()" style="block-size: 420px">
      <tm-tiles
        style="block-size: 100%"
        ariaLabel="Products"
        [data]="rows()"
        [rowId]="idOf"
        [layout]="layout"
        [loading]="loading()"
        [selectable]="selectable()"
        [(selectedIds)]="selected"
        [(activeRowId)]="active"
        [rowClass]="rowClass"
        [parentId]="parentId()"
        [hasChildren]="hasChildren()"
        [loadChildren]="loadChildren()"
        [(expandedIds)]="expanded"
        (rowActivate)="activations.push($event)"
      >
        @if (customEmpty()) {
          <p *tmTilesEmpty class="custom-empty">Nothing here</p>
        }
        @if (customBody()) {
          <ng-template tmTileContent [tmTileContentOf]="rows()" let-row>
            <span class="custom-body">{{ row.name }}!</span>
          </ng-template>
        }
      </tm-tiles>
    </div>
  `,
})
class Host {
  readonly tiles = viewChild.required<TmTiles<Product>>(TmTiles);
  readonly dir = signal<'ltr' | 'rtl'>('ltr');
  readonly width = signal(1000);
  readonly rows = signal<readonly Product[]>(products(10));
  readonly loading = signal(false);
  readonly selectable = signal(false);
  readonly selected = signal<ReadonlySet<TmRowId>>(new Set());
  readonly active = signal<TmRowId | null>(null);
  readonly parentId = signal<((row: Product) => TmRowId | null) | undefined>(undefined);
  readonly hasChildren = signal<((row: Product) => boolean) | undefined>(undefined);
  readonly loadChildren = signal<((rows: readonly Product[]) => Promise<void>) | undefined>(
    undefined,
  );
  readonly expanded = signal<ReadonlySet<TmRowId>>(new Set());
  readonly customEmpty = signal(false);
  readonly customBody = signal(false);
  readonly activations: TmGridRowActivation<Product>[] = [];
  /** The active status label: a signal the layout reads, as a translated label is. */
  readonly activeLabel = signal('Active');
  /** Whether every row has a photo (`/img/<id>`), or none has. */
  readonly photos = signal(false);
  readonly idOf = (row: Product): number => row.id;
  readonly rowClass = (row: Product): string | null =>
    row.active ? null : 'tm-tiles__item--context';
  readonly layout: TmTileLayout<Product> = {
    image: (row) => ({ src: this.photos() ? `/img/${row.id}` : null, alt: row.name }),
    title: (row) => row.name,
    href: (row) => (row.id === 1 ? '/products/1' : null),
    topEnd: (row) => ({ text: String(row.price), numeric: true }),
    bottomStart: (row) => `P-${row.id}`,
    bottomEnd: (row) => ({ text: row.active ? this.activeLabel() : 'Inactive', badge: 'success' }),
  };

  /** Switches the host to tree mode over parent ids and lazy markers. */
  tree(load?: (rows: readonly Product[]) => Promise<void>): void {
    this.parentId.set((row) => row.parentId ?? null);
    this.hasChildren.set((row) => row.lazy === true);
    this.loadChildren.set(load);
  }
}

/** The unit page loads no token stylesheet; the geometry reads the tokens. */
let tokenSheet: HTMLStyleElement;

beforeAll(() => {
  tokenSheet = document.createElement('style');
  tokenSheet.textContent = tmEmitCss(tmTokensDefault);
  document.head.appendChild(tokenSheet);
});

afterAll(() => tokenSheet.remove());

afterEach(() => vi.useRealTimers());

/** Lets a settled load's promise chain run, then waits for the render it causes. */
async function settled(fixture: { whenStable(): Promise<unknown> }): Promise<void> {
  await new Promise((resolve) => setTimeout(resolve));
  await fixture.whenStable();
}

/** Whether a tile's box lies inside the scroller's viewport. */
function inView(scroller: HTMLElement, tile: Element): boolean {
  const viewport = scroller.getBoundingClientRect();
  const box = tile.getBoundingClientRect();
  return box.top >= viewport.top && box.bottom <= viewport.bottom;
}

/** The rendered tile at a visible index. */
function tileAt(element: Element, index: number): HTMLElement {
  return element.querySelector<HTMLElement>(`.tm-tiles__item[data-index="${index}"]`)!;
}

/** Scrolls the scroller to an offset and waits for the renders it causes. */
async function scrollTo(
  fixture: { whenStable(): Promise<unknown> },
  scroller: HTMLElement,
  top: number,
): Promise<void> {
  scroller.scrollTop = top;
  scroller.dispatchEvent(new Event('scroll'));
  await fixture.whenStable();
}

/** The rendered tiles, in DOM order. */
function renderedTiles(element: Element): HTMLElement[] {
  return [...element.querySelectorAll<HTMLElement>('.tm-tiles__item[data-index]')];
}

/** The row a rendered tile shows, read from its title. */
function shownTitle(tile: Element): string {
  return tile.querySelector('.tm-tiles__title')!.textContent!.trim();
}

/**
 * Checks that every rendered tile states its own row and nothing else: the
 * element id, the line and column, the title, the corners, the checkbox,
 * the selection, the tab stop and the state classes. A tile element that
 * was reused for another row and kept anything of its last one fails here.
 */
function expectTilesTrueToTheirRows(element: Element, host: Host): void {
  const tiles = host.tiles();
  const prefix = tiles.controlsId().replace(/-grid$/, '');
  const rows = host.rows();
  const columns = Number(element.querySelector('[role="grid"]')!.getAttribute('aria-colcount'));
  const activeIndex = rows.findIndex((row) => row.id === host.active());
  const ids = new Set<string>();
  for (const tile of renderedTiles(element)) {
    const index = Number(tile.dataset['index']);
    const row = rows[index];
    const selected = host.selected().has(row.id);
    ids.add(tile.id);
    expect(tile.id).toBe(`${prefix}-t${index}`);
    expect(tile.getAttribute('aria-colindex')).toBe(String((index % columns) + 1));
    expect(tile.closest('[role="row"]')!.getAttribute('aria-rowindex')).toBe(
      String(Math.floor(index / columns) + 1),
    );
    expect(shownTitle(tile)).toBe(row.name);
    expect(tile.querySelector('.tm-tiles__corner--bottom-start')!.textContent!.trim()).toBe(
      `P-${row.id}`,
    );
    expect(tile.querySelector('.tm-tiles__corner--top-end')!.textContent!.trim()).toBe(
      String(row.price),
    );
    expect(tile.classList.contains('tm-tiles__item--context')).toBe(!row.active);
    expect(tile.classList.contains('tm-tiles__item--selected')).toBe(selected);
    expect(tile.classList.contains('tm-tiles__item--active')).toBe(index === activeIndex);
    expect(tile.getAttribute('tabindex')).toBe(
      index === (activeIndex === -1 ? null : activeIndex) ? '0' : '-1',
    );
    if (host.selectable()) {
      const check = tile.querySelector('[role="checkbox"]')!;
      expect(tile.getAttribute('aria-selected')).toBe(String(selected));
      expect(check.getAttribute('aria-checked')).toBe(String(selected));
      expect(check.getAttribute('aria-label')).toBe(`Select ${row.name}`);
      expect(check.querySelector('tm-icon') !== null).toBe(selected);
    }
  }
  // No two rendered tiles share an id, so the active descendant is unique.
  expect(ids.size).toBe(renderedTiles(element).length);
}

async function setup(configure: (host: Host) => void = () => undefined) {
  TestBed.configureTestingModule({
    providers: [
      provideTellmaUi(),
      // Photos never arrive: the tests watch which tm-image shows which row.
      { provide: TM_BLOB_FETCHER, useValue: () => new Promise<never>(() => undefined) },
    ],
  });
  const fixture = TestBed.createComponent(Host);
  configure(fixture.componentInstance);
  fixture.autoDetectChanges();
  await fixture.whenStable();
  const tiles = await TestbedHarnessEnvironment.loader(fixture).getHarness(TmTilesHarness);
  const element = (fixture.nativeElement as HTMLElement).querySelector('tm-tiles')!;
  const scroller = element.querySelector<HTMLElement>('.tm-tiles__scroller')!;
  const announce = vi.spyOn(TestBed.inject(LiveAnnouncer), 'announce');
  return { fixture, host: fixture.componentInstance, tiles, element, scroller, announce };
}

describe('tm-tiles', () => {
  describe('the lattice', () => {
    it('lays 1,000px out in four 60px columns on a 70px pitch, flowing from inline-start', async () => {
      const { tiles, element } = await setup();
      expect(await tiles.getLayout()).toBe('lattice');
      expect(await tiles.getColumnCount()).toBe(4);
      expect(await tiles.getLineCount()).toBe(3);
      const items = element.querySelectorAll<HTMLElement>('.tm-tiles__item');
      const first = items[0].getBoundingClientRect();
      const fifth = items[4].getBoundingClientRect();
      expect(first.height).toBe(60);
      expect(fifth.top - first.top).toBe(70);
      expect(items[1].getBoundingClientRect().left).toBeGreaterThan(first.left);
    });

    it('starts the lattice at the right edge in a right-to-left page', async () => {
      const { element } = await setup((host) => host.dir.set('rtl'));
      const items = element.querySelectorAll<HTMLElement>('.tm-tiles__item');
      expect(items[1].getBoundingClientRect().right).toBeLessThan(
        items[0].getBoundingClientRect().left,
      );
    });

    it('stacks a narrow list in one column of dividers at a 60px pitch, recomputed on resize', async () => {
      const { fixture, host, tiles, element } = await setup((h) => h.width.set(400));
      expect(await tiles.getLayout()).toBe('list');
      expect(await tiles.getColumnCount()).toBe(1);
      const items = element.querySelectorAll<HTMLElement>('.tm-tiles__item');
      expect(items[1].getBoundingClientRect().top - items[0].getBoundingClientRect().top).toBe(60);
      host.width.set(1000);
      // The resize observer reports the new width after the next layout.
      await new Promise((resolve) => requestAnimationFrame(() => setTimeout(resolve)));
      await fixture.whenStable();
      expect(await tiles.getColumnCount()).toBe(4);
    });

    it('sizes the image box from the token the component passes to tm-image', async () => {
      const { element } = await setup();
      expect(getComputedStyle(element).getPropertyValue('--tiles-image-size').trim()).toBe('40px');
      const image = element.querySelector('.tm-tiles__image')!.getBoundingClientRect();
      expect([image.width, image.height]).toEqual([40, 40]);
    });

    it('renders only the windowed lines of 10,000 rows, plus the active tile', async () => {
      const { fixture, host, tiles, element, scroller } = await setup((h) =>
        h.rows.set(products(10_000)),
      );
      // 420px shows six 70px lines, seven in part; three more of overscan below.
      expect(await tiles.getLineCount()).toBe(2500);
      expect(element.querySelectorAll('.tm-tiles__item')).toHaveLength(10 * 4);
      host.active.set(1);
      await fixture.whenStable();
      scroller.scrollTop = 100 * 70;
      scroller.dispatchEvent(new Event('scroll'));
      await fixture.whenStable();
      // Lines 97–109 around the scroll position, and line 0 for the active tile.
      const lines = [...element.querySelectorAll('.tm-tiles__line')].map((line) =>
        Number(line.getAttribute('aria-rowindex')),
      );
      expect(lines).toEqual([1, ...Array.from({ length: 13 }, (_, i) => 98 + i)]);
      expect(element.querySelector('#' + host.tiles().activeDescendantId()!)).not.toBeNull();
    });
  });

  describe('a tile', () => {
    it('shows the title, the corners, a badge and the numeric corner left-to-right', async () => {
      const { tiles, element } = await setup((host) => host.dir.set('rtl'));
      const tile = await tiles.getTile({ index: 0 });
      expect(await tile.getTitle()).toBe('Product 1');
      expect(await tile.getCornerText('topEnd')).toBe('10');
      expect(await tile.getCornerText('bottomStart')).toBe('P-1');
      expect(await tile.getCornerText('bottomEnd')).toBe('Active');
      expect(await tile.hasCornerBadge('bottomEnd')).toBe(true);
      expect(await tile.hasCornerBadge('topEnd')).toBe(false);
      const numeric = element.querySelector<HTMLElement>('.tm-tiles__corner--top-end')!;
      expect(numeric.getAttribute('dir')).toBe('ltr');
      expect(getComputedStyle(numeric).fontVariantNumeric).toBe('tabular-nums');
      expect(getComputedStyle(numeric).textAlign).toBe('right');
    });

    it('links the title out of the tab order, and marks rowClass rows', async () => {
      const { tiles, element } = await setup();
      expect(await (await tiles.getTile({ index: 0 })).getHref()).toBe('/products/1');
      expect(await (await tiles.getTile({ index: 1 })).getHref()).toBeNull();
      expect(element.querySelector('a.tm-tiles__title[href]')!.getAttribute('tabindex')).toBe('-1');
      // A row without an href has no link: its title takes no focus.
      const plain = tileAt(element, 1).querySelector('.tm-tiles__title')!;
      expect([plain.hasAttribute('href'), plain.hasAttribute('tabindex')]).toEqual([false, false]);
      expect(await (await tiles.getTile({ index: 1 })).hasClass('tm-tiles__item--context')).toBe(
        true,
      );
    });

    it('sizes the title link to its text, leaving the space beside it to the tile', async () => {
      const { fixture, host, element } = await setup();
      const link = (): HTMLElement => element.querySelector('a.tm-tiles__title[href]')!;
      const text = link().closest<HTMLElement>('.tm-tiles__text')!;
      // A short title: the link ends where its text does, well short of the
      // title's column, so a press beside it is the tile's, not the link's.
      const range = document.createRange();
      range.selectNodeContents(link());
      const textWidth = range.getBoundingClientRect().width;
      expect(link().getBoundingClientRect().width).toBeLessThanOrEqual(textWidth + 1);
      expect(link().getBoundingClientRect().width).toBeLessThan(
        text.getBoundingClientRect().width / 2,
      );
      // A long title still ends at its column, cut short with an ellipsis.
      host.rows.update(([first, ...rest]) => [{ ...first!, name: 'Product '.repeat(40) }, ...rest]);
      await fixture.whenStable();
      expect(link().scrollWidth).toBeGreaterThan(link().clientWidth);
      expect(link().getBoundingClientRect().right).toBeLessThanOrEqual(
        text.getBoundingClientRect().right,
      );
    });

    it('re-reads a row when a signal its layout reads changes', async () => {
      const { fixture, host, tiles } = await setup();
      const tile = await tiles.getTile({ index: 0 });
      expect(await tile.getCornerText('bottomEnd')).toBe('Active');
      host.activeLabel.set('نشط');
      await fixture.whenStable();
      expect(await tile.getCornerText('bottomEnd')).toBe('نشط');
    });

    it('renders a custom body in place of the corners', async () => {
      const { element } = await setup((host) => host.customBody.set(true));
      expect(element.querySelector('.custom-body')!.textContent).toBe('Product 1!');
      expect(element.querySelector('.tm-tiles__corner')).toBeNull();
    });
  });

  describe('selection', () => {
    it('toggles with the checkbox, keeps unknown ids, and announces the count', async () => {
      const { host, tiles, announce } = await setup((h) => {
        h.selectable.set(true);
        h.selected.set(new Set([99]));
      });
      const tile = await tiles.getTile({ index: 1 });
      expect(await tile.getCheckboxLabel()).toBe('Select Product 2');
      const before = host.selected();
      await tile.toggleSelected();
      expect(host.selected()).not.toBe(before);
      expect(host.selected()).toEqual(new Set([99, 2]));
      expect(await tile.isSelected()).toBe(true);
      expect(host.activations).toEqual([]);
      expect(announce).toHaveBeenLastCalledWith('1 of 10 selected', 'polite');
      await tile.toggleSelected();
      expect(host.selected()).toEqual(new Set([99]));
    });

    it('toggles the active tile with Space and every loaded tile with Ctrl+Shift+Space', async () => {
      const { host, tiles, announce } = await setup((h) => h.selectable.set(true));
      await tiles.press('Space');
      expect(host.selected()).toEqual(new Set([1]));
      await tiles.press('Space', { control: true, shift: true });
      expect(host.selected().size).toBe(10);
      expect(announce).toHaveBeenLastCalledWith('10 of 10 selected', 'polite');
      await tiles.press('Space', { control: true, shift: true });
      expect(host.selected()).toEqual(new Set());
    });

    it('ignores Space when the tiles are not selectable', async () => {
      const { host, tiles } = await setup();
      await tiles.press('Space');
      expect(host.selected()).toEqual(new Set());
    });
  });

  describe('activation', () => {
    it('opens a clicked tile, Mod-clicked and middle-clicked in a new tab', async () => {
      const { host, tiles, element } = await setup();
      vi.useFakeTimers({ toFake: ['Date'] });
      vi.setSystemTime(1_000_000);
      await (await tiles.getTile({ index: 1 })).click();
      vi.setSystemTime(1_001_000);
      await (await tiles.getTile({ index: 1 })).click(MOD);
      vi.setSystemTime(1_002_000);
      // A middle press keeps autoscroll off; its auxclick opens the tile.
      const body = element.querySelectorAll('.tm-tiles__item')[1].querySelector('.tm-tiles__text')!;
      const press = new PointerEvent('pointerdown', { bubbles: true, cancelable: true, button: 1 });
      body.dispatchEvent(press);
      expect(press.defaultPrevented).toBe(true);
      body.dispatchEvent(new MouseEvent('auxclick', { bubbles: true, button: 1 }));
      expect(host.activations.map((a) => [a.rowId, a.via, a.newTab])).toEqual([
        [2, 'pointer', false],
        [2, 'pointer', true],
        [2, 'pointer', true],
      ]);
      expect(host.activations[0].row).toBe(host.rows()[1]);
    });

    it('ignores a second activation of the same tile within 400ms', async () => {
      const { host, tiles } = await setup();
      vi.useFakeTimers({ toFake: ['Date'] });
      vi.setSystemTime(1_000_000);
      const tile = await tiles.getTile({ index: 2 });
      await tile.click();
      vi.setSystemTime(1_000_399);
      await tile.click();
      expect(host.activations).toHaveLength(1);
      vi.setSystemTime(1_000_400);
      await tile.press('Enter');
      expect(host.activations.map((a) => a.via)).toEqual(['pointer', 'keyboard']);
    });

    it('opens the active tile with Enter, in a new tab with Mod+Enter', async () => {
      const { host, tiles } = await setup();
      vi.useFakeTimers({ toFake: ['Date'] });
      vi.setSystemTime(1_000_000);
      await tiles.press('Enter');
      vi.setSystemTime(1_001_000);
      await tiles.press('Enter', MOD);
      expect(host.activations.map((a) => [a.rowId, a.via, a.newTab])).toEqual([
        [1, 'keyboard', false],
        [1, 'keyboard', true],
      ]);
    });

    it('leaves a Mod-click on the title link to the browser', async () => {
      const { host, element } = await setup();
      const link = element.querySelector<HTMLAnchorElement>('a.tm-tiles__title[href]')!;
      const click = new MouseEvent('click', {
        bubbles: true,
        cancelable: true,
        ctrlKey: !tmIsApplePlatform(),
        metaKey: tmIsApplePlatform(),
      });
      // Stop the navigation itself after the tiles have seen the click.
      element.addEventListener('click', (event) => event.preventDefault(), { once: true });
      link.dispatchEvent(click);
      expect(host.activations).toEqual([]);
    });
  });

  describe('pointer rules', () => {
    it('opens the tile in place on a plain click on its title link', async () => {
      const { host, element } = await setup();
      const link = element.querySelector<HTMLAnchorElement>('a.tm-tiles__title[href]')!;
      // Read what the tiles did, then stop any navigation they let through.
      let prevented = false;
      element.addEventListener(
        'click',
        (event) => {
          prevented = event.defaultPrevented;
          event.preventDefault();
        },
        { once: true },
      );
      link.dispatchEvent(new MouseEvent('click', { bubbles: true, cancelable: true, detail: 1 }));
      expect(prevented).toBe(true);
      expect(host.activations.map((a) => [a.rowId, a.via, a.newTab])).toEqual([
        [1, 'pointer', false],
      ]);
    });

    it('does not open a tile whose press travelled 4px or more before the click', async () => {
      const { host, element } = await setup();
      const body = tileAt(element, 2).querySelector('.tm-tiles__text')!;
      body.dispatchEvent(
        new PointerEvent('pointerdown', { bubbles: true, clientX: 50, clientY: 20 }),
      );
      body.dispatchEvent(
        new MouseEvent('click', { bubbles: true, clientX: 55, clientY: 20, detail: 1 }),
      );
      expect(host.activations).toEqual([]);
    });

    it('opens nothing when the pressed tile shows another row by the release', async () => {
      const { fixture, host, element } = await setup();
      /** Presses and releases on tile 2's body, with `between` run in between. */
      const tap = async (button: number, between: () => Promise<void>): Promise<void> => {
        const body = tileAt(element, 2).querySelector('.tm-tiles__text')!;
        body.dispatchEvent(
          new PointerEvent('pointerdown', { bubbles: true, button, clientX: 50, clientY: 20 }),
        );
        await between();
        // The same element, re-bound in place, takes the release.
        body.dispatchEvent(
          new MouseEvent(button === 0 ? 'click' : 'auxclick', {
            bubbles: true,
            button,
            clientX: 50,
            clientY: 20,
            detail: 1,
          }),
        );
      };
      const arrive = async (): Promise<void> => {
        host.rows.update((rows) => [{ ...rows[0], id: -rows.length, name: 'New' }, ...rows]);
        await fixture.whenStable();
      };
      await tap(0, arrive);
      await tap(1, arrive);
      expect(host.activations).toEqual([]);
      // With nothing in between, the same taps open the row (a second apart,
      // past the repeat guard).
      vi.useFakeTimers({ toFake: ['Date'] });
      try {
        vi.setSystemTime(1_000_000);
        await tap(0, async () => undefined);
        vi.setSystemTime(1_001_000);
        await tap(1, async () => undefined);
      } finally {
        vi.useRealTimers();
      }
      expect(host.activations.map((a) => [a.rowId, a.newTab])).toEqual([
        [1, false],
        [1, true],
      ]);
    });

    it('opens a tile clicked with no press of its own, whatever press came before', async () => {
      const { host, element } = await setup();
      const body = tileAt(element, 2).querySelector('.tm-tiles__text')!;
      body.dispatchEvent(
        new PointerEvent('pointerdown', { bubbles: true, clientX: 50, clientY: 20 }),
      );
      body.dispatchEvent(
        new MouseEvent('click', { bubbles: true, clientX: 50, clientY: 20, detail: 1 }),
      );
      // A script's or an assistive technology's click: no press, detail 0.
      tileAt(element, 5).click();
      expect(host.activations.map((a) => a.rowId)).toEqual([3, 6]);
    });
  });

  describe('the keyboard', () => {
    it('moves by tile across lines and keeps the column through a short last line', async () => {
      const { tiles } = await setup();
      await tiles.press('ArrowRight');
      expect(await (await tiles.getTile({ index: 1 })).isFocused()).toBe(true);
      await tiles.press('ArrowRight');
      await tiles.press('ArrowRight');
      await tiles.press('ArrowRight');
      expect(await (await tiles.getTile({ index: 4 })).isActive()).toBe(true);
      // Ten tiles in four columns: tile 7 (column 3) moves down to tile 9, the
      // short last line's end, and back up to column 3.
      await tiles.press('End');
      await tiles.press('ArrowDown');
      expect(await (await tiles.getTile({ index: 9 })).isActive()).toBe(true);
      await tiles.press('ArrowUp');
      expect(await (await tiles.getTile({ index: 7 })).isActive()).toBe(true);
      await tiles.press('Home');
      expect(await (await tiles.getTile({ index: 4 })).isActive()).toBe(true);
      await tiles.press('End', { control: true });
      expect(await (await tiles.getTile({ index: 9 })).isActive()).toBe(true);
      await tiles.press('Home', { control: true });
      expect(await (await tiles.getTile({ index: 0 })).isActive()).toBe(true);
    });

    it('mirrors Left and Right in a right-to-left page', async () => {
      const { tiles } = await setup((host) => host.dir.set('rtl'));
      await tiles.press('ArrowLeft');
      expect(await (await tiles.getTile({ index: 1 })).isActive()).toBe(true);
      await tiles.press('ArrowRight');
      expect(await (await tiles.getTile({ index: 0 })).isActive()).toBe(true);
    });

    it('pages by the visible lines and goes to the ends in a single column', async () => {
      const { tiles } = await setup((host) => {
        host.width.set(400);
        host.rows.set(products(40));
      });
      // 420px shows seven 60px lines.
      await tiles.press('PageDown');
      expect(await (await tiles.getTile({ index: 7 })).isActive()).toBe(true);
      await tiles.press('End');
      expect(await (await tiles.getTile({ index: 39 })).isFocused()).toBe(true);
      await tiles.press('Home');
      expect(await (await tiles.getTile({ index: 0 })).isFocused()).toBe(true);
    });

    it('keeps one tab stop that follows the active tile', async () => {
      const { host, tiles } = await setup();
      expect(await (await tiles.getTile({ index: 0 })).isTabStop()).toBe(true);
      await tiles.press('ArrowDown');
      expect(host.active()).toBe(5);
      const stops = [];
      for (const tile of await tiles.getTiles()) {
        stops.push(await tile.isTabStop());
      }
      expect(stops.filter(Boolean)).toHaveLength(1);
      expect(await (await tiles.getTile({ index: 4 })).isTabStop()).toBe(true);
    });
  });

  describe('the active row', () => {
    it('follows an external activeRowId write and scrolls to it without taking focus', async () => {
      const { fixture, host, tiles, scroller } = await setup((h) => h.rows.set(products(400)));
      const focused = document.activeElement;
      host.active.set(201);
      await fixture.whenStable();
      expect(await (await tiles.getTile({ index: 200 })).isActive()).toBe(true);
      expect(inView(scroller, tileAt(scroller, 200))).toBe(true);
      expect(document.activeElement).toBe(focused);
    });

    it('reveals an activeRowId bound before the lattice is measured', async () => {
      const { scroller } = await setup((h) => {
        h.rows.set(products(400));
        h.active.set(201);
      });
      expect(scroller.scrollTop).toBeGreaterThan(0);
      expect(inView(scroller, tileAt(scroller, 200))).toBe(true);
    });

    it('writes back an unknown id', async () => {
      const { fixture, host } = await setup((h) => h.active.set(3));
      host.active.set(999);
      await fixture.whenStable();
      expect(host.active()).toBe(3);
    });

    it('moves for a driver by lines, naming the tile at once', async () => {
      const { fixture, host } = await setup();
      const tiles = host.tiles();
      expect(tiles.pageStep()).toBe(6);
      tiles.moveActiveRow(1);
      expect(tiles.activeDescendantId()).toMatch(/-t0$/);
      tiles.moveActiveRow(1);
      expect(tiles.activeDescendantId()).toMatch(/-t4$/);
      tiles.moveActiveRow('last');
      expect(tiles.activeDescendantId()).toMatch(/-t9$/);
      await fixture.whenStable();
      expect(host.active()).toBe(10);
      expect(tiles.activateRow()).toBe(true);
      expect(host.activations.map((a) => [a.rowId, a.via])).toEqual([[10, 'keyboard']]);
      expect(tiles.activateRow(999)).toBe(false);
    });

    it('falls back to the tile at the same place when the active row leaves', async () => {
      const { fixture, host } = await setup((h) => h.active.set(4));
      host.rows.update((rows) => rows.filter((row) => row.id !== 4));
      await fixture.whenStable();
      expect(host.active()).toBe(5);
    });

    it('scrolls a row into view, flashes it, and refuses an unknown one', async () => {
      const { fixture, host, element, scroller } = await setup((h) => h.rows.set(products(400)));
      expect(host.tiles().scrollToRow(301, { flash: true })).toBe(true);
      await fixture.whenStable();
      expect(inView(scroller, tileAt(scroller, 300))).toBe(true);
      expect(host.active()).toBe(301);
      expect(element.querySelector('.tm-tiles__item--flash')).not.toBeNull();
      expect(host.tiles().scrollToRow(999)).toBe(false);
      host.tiles().scrollTo(0);
      expect(host.tiles().scrollOffset()).toBe(0);
    });
  });

  // Scrolling re-binds the elements of the lines that leave to the lines
  // that enter. These pin that the reuse happens, and that a reused tile
  // carries nothing of its last row: no state, no attribute, no focus and
  // no image.
  describe('reused tiles', () => {
    /** The rendered lines' indexes, in DOM order. */
    function lineIndexes(element: Element): number[] {
      return [...element.querySelectorAll('.tm-tiles__line')].map(
        (line) => Number(line.getAttribute('aria-rowindex')) - 1,
      );
    }

    /** The indexes `start … start + count - 1`. */
    function range(start: number, count: number): number[] {
      return Array.from({ length: count }, (_, i) => start + i);
    }

    it('keeps every rendered tile true to its own row while lines scroll past', async () => {
      const { fixture, host, tiles, element, scroller } = await setup((h) => {
        h.rows.set(products(10_000));
        h.selectable.set(true);
        h.selected.set(new Set([3, 42, 47, 4_001, 9_998]));
      });
      host.active.set(46);
      await fixture.whenStable();
      for (const line of [2, 9, 10, 900, 1_000, 2_494, 1_003]) {
        await scrollTo(fixture, scroller, line * 70 + 15);
        expectTilesTrueToTheirRows(element, host);
      }
      // A selection made on a reused tile shows on that tile only.
      await (await tiles.getTile({ index: 4_013 })).toggleSelected();
      await fixture.whenStable();
      expect(host.selected().has(4_014)).toBe(true);
      expectTilesTrueToTheirRows(element, host);
      await scrollTo(fixture, scroller, 0);
      expectTilesTrueToTheirRows(element, host);
    });

    it('re-binds the elements of the lines that scroll out to the lines that scroll in', async () => {
      const { fixture, element, scroller } = await setup((h) => h.rows.set(products(10_000)));
      await scrollTo(fixture, scroller, 100 * 70);
      const before = new Set(element.querySelectorAll('.tm-tiles__line, .tm-tiles__item'));
      for (const line of [102, 109, 2_000]) {
        await scrollTo(fixture, scroller, line * 70);
        const after = [...element.querySelectorAll('.tm-tiles__line, .tm-tiles__item')];
        expect(after).toHaveLength(before.size);
        expect(after.every((node) => before.has(node))).toBe(true);
      }
    });

    it('moves only the line elements a scroll re-binds, scrolling down or back up', async () => {
      const { fixture, element, scroller } = await setup((h) => h.rows.set(products(10_000)));
      await scrollTo(fixture, scroller, 100 * 70);
      // Counts the line elements taken out of the list to be put back elsewhere.
      let moved = 0;
      const count = (records: MutationRecord[]): void => {
        for (const record of records) {
          for (const node of record.removedNodes) {
            moved += node instanceof Element && node.classList.contains('tm-tiles__line') ? 1 : 0;
          }
        }
      };
      const observer = new MutationObserver(count);
      observer.observe(element.querySelector('.tm-tiles__canvas')!, { childList: true });
      // Each scroll of six lines re-binds six of the thirteen: the seven
      // lines that stay never move, in either direction.
      for (const line of [106, 112, 106, 100, 94, 100]) {
        moved = 0;
        await scrollTo(fixture, scroller, line * 70);
        count(observer.takeRecords());
        expect(moved).toBeLessThanOrEqual(6);
        expect(lineIndexes(element)).toEqual(range(line - 3, 13));
      }
      observer.disconnect();
    });

    it('renders a scroll in its own frame from the lines it has, and the overscan after it', async () => {
      const { fixture, element, scroller } = await setup((h) => h.rows.set(products(10_000)));
      await scrollTo(fixture, scroller, 50 * 70);
      expect(lineIndexes(element)).toEqual(range(47, 13));
      const lines = new Set(element.querySelectorAll('.tm-tiles__line'));
      scroller.scrollTop = 60 * 70;
      scroller.dispatchEvent(new Event('scroll'));
      fixture.detectChanges();
      // The frame moves its thirteen lines only as far as lines 60–66 need.
      expect(lineIndexes(element)).toEqual(range(54, 13));
      expect(
        [...element.querySelectorAll('.tm-tiles__line')].every((line) => lines.has(line)),
      ).toBe(true);
      // The next task renders three lines of overscan on each side again.
      await fixture.whenStable();
      expect(lineIndexes(element)).toEqual(range(57, 13));
    });

    it('keeps a focused tile on its row while it scrolls away and back, and moves on from it', async () => {
      const { fixture, tiles, scroller } = await setup((h) => h.rows.set(products(10_000)));
      await (await tiles.getTile({ index: 5 })).focus();
      await fixture.whenStable();
      const focused = document.activeElement as HTMLElement;
      expect(focused.dataset['index']).toBe('5');
      for (const top of [1_000 * 70, 0]) {
        await scrollTo(fixture, scroller, top);
        expect(document.activeElement).toBe(focused);
        expect(focused.dataset['index']).toBe('5');
        expect(shownTitle(focused)).toBe('Product 6');
      }
      await (await tiles.getTile({ index: 5 })).press('ArrowRight');
      const next = document.activeElement as HTMLElement;
      expect(next.dataset['index']).toBe('6');
      expect(shownTitle(next)).toBe('Product 7');
    });

    it('announces an arrow move that re-binds the focused tile in place, and keeps focus there', async () => {
      const { fixture, host, tiles, scroller } = await setup((h) => h.rows.set(products(10_000)));
      await (await tiles.getTile({ index: 5 })).focus();
      await fixture.whenStable();
      const focused = document.activeElement as HTMLElement;
      // Scrolled away, the focused line stays rendered off the window; the
      // line below it then takes its slot, and with it the focused element.
      await scrollTo(fixture, scroller, 1_000 * 70);
      const entered: Element[] = [];
      scroller.addEventListener('focusin', (event) => entered.push(event.target as Element));
      await (await tiles.getTile({ index: 5 })).press('ArrowDown');
      await fixture.whenStable();
      const next = document.activeElement as HTMLElement;
      const index = Number(next.dataset['index']);
      expect(index).toBeGreaterThan(5);
      expect(shownTitle(next)).toBe(`Product ${index + 1}`);
      expect(next).toBe(focused);
      // The move fires focus on the tile, so assistive technology reads it.
      expect(entered).toContain(next);
      // A page moving the active tile elsewhere never moves focus from it.
      host.active.set(index + 2);
      await fixture.whenStable();
      expect(document.activeElement).toBe(next);
      expect(shownTitle(next)).toBe(`Product ${index + 1}`);
    });

    it('moves focus with its row when rows arrive above the focused tile', async () => {
      const { fixture, host, tiles } = await setup((h) => h.rows.set(products(40)));
      await (await tiles.getTile({ index: 5 })).focus();
      await fixture.whenStable();
      host.rows.update((rows) => [{ id: 0, name: 'Product 0', price: 0, active: true }, ...rows]);
      await fixture.whenStable();
      // The focused element now shows Product 5: focus moves on to Product 6's tile.
      const focused = document.activeElement as HTMLElement;
      expect(shownTitle(focused)).toBe('Product 6');
      expect(focused.dataset['index']).toBe('6');
      expect(host.active()).toBe(6);
    });

    it('never leaves focus on a tile re-bound to another row', async () => {
      const { fixture, host, tiles } = await setup((h) => h.rows.set(products(10_000)));
      await (await tiles.getTile({ index: 5 })).focus();
      await fixture.whenStable();
      // A driver moves the active tile far off: the focused tile's line
      // stops being kept, and its elements go to the lines in view.
      host.active.set(8_001);
      await fixture.whenStable();
      const focused = document.activeElement as HTMLElement;
      expect(shownTitle(focused)).toBe('Product 8001');
      expect(focused.classList.contains('tm-tiles__item--active')).toBe(true);
    });

    it('gives a tile re-bound to another row a fresh image of that row', async () => {
      const { fixture, host, element, scroller } = await setup((h) => {
        h.rows.set(products(10_000));
        h.photos.set(true);
      });
      /** Waits out the scroll settle, so held images load. */
      const settle = async (): Promise<void> => {
        await new Promise((resolve) => setTimeout(resolve, 150));
        await fixture.whenStable();
      };
      /** Each rendered tile's image element and the row it is for. */
      const images = (): Map<Element, { readonly image: Element | null; readonly id: number }> =>
        new Map(
          renderedTiles(element).map((tile) => [
            tile,
            {
              image: tile.querySelector('tm-image'),
              id: host.rows()[Number(tile.dataset['index'])].id,
            },
          ]),
        );
      // Lines 0–12 show their photos once scrolling settles, then lines 21–33.
      await scrollTo(fixture, scroller, 3 * 70);
      await settle();
      await scrollTo(fixture, scroller, 24 * 70);
      await settle();
      const before = images();
      // Back at line 3 the tiles of lines 21–33 show lines 0–12, whose
      // photos showed before, so each tile shows its row's photo at once.
      await scrollTo(fixture, scroller, 3 * 70);
      const instances = new Map(
        fixture.debugElement
          .queryAll(By.directive(TmImage))
          .map((debug) => [debug.nativeElement as Element, debug.componentInstance as TmImage]),
      );
      for (const [tile, { image, id }] of images()) {
        expect(image).not.toBeNull();
        expect(instances.get(image!)!.src()).toBe(`/img/${id}`);
        expect(instances.get(image!)!.alt()).toBe(`Product ${id}`);
        const earlier = before.get(tile);
        if (earlier !== undefined && earlier.id !== id) {
          expect(image).not.toBe(earlier.image);
        }
      }
      // Every tile was reused, so every image is a fresh one.
      expect([...images().keys()].every((tile) => before.has(tile))).toBe(true);
    });

    it('never re-binds a custom body to another row', async () => {
      const { fixture, element, scroller } = await setup((h) => {
        h.rows.set(products(10_000));
        h.customBody.set(true);
      });
      await scrollTo(fixture, scroller, 100 * 70);
      const shown = new Map(
        renderedTiles(element).map((tile) => [
          tile,
          tile.querySelector('.custom-body')!.textContent,
        ]),
      );
      await scrollTo(fixture, scroller, 104 * 70);
      const after = renderedTiles(element);
      for (const tile of after) {
        const body = tile.querySelector('.custom-body')!.textContent;
        expect(body).toBe(`Product ${Number(tile.dataset['index']) + 1}!`);
        if (shown.has(tile)) {
          expect(body).toBe(shown.get(tile));
        }
      }
      // The lines that stayed kept their tiles; the lines that came in have new ones.
      expect(after.filter((tile) => shown.has(tile))).toHaveLength(9 * 4);
    });

    it('keeps tree rows true to their level, position and expansion while lines scroll past', async () => {
      // 200 roots of three children each; the odd roots are expanded.
      const rows: Product[] = [];
      const shown: {
        readonly name: string;
        readonly level: number;
        readonly pos: number;
        readonly size: number;
        readonly expanded: string | null;
      }[] = [];
      for (let root = 1; root <= 200; root++) {
        rows.push({ id: root, name: `Root ${root}`, price: 0, active: true, parentId: null });
        shown.push({
          name: `Root ${root}`,
          level: 1,
          pos: root,
          size: 200,
          expanded: String(root % 2 === 1),
        });
        for (let child = 1; child <= 3; child++) {
          const id = 10_000 + root * 10 + child;
          rows.push({ id, name: `Leaf ${id}`, price: 0, active: true, parentId: root });
          if (root % 2 === 1) {
            shown.push({ name: `Leaf ${id}`, level: 2, pos: child, size: 3, expanded: null });
          }
        }
      }
      const { fixture, element, scroller } = await setup((h) => {
        h.rows.set(rows);
        h.tree();
        h.expanded.set(
          new Set(rows.filter((row) => row.id % 2 === 1 && row.id <= 200).map((row) => row.id)),
        );
      });
      for (const top of [10 * 60, 251 * 60 + 20, 37 * 60, 0]) {
        await scrollTo(fixture, scroller, top);
        for (const line of element.querySelectorAll('[role="row"]')) {
          const expected = shown[Number(line.getAttribute('aria-rowindex')) - 1];
          expect(shownTitle(line.querySelector('.tm-tiles__item')!)).toBe(expected.name);
          expect(line.getAttribute('aria-level')).toBe(String(expected.level));
          expect(line.getAttribute('aria-posinset')).toBe(String(expected.pos));
          expect(line.getAttribute('aria-setsize')).toBe(String(expected.size));
          expect(line.getAttribute('aria-expanded')).toBe(expected.expanded);
        }
      }
    });
  });

  describe('loading and empty', () => {
    it('shows skeleton tiles while loading without data, then the data in their place', async () => {
      const { fixture, host, tiles, element } = await setup((h) => {
        h.rows.set([]);
        h.loading.set(true);
      });
      expect(await tiles.isShowingSkeleton()).toBe(true);
      expect(await tiles.isBusy()).toBe(true);
      const skeleton = element.querySelector('.tm-tiles__item--skeleton')!.getBoundingClientRect();
      host.rows.set(products(4));
      host.loading.set(false);
      await fixture.whenStable();
      expect(await tiles.isShowingSkeleton()).toBe(false);
      const tile = element.querySelector('.tm-tiles__item')!.getBoundingClientRect();
      expect([tile.left, tile.top, tile.width, tile.height]).toEqual([
        skeleton.left,
        skeleton.top,
        skeleton.width,
        skeleton.height,
      ]);
    });

    it('marks loading over data as busy', async () => {
      const { tiles } = await setup((host) => host.loading.set(true));
      expect(await tiles.isBusy()).toBe(true);
      expect(await tiles.isShowingSkeleton()).toBe(false);
      expect(await tiles.getTiles()).toHaveLength(10);
    });

    it('says there is nothing to show, or renders the empty template', async () => {
      const { fixture, host, tiles } = await setup((h) => h.rows.set([]));
      expect(await tiles.getEmptyText()).toBe('No items to display');
      host.customEmpty.set(true);
      await fixture.whenStable();
      expect(await tiles.getEmptyText()).toBe('Nothing here');
    });
  });

  describe('tree mode', () => {
    /** Two lazy roots, a leaf root, and a loaded child under the leaf root. */
    const TREE: readonly Product[] = [
      { id: 1, name: 'Assets', price: 0, active: true, parentId: null, lazy: true },
      { id: 2, name: 'Liabilities', price: 0, active: true, parentId: null, lazy: true },
      { id: 3, name: 'Equity', price: 0, active: true, parentId: null },
      { id: 31, name: 'Capital', price: 0, active: true, parentId: 3 },
    ];

    it('is a one-column treegrid with levels, sibling positions and expansion', async () => {
      const { fixture, host, tiles, element } = await setup((h) => {
        h.rows.set(TREE);
        h.tree();
        h.expanded.set(new Set([3]));
      });
      expect(await tiles.isTree()).toBe(true);
      expect(await tiles.getLayout()).toBe('tree');
      expect(await tiles.getColumnCount()).toBe(1);
      expect(await tiles.getTileTitles()).toEqual(['Assets', 'Liabilities', 'Equity', 'Capital']);
      const rows = [...element.querySelectorAll('[role="row"]')];
      expect(rows.map((row) => row.getAttribute('aria-level'))).toEqual(['1', '1', '1', '2']);
      expect(rows.map((row) => row.getAttribute('aria-posinset'))).toEqual(['1', '2', '3', '1']);
      expect(rows.map((row) => row.getAttribute('aria-setsize'))).toEqual(['3', '3', '3', '1']);
      expect(rows.map((row) => row.getAttribute('aria-expanded'))).toEqual([
        'false',
        'false',
        'true',
        null,
      ]);
      expect(await (await tiles.getTile({ title: 'Capital' })).getLevel()).toBe(2);
      await (await tiles.getTile({ title: 'Equity' })).toggle();
      expect(host.expanded()).toEqual(new Set());
      // The twisty toggles; it does not open the row.
      expect(host.activations).toEqual([]);
      await fixture.whenStable();
      expect(await tiles.getTileTitles()).toEqual(['Assets', 'Liabilities', 'Equity']);
    });

    it('loads the children of every expanded row in one batch, in data order, level by level', async () => {
      const first = deferred();
      const second = deferred();
      const load = vi
        .fn<(rows: readonly Product[]) => Promise<void>>()
        .mockReturnValueOnce(first.promise)
        .mockReturnValueOnce(second.promise);
      const { fixture, host, tiles } = await setup((h) => {
        h.rows.set(TREE);
        h.tree(load);
        h.expanded.set(new Set([2, 1, 11]));
      });
      expect(load).toHaveBeenCalledTimes(1);
      expect(load.mock.calls[0][0].map((row) => row.id)).toEqual([1, 2]);
      expect(await (await tiles.getTile({ title: 'Assets' })).isLoading()).toBe(true);
      expect(await (await tiles.getTile({ title: 'Assets' })).isExpanded()).toBe(true);
      expect(await (await tiles.getTile({ title: 'Equity' })).isLoading()).toBe(false);
      // The consumer appends the children, one of them lazy and remembered
      // expanded, then resolves: the next level loads in a batch of its own.
      host.rows.update((rows) => [
        ...rows,
        { id: 11, name: 'Cash', price: 0, active: true, parentId: 1, lazy: true },
      ]);
      first.resolve();
      await settled(fixture);
      expect(load).toHaveBeenCalledTimes(2);
      expect(load.mock.calls[1][0].map((row) => row.id)).toEqual([11]);
      // Liabilities loaded no children: it is a leaf now, and never reloads.
      expect(await (await tiles.getTile({ title: 'Liabilities' })).isExpandable()).toBe(false);
      second.resolve();
      await settled(fixture);
      expect(await tiles.getTileTitles()).toEqual(['Assets', 'Cash', 'Liabilities', 'Equity']);
      expect(load).toHaveBeenCalledTimes(2);
    });

    it('loads a refreshed row again, even one whose earlier load brought no children', async () => {
      const load = vi.fn<(rows: readonly Product[]) => Promise<void>>(() => Promise.resolve());
      const { fixture, host, tiles } = await setup((h) => {
        h.rows.set(TREE);
        h.tree(load);
        h.expanded.set(new Set([2]));
      });
      await settled(fixture);
      expect(await (await tiles.getTile({ title: 'Liabilities' })).isExpandable()).toBe(false);
      // A re-query brings fresh row objects: the row is lazy again and,
      // still expanded, loads in a batch of its own.
      host.rows.set(TREE.map((row) => ({ ...row })));
      await settled(fixture);
      expect(load).toHaveBeenCalledTimes(2);
      expect(load.mock.calls[1][0].map((row) => row.id)).toEqual([2]);
    });

    it('collapses a failed batch and announces it once', async () => {
      const failed = deferred();
      const { fixture, host, tiles, announce } = await setup((h) => {
        h.rows.set(TREE);
        h.tree(() => failed.promise);
        h.expanded.set(new Set([1, 2, 77]));
      });
      failed.reject();
      await settled(fixture);
      expect(host.expanded()).toEqual(new Set([77]));
      expect(await (await tiles.getTile({ title: 'Assets' })).isExpanded()).toBe(false);
      expect(await (await tiles.getTile({ title: 'Assets' })).isExpandable()).toBe(true);
      expect(announce).toHaveBeenCalledTimes(1);
      expect(announce).toHaveBeenCalledWith("Couldn't load Assets and Liabilities", 'polite');
    });

    it('expands with Right, enters with Right, leaves with Left and collapses with Left', async () => {
      const { host, tiles } = await setup((h) => {
        h.rows.set(TREE);
        h.tree();
      });
      await (await tiles.getTile({ title: 'Equity' })).focus();
      await tiles.press('ArrowRight');
      expect(host.expanded()).toEqual(new Set([3]));
      await tiles.press('ArrowRight');
      expect(await (await tiles.getTile({ title: 'Capital' })).isFocused()).toBe(true);
      await tiles.press('ArrowLeft');
      expect(await (await tiles.getTile({ title: 'Equity' })).isFocused()).toBe(true);
      await tiles.press('ArrowLeft');
      expect(host.expanded()).toEqual(new Set());
    });

    it('mirrors Right and Left in a right-to-left page', async () => {
      const { host, tiles } = await setup((h) => {
        h.dir.set('rtl');
        h.rows.set(TREE);
        h.tree();
      });
      await (await tiles.getTile({ title: 'Equity' })).focus();
      await tiles.press('ArrowLeft');
      expect(host.expanded()).toEqual(new Set([3]));
      await tiles.press('ArrowRight');
      expect(host.expanded()).toEqual(new Set());
    });

    it('expands the ancestors of an external activeRowId write', async () => {
      const { fixture, host, tiles } = await setup((h) => {
        h.rows.set(TREE);
        h.tree();
      });
      host.active.set(31);
      await fixture.whenStable();
      expect(host.expanded()).toEqual(new Set([3]));
      expect(await (await tiles.getTile({ title: 'Capital' })).isActive()).toBe(true);
    });

    it('moves the active row to the ancestor a collapse hides it under', async () => {
      const { fixture, host } = await setup((h) => {
        h.rows.set(TREE);
        h.tree();
        h.expanded.set(new Set([3]));
        h.active.set(31);
      });
      host.expanded.set(new Set());
      await fixture.whenStable();
      expect(host.active()).toBe(3);
    });
  });
});

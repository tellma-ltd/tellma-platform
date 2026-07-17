// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { Component, computed, viewChild, type ElementRef, type TemplateRef } from '@angular/core';
import { TestBed, type ComponentFixture } from '@angular/core/testing';
import { TestbedHarnessEnvironment } from '@angular/cdk/testing/testbed';

import { provideTellmaUi } from '@tellma/core-ui';
import { ɵtmObserveLongPress } from '@tellma/core-ui/menu';
import { TmMenuHarness } from '@tellma/core-ui-testing';

import { TmMenu, type TmMenuEntry, type TmMenuItem } from './tm-menu';

@Component({
  imports: [TmMenu],
  template: `
    <button type="button" #btn>Open menu</button>

    <ng-template #star>
      <svg viewBox="0 0 16 16" fill="none" aria-hidden="true">
        <path d="M8 2l1.8 3.9L14 6.5l-3 3 .7 4.2L8 11.6l-3.7 2.1.7-4.2-3-3 4.2-.6z" fill="currentColor" />
      </svg>
    </ng-template>

    <tm-menu [items]="items()" aria-label="Test actions" (itemSelected)="selections.push($event)" />
  `,
})
class Host {
  readonly menu = viewChild.required(TmMenu);
  readonly button = viewChild.required<ElementRef<HTMLButtonElement>>('btn');
  private readonly star = viewChild<TemplateRef<void>>('star');

  readonly log: string[] = [];
  readonly selections: TmMenuItem[] = [];

  readonly items = computed<readonly TmMenuEntry[]>(() => [
    { id: 'edit', label: 'Edit', icon: this.star(), action: () => this.log.push('edit') },
    { id: 'copy', label: 'Copy', action: () => this.log.push('copy') },
    { separator: true },
    // Resolved through provideTellmaUi's packaged English strings ->
    // 'Select an option' (proves labelKey goes through the i18n seam).
    { id: 'localized', labelKey: 'select.placeholder', action: () => this.log.push('localized') },
    { id: 'blocked', label: 'Blocked', disabled: true, action: () => this.log.push('blocked') },
    { id: 'delete', label: 'Delete', action: () => this.log.push('delete') },
  ]);
}

const macrotask = () => new Promise((resolve) => setTimeout(resolve, 0));

/** Two stabilizations with a macrotask hop: the overlay attach + the panel's post-attach re-measure. */
async function settle(fixture: ComponentFixture<unknown>): Promise<void> {
  await fixture.whenStable();
  await macrotask();
  await fixture.whenStable();
}

async function setup() {
  TestBed.configureTestingModule({ providers: [provideTellmaUi()] });
  const fixture = TestBed.createComponent(Host);
  await settle(fixture);
  return { fixture, host: fixture.componentInstance };
}

/** The overlay-portaled panel (null while the menu is closed). */
function panel(): HTMLElement | null {
  return document.querySelector('.tm-menu__panel');
}

/** Gets the panel harness from the document root (the panel lives in the overlay). */
function menuHarness(fixture: ComponentFixture<unknown>): Promise<TmMenuHarness> {
  return TestbedHarnessEnvironment.documentRootLoader(fixture).getHarness(TmMenuHarness);
}

/** Dispatches a real keydown on whatever currently has focus (bubbles to the aria menu). */
function press(key: string): void {
  (document.activeElement ?? document.body).dispatchEvent(
    new KeyboardEvent('keydown', { key, bubbles: true }),
  );
}

describe('tm-menu', () => {
  describe('rendering & labels', () => {
    it('open() at a point renders one overlay panel with role=menu and the entries in order', async () => {
      const { fixture, host } = await setup();
      expect(panel()).toBeNull(); // renders ONLY into the overlay, nothing in-flow

      host.menu().open({ x: 80, y: 90 });
      await settle(fixture);

      const panelEl = panel();
      expect(panelEl).not.toBeNull();
      const list = panelEl!.querySelector('.tm-menu__list')!;
      expect(list.getAttribute('role')).toBe('menu');
      expect(list.getAttribute('aria-label')).toBe('Test actions');
      // The accessible name is RELOCATED: it must not linger on the
      // role-less host, where axe flags it (aria-prohibited-attr).
      expect(
        (fixture.nativeElement as HTMLElement).querySelector('tm-menu')!.getAttribute('aria-label'),
      ).toBeNull();

      // Items in order; the labelKey entry resolved through the English strings.
      const menu = await menuHarness(fixture);
      expect(await menu.getItemLabels()).toEqual([
        'Edit',
        'Copy',
        'Select an option',
        'Blocked',
        'Delete',
      ]);

      // The separator renders with role=separator, between Copy and the localized item.
      const children = Array.from(list.children).map((child) =>
        child.classList.contains('tm-menu__separator') ? 'separator' : 'item',
      );
      expect(children).toEqual(['item', 'item', 'separator', 'item', 'item', 'item']);
      expect(list.querySelector('.tm-menu__separator')!.getAttribute('role')).toBe('separator');
      expect(host.menu().isOpen()).toBe(true);
    });

    it('items have role=menuitem; a disabled entry carries aria-disabled=true', async () => {
      const { fixture, host } = await setup();
      host.menu().open({ x: 80, y: 90 });
      await settle(fixture);

      const rows = Array.from(panel()!.querySelectorAll('.tm-menu__item'));
      expect(rows.every((row) => row.getAttribute('role') === 'menuitem')).toBe(true);

      const menu = await menuHarness(fixture);
      const items = await menu.getItems();
      expect(await items[3].isDisabled()).toBe(true); // Blocked
      expect(await items[0].isDisabled()).toBe(false);

      // The icon template renders inside the item, hidden from the tree.
      const icon = rows[0].querySelector('.tm-menu__icon')!;
      expect(icon.getAttribute('aria-hidden')).toBe('true');
      expect(icon.querySelector('svg')).not.toBeNull();
    });
  });

  describe('activation', () => {
    it('clicking an item runs its action, emits itemSelected, and closes', async () => {
      const { fixture, host } = await setup();
      host.menu().open({ x: 80, y: 90 });
      await settle(fixture);

      const menu = await menuHarness(fixture);
      await menu.clickItem('Copy');
      await settle(fixture);

      expect(host.log).toEqual(['copy']); // action ran...
      expect(host.selections.map((item) => item.id)).toEqual(['copy']); // ...before the emit
      expect(panel()).toBeNull();
      expect(host.menu().isOpen()).toBe(false);
    });

    it('clicking a DISABLED item neither activates nor closes', async () => {
      const { fixture, host } = await setup();
      host.menu().open({ x: 80, y: 90 });
      await settle(fixture);

      const menu = await menuHarness(fixture);
      await menu.clickItem('Blocked');
      await settle(fixture);

      expect(host.log).toEqual([]);
      expect(host.selections).toEqual([]);
      expect(panel()).not.toBeNull();
      expect(host.menu().isOpen()).toBe(true);
    });

    it('Enter activates the typeahead-selected item — itemSelected routes by id', async () => {
      const { fixture, host } = await setup();
      host.menu().open({ x: 80, y: 90 });
      await settle(fixture);

      press('d'); // typeahead -> Delete
      await fixture.whenStable();
      press('Enter');
      await settle(fixture);

      expect(host.log).toEqual(['delete']);
      expect(host.selections.map((item) => item.id)).toEqual(['delete']);
      expect(panel()).toBeNull();
    });
  });

  describe('keyboard & focus', () => {
    // Version-locked guard for the parentless-@angular/aria seam tm-menu is
    // built on (aria 22.0.3): a Menu with no MenuTrigger/MenuItem parent
    // reports visible()=true unconditionally, and its default-state effect
    // activates the FIRST item once items render — tm-menu then focuses the
    // list and keyboard interaction takes over from there. If an @angular/aria
    // upgrade changes the parentless behavior, this spec is the tripwire.
    it('after open, focus is inside the menu and the FIRST item is active (parentless-aria guard)', async () => {
      const { fixture, host } = await setup();
      host.menu().open({ x: 80, y: 90 });
      await settle(fixture);

      const panelEl = panel()!;
      expect(panelEl.contains(document.activeElement)).toBe(true);

      const menu = await menuHarness(fixture);
      const items = await menu.getItems();
      expect(await items[0].isActive()).toBe(true);
      expect(await items[1].isActive()).toBe(false);
    });

    it('arrow keys move data-active; Home/End jump to the edges', async () => {
      const { fixture, host } = await setup();
      host.menu().open({ x: 80, y: 90 });
      await settle(fixture);
      const menu = await menuHarness(fixture);
      const items = await menu.getItems();

      press('ArrowDown');
      await fixture.whenStable();
      expect(await items[1].isActive()).toBe(true); // Copy

      press('ArrowDown');
      await fixture.whenStable();
      expect(await items[2].isActive()).toBe(true); // Select an option

      press('ArrowUp');
      await fixture.whenStable();
      expect(await items[1].isActive()).toBe(true);

      press('End');
      await fixture.whenStable();
      expect(await items[4].isActive()).toBe(true); // Delete

      press('Home');
      await fixture.whenStable();
      expect(await items[0].isActive()).toBe(true); // Edit
    });

    it('typeahead activates the first matching item by its RESOLVED label', async () => {
      const { fixture, host } = await setup();
      host.menu().open({ x: 80, y: 90 });
      await settle(fixture);
      const menu = await menuHarness(fixture);
      const items = await menu.getItems();

      // 's' matches 'Select an option' — the labelKey item, so the search
      // term is the translated string, not the key.
      press('s');
      await fixture.whenStable();
      expect(await items[2].isActive()).toBe(true);
    });

    it('Escape closes and restores focus to the restoreFocus element', async () => {
      const { fixture, host } = await setup();
      const button = host.button().nativeElement;
      host.menu().open(button, { restoreFocus: button });
      await settle(fixture);
      expect(panel()!.contains(document.activeElement)).toBe(true);

      press('Escape');
      await settle(fixture);

      // Escape is intercepted at the DOCUMENT capture phase: aria's
      // MenuPattern registers its own Escape handler (a no-op parentless)
      // with preventDefault + stopPropagation, so the event would otherwise
      // be consumed at .tm-menu__list before reaching the panel.
      expect(panel()).toBeNull();
      expect(host.menu().isOpen()).toBe(false);
      expect(document.activeElement).toBe(button);
      expect(host.log).toEqual([]); // Escape must never activate an item
    });

    it('Tab closes and returns focus so it can continue past the invoker', async () => {
      const { fixture, host } = await setup();
      const button = host.button().nativeElement;
      host.menu().open(button, { restoreFocus: button });
      await settle(fixture);

      press('Tab');
      await settle(fixture);

      expect(panel()).toBeNull();
      // Focus is handed back to the invoker; the un-consumed Tab keydown
      // would then move it onward in a real browser interaction.
      expect(document.activeElement).toBe(button);
    });
  });

  describe('open/close semantics', () => {
    it('outside click closes WITHOUT restoring focus', async () => {
      const { fixture, host } = await setup();
      const button = host.button().nativeElement;
      host.menu().open(button, { restoreFocus: button });
      await settle(fixture);

      document.body.dispatchEvent(new PointerEvent('pointerdown', { bubbles: true }));
      document.body.dispatchEvent(new MouseEvent('click', { bubbles: true }));
      await settle(fixture);

      expect(panel()).toBeNull();
      expect(host.menu().isOpen()).toBe(false);
      expect(document.activeElement).not.toBe(button); // outside click ≠ keyboard dismissal
    });

    it('open while open re-anchors — no crash, still exactly one panel', async () => {
      const { fixture, host } = await setup();
      host.menu().open({ x: 40, y: 40 });
      await settle(fixture);
      expect(document.querySelectorAll('.tm-menu__panel').length).toBe(1);

      host.menu().open({ x: 200, y: 160 });
      await settle(fixture);

      expect(document.querySelectorAll('.tm-menu__panel').length).toBe(1);
      expect(host.menu().isOpen()).toBe(true);

      // Still a fully functional menu after the re-anchor.
      const menu = await menuHarness(fixture);
      expect(await menu.getItemLabels()).toContain('Edit');
    });
  });
});

describe('ɵtmObserveLongPress', () => {
  let element: HTMLElement;
  let stop: (() => void) | undefined;

  beforeEach(() => {
    element = document.createElement('div');
    document.body.appendChild(element);
  });

  afterEach(() => {
    stop?.();
    stop = undefined;
    element.remove();
  });

  const sleep = (ms: number) => new Promise((resolve) => setTimeout(resolve, ms));

  function touchDown(x = 50, y = 60): void {
    element.dispatchEvent(
      new PointerEvent('pointerdown', {
        pointerType: 'touch',
        isPrimary: true,
        pointerId: 1,
        clientX: x,
        clientY: y,
        bubbles: true,
      }),
    );
  }

  it('fires with the press point after the delay', async () => {
    const points: { x: number; y: number }[] = [];
    stop = ɵtmObserveLongPress(element, (point) => points.push(point), { delayMs: 30 });

    touchDown(50, 60);
    await sleep(80);

    expect(points).toEqual([{ x: 50, y: 60 }]);
  });

  it('movement beyond the slop cancels the press', async () => {
    const points: { x: number; y: number }[] = [];
    stop = ɵtmObserveLongPress(element, (point) => points.push(point), {
      delayMs: 30,
      slopPx: 8,
    });

    touchDown(50, 60);
    element.dispatchEvent(
      new PointerEvent('pointermove', {
        pointerType: 'touch',
        isPrimary: true,
        pointerId: 1,
        clientX: 62, // 12px > the 8px slop
        clientY: 60,
        bubbles: true,
      }),
    );
    await sleep(80);

    expect(points).toEqual([]);
  });

  it('releasing before the delay cancels the press', async () => {
    const points: { x: number; y: number }[] = [];
    stop = ɵtmObserveLongPress(element, (point) => points.push(point), { delayMs: 30 });

    touchDown();
    element.dispatchEvent(
      new PointerEvent('pointerup', {
        pointerType: 'touch',
        isPrimary: true,
        pointerId: 1,
        bubbles: true,
      }),
    );
    await sleep(80);

    expect(points).toEqual([]);
  });

  it('mouse presses never qualify — right-click owns that path', async () => {
    const points: { x: number; y: number }[] = [];
    stop = ɵtmObserveLongPress(element, (point) => points.push(point), { delayMs: 30 });

    element.dispatchEvent(
      new PointerEvent('pointerdown', {
        pointerType: 'mouse',
        isPrimary: true,
        pointerId: 1,
        clientX: 10,
        clientY: 10,
        bubbles: true,
      }),
    );
    await sleep(80);

    expect(points).toEqual([]);
  });
});

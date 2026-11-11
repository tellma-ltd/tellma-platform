// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import {
  Component,
  computed,
  signal,
  viewChild,
  type ElementRef,
  type TemplateRef,
} from '@angular/core';
import { TestBed, type ComponentFixture } from '@angular/core/testing';
import { TestbedHarnessEnvironment } from '@angular/cdk/testing/testbed';
import { Star } from 'lucide';

import { provideTellmaUi } from '@tellma/core-ui';
import { provideTellmaIcons } from '@tellma/core-ui/icon';
import { tmFormatKeys } from '@tellma/core-ui/kbd';
import { TmMenuHarness, TmMenuTriggerHarness } from '@tellma/core-ui-testing';

import { TmMenu, TmMenuHeaderDef } from './tm-menu';
import type { TmMenuEntry, TmMenuItem } from './tm-menu-entries';
import { TmMenuTrigger } from './tm-menu-trigger';

/** A host with a trigger button, a header slot, and whatever entries a test sets. */
@Component({
  imports: [TmMenu, TmMenuHeaderDef, TmMenuTrigger],
  template: `
    <button type="button" #trigger [tmMenuTriggerFor]="menu">Options</button>
    <button type="button" #other>Elsewhere</button>

    <ng-template #glyph>
      <svg
        class="template-glyph"
        viewBox="0 0 16 16"
        width="16"
        height="16"
        aria-hidden="true"
      ></svg>
    </ng-template>

    <tm-menu #menu [items]="items()" aria-label="Rich" (itemSelected)="selections.push($event)">
      @if (header() !== null) {
        <ng-template tmMenuHeader>
          <span class="header-text">{{ header() }}</span>
          @if (interactiveHeader()) {
            <button type="button">Not allowed here</button>
          }
        </ng-template>
      }
    </tm-menu>
  `,
})
class RichHost {
  readonly menu = viewChild.required(TmMenu);
  readonly trigger = viewChild.required<ElementRef<HTMLButtonElement>>('trigger');
  readonly other = viewChild.required<ElementRef<HTMLButtonElement>>('other');
  readonly glyph = viewChild.required<TemplateRef<void>>('glyph');

  readonly log: string[] = [];
  readonly selections: TmMenuItem[] = [];
  readonly items = signal<readonly TmMenuEntry[]>([]);
  readonly header = signal<string | null>(null);
  readonly interactiveHeader = signal(false);

  /** A do-nothing action that records its id. */
  act(id: string): () => void {
    return () => this.log.push(id);
  }
}

const macrotask = () => new Promise((resolve) => setTimeout(resolve, 0));
const sleep = (ms: number) => new Promise((resolve) => setTimeout(resolve, ms));

/** Two stabilizations with a macrotask hop: the overlay attach + the post-attach re-measure. */
async function settle(fixture: ComponentFixture<unknown>): Promise<void> {
  await fixture.whenStable();
  await macrotask();
  await fixture.whenStable();
}

async function setup(items: (host: RichHost) => readonly TmMenuEntry[]) {
  TestBed.configureTestingModule({
    providers: [provideTellmaUi(), provideTellmaIcons({ star: Star })],
  });
  const fixture = TestBed.createComponent(RichHost);
  const host = fixture.componentInstance;
  await settle(fixture);
  host.items.set(items(host));
  await settle(fixture);
  return { fixture, host };
}

async function open(fixture: ComponentFixture<RichHost>): Promise<TmMenuHarness> {
  const button = fixture.componentInstance.trigger().nativeElement;
  fixture.componentInstance.menu().open(button, { restoreFocus: button });
  await settle(fixture);
  return TestbedHarnessEnvironment.documentRootLoader(fixture).getHarness(TmMenuHarness);
}

/** The menu's own panel (null while closed). */
function panel(): HTMLElement | null {
  return document.querySelector('.tm-menu__panel:not(.tm-menu__panel--sub)');
}

/** Every submenu panel currently displayed. */
function shownSubPanels(): HTMLElement[] {
  return Array.from(document.querySelectorAll<HTMLElement>('.tm-menu__panel--sub')).filter(
    (sub) => getComputedStyle(sub).display !== 'none',
  );
}

/** The row with the given label, on any level. */
function row(label: string): HTMLElement {
  const rows = Array.from(document.querySelectorAll<HTMLElement>('.tm-menu__item'));
  const found = rows.find(
    (candidate) => candidate.querySelector('.tm-menu__label')?.textContent?.trim() === label,
  );
  if (found === undefined) {
    throw new Error(`no row labelled "${label}"`);
  }
  return found;
}

/** Dispatches a cancelable keydown on whatever has focus and returns it. */
function press(key: string, init: KeyboardEventInit = {}): KeyboardEvent {
  const event = new KeyboardEvent('keydown', { key, bubbles: true, cancelable: true, ...init });
  (document.activeElement ?? document.body).dispatchEvent(event);
  return event;
}

/** The label of the row that holds DOM focus. */
function focusedLabel(): string | undefined {
  return document.activeElement?.querySelector('.tm-menu__label')?.textContent?.trim();
}

/**
 * Clicks a link row with a synthetic click (or `auxclick`) whose default
 * action (the navigation) is cancelled only AFTER the menu's handlers ran,
 * and reports whether those handlers had cancelled it themselves. The guard
 * listens on the link itself, after the row's own listener: a modified
 * click stops propagating there, and a guard further up would never see it
 * and the test browser would really open the address in a new tab.
 */
function clickLink(
  link: HTMLElement,
  init: MouseEventInit = {},
  type: 'click' | 'auxclick' = 'click',
): { prevented: boolean } {
  let prevented: boolean | undefined;
  const guard = (event: Event): void => {
    prevented = event.defaultPrevented;
    event.preventDefault();
  };
  link.addEventListener(type, guard, { once: true });
  link.dispatchEvent(new MouseEvent(type, { bubbles: true, cancelable: true, ...init }));
  if (prevented === undefined) {
    throw new Error('the navigation guard never saw the event');
  }
  return { prevented };
}

describe('tm-menu entry kinds', () => {
  afterEach(() => {
    document.documentElement.removeAttribute('dir');
  });

  describe('items', () => {
    it('renders a description as an aria-hidden second line that describes the row', async () => {
      const { fixture } = await setup((host) => [
        { id: 'a', label: 'Archive', description: 'Hide it from lists', action: host.act('a') },
        { id: 'b', label: 'Plain', action: host.act('b') },
      ]);
      const menu = await open(fixture);

      const items = await menu.getItems();
      expect(await items[0].getDescription()).toBe('Hide it from lists');
      expect(await items[1].getDescription()).toBeNull();

      const archive = row('Archive');
      const line = archive.querySelector('.tm-menu__description')!;
      expect(line.getAttribute('aria-hidden')).toBe('true');
      expect(archive.getAttribute('aria-describedby')).toBe(line.id);
      expect(row('Plain').hasAttribute('aria-describedby')).toBe(false);
    });

    it('renders detail as trailing text that joins the accessible name after a pause', async () => {
      const { fixture } = await setup((host) => [
        { id: 'v', label: 'Version', detail: 'v2.14.0', action: host.act('v') },
        { id: 'p', label: 'Plain', action: host.act('p') },
        {
          id: 'd',
          label: 'Docs',
          detail: 'PDF',
          href: 'https://docs.example/',
          target: '_blank',
        },
      ]);
      const menu = await open(fixture);

      const items = await menu.getItems();
      expect(await items[0].getDetail()).toBe('v2.14.0');
      expect(await items[1].getDetail()).toBeNull();
      expect(row('Version').getAttribute('aria-label')).toBe('Version, v2.14.0');
      // A row with no detail is named by its content.
      expect(row('Plain').hasAttribute('aria-label')).toBe(false);
      // The spelled-out name keeps a new-tab link's note.
      expect(row('Docs').getAttribute('aria-label')).toBe('Docs, PDF (opens in a new tab)');
    });

    it('sets lang on a row whose label is in another language', async () => {
      const { fixture } = await setup((host) => [
        { id: 'ar', label: 'العربية', lang: 'ar', action: host.act('ar') },
        { id: 'en', label: 'English', action: host.act('en') },
      ]);
      await open(fixture);

      expect(row('العربية').getAttribute('lang')).toBe('ar');
      expect(row('English').hasAttribute('lang')).toBe(false);
    });

    it('renders a shortcut as platform key caps, hidden from assistive technology', async () => {
      const { fixture } = await setup((host) => [
        { id: 'c', label: 'Copy', shortcut: 'Mod+C', action: host.act('c') },
      ]);
      await open(fixture);

      const caps = row('Copy').querySelector('tm-kbd')!;
      expect(caps.getAttribute('aria-hidden')).toBe('true');
      expect(Array.from(caps.querySelectorAll('kbd')).map((cap) => cap.textContent)).toEqual([
        ...tmFormatKeys('Mod+C'),
      ]);
    });

    it('draws each icon form: a registry name, icon data, and a template', async () => {
      const { fixture } = await setup((host) => [
        { id: 'n', label: 'Named', icon: 'star', action: host.act('n') },
        { id: 'd', label: 'Data', icon: Star, action: host.act('d') },
        { id: 't', label: 'Template', icon: host.glyph(), action: host.act('t') },
        { id: 'x', label: 'None', action: host.act('x') },
      ]);
      await open(fixture);

      for (const label of ['Named', 'Data']) {
        const slot = row(label).querySelector('.tm-menu__icon')!;
        expect(slot.getAttribute('aria-hidden')).toBe('true');
        expect(slot.querySelector('tm-icon svg.tm-icon__svg')).not.toBeNull();
      }
      expect(row('Template').querySelector('.tm-menu__icon svg.template-glyph')).not.toBeNull();
      // The box is there even with no icon, so labels line up.
      const empty = row('None').querySelector('.tm-menu__icon')!;
      expect(empty.children.length).toBe(0);
    });

    it('opens on the last row when asked to', async () => {
      const { fixture, host } = await setup((h) => [
        { id: 'a', label: 'Alpha', action: h.act('a') },
        { id: 'b', label: 'Beta', action: h.act('b') },
      ]);
      host.menu().open({ x: 10, y: 10 }, { initialFocus: 'last' });
      await settle(fixture);

      expect(focusedLabel()).toBe('Beta');
    });

    it('moves DOM focus onto the first row as it opens', async () => {
      const { fixture } = await setup((host) => [
        { id: 'a', label: 'Alpha', action: host.act('a') },
        { id: 'b', label: 'Beta', action: host.act('b') },
      ]);
      await open(fixture);

      expect(focusedLabel()).toBe('Alpha');
      expect(row('Alpha').getAttribute('tabindex')).toBe('0');
      expect(row('Beta').getAttribute('tabindex')).toBe('-1');
    });
  });

  describe('typeahead', () => {
    it('matches labels only: never a detail, a description or a shortcut', async () => {
      const { fixture } = await setup((host) => [
        { id: 'a', label: 'Alpha', action: host.act('a') },
        {
          id: 'l',
          label: 'Language',
          detail: 'Zulu',
          description: 'Yours to choose',
          shortcut: 'Mod+Q',
          action: host.act('l'),
        },
        { id: 'b', label: 'Beta', action: host.act('b') },
      ]);
      await open(fixture);

      for (const key of ['z', 'y', 'q']) {
        press(key);
        await fixture.whenStable();
        expect(focusedLabel()).toBe('Alpha'); // nothing starts with it
        await sleep(520); // let the typeahead buffer clear
      }

      press('l');
      await fixture.whenStable();
      expect(focusedLabel()).toBe('Language');
    });
  });

  describe('disabledReason', () => {
    const entries = (host: RichHost): readonly TmMenuEntry[] => [
      { id: 'a', label: 'Alpha', action: host.act('a') },
      {
        id: 'p',
        label: 'Post',
        description: 'Never shown',
        disabledReason: 'The period is closed',
        action: host.act('p'),
      },
      { id: 'b', label: 'Beta', action: host.act('b') },
    ];

    it('renders aria-disabled with the reason as its second line and description', async () => {
      const { fixture } = await setup(entries);
      const menu = await open(fixture);

      const post = row('Post');
      expect(post.getAttribute('aria-disabled')).toBe('true');
      const line = post.querySelector('.tm-menu__description')!;
      expect(line.textContent!.trim()).toBe('The period is closed'); // in place of the description
      expect(post.getAttribute('aria-describedby')).toBe(line.id);
      expect(await menu.getDisabledReason('Post')).toBe('The period is closed');
      expect(await menu.getDisabledReason('Alpha')).toBeNull();
    });

    it('stays in the roving focus order and in typeahead', async () => {
      const { fixture } = await setup(entries);
      await open(fixture);

      press('ArrowDown');
      await fixture.whenStable();
      expect(focusedLabel()).toBe('Post');
      expect(row('Post').getAttribute('tabindex')).toBe('0');

      press('ArrowDown');
      await fixture.whenStable();
      expect(focusedLabel()).toBe('Beta');

      press('p');
      await fixture.whenStable();
      expect(focusedLabel()).toBe('Post');
    });

    it('does nothing on click, Enter or Space, and leaves the menu open', async () => {
      const { fixture, host } = await setup(entries);
      const menu = await open(fixture);

      await menu.clickItem('Post');
      await settle(fixture);
      expect(focusedLabel()).toBe('Post'); // a click still focuses the row
      press('Enter');
      await settle(fixture);
      press(' ');
      await settle(fixture);

      expect(host.log).toEqual([]);
      expect(host.selections).toEqual([]);
      expect(host.menu().isOpen()).toBe(true);
      expect(focusedLabel()).toBe('Post');
    });

    it('keeps a menu navigable when every row is disabled (aria list guard)', async () => {
      // Version-locked guard: @angular/aria reports a list whose items are
      // ALL disabled as a disabled list, which takes no focus and ignores
      // the arrow keys. tm-menu overrides that answer so each reason stays
      // reachable; an aria upgrade that moves the seam trips this spec.
      const { fixture } = await setup((host) => [
        { id: 'a', label: 'Alpha', disabledReason: 'Reason one', action: host.act('a') },
        { id: 'b', label: 'Beta', disabled: true, action: host.act('b') },
      ]);
      await open(fixture);

      expect(focusedLabel()).toBe('Alpha');
      expect(row('Alpha').getAttribute('tabindex')).toBe('0');
      press('ArrowDown');
      await fixture.whenStable();
      expect(focusedLabel()).toBe('Beta');
      expect(row('Beta').getAttribute('tabindex')).toBe('0');
      expect(row('Alpha').getAttribute('tabindex')).toBe('-1');
    });

    it('warns in development when an entry sets both disabled and disabledReason', async () => {
      const warn = vi.spyOn(console, 'warn').mockImplementation(() => undefined);
      const { fixture } = await setup((host) => [
        { id: 'both', label: 'Both', disabled: true, disabledReason: 'Why', action: host.act('x') },
      ]);
      await open(fixture);

      expect(warn).toHaveBeenCalledOnce();
      expect(warn.mock.calls[0][0]).toContain('"both"');
      warn.mockRestore();
    });
  });

  describe('checkable items', () => {
    it('binds menuitemcheckbox / menuitemradio with aria-checked, and draws the check in the icon box', async () => {
      const { fixture } = await setup((host) => [
        { id: 'w', label: 'Wrap', role: 'checkbox', checked: true, action: host.act('w') },
        { id: 'g', label: 'Grid', role: 'checkbox', action: host.act('g') },
        {
          group: true,
          items: [
            { id: 's', label: 'Small', role: 'radio', checked: false, action: host.act('s') },
            { id: 'l', label: 'Large', role: 'radio', checked: true, action: host.act('l') },
          ],
        },
      ]);
      const menu = await open(fixture);

      expect(row('Wrap').getAttribute('role')).toBe('menuitemcheckbox');
      expect(row('Small').getAttribute('role')).toBe('menuitemradio');
      expect(await menu.isChecked('Wrap')).toBe(true);
      expect(await menu.isChecked('Grid')).toBe(false);
      expect(await menu.isChecked('Small')).toBe(false);
      expect(await menu.isChecked('Large')).toBe(true);

      expect(row('Wrap').querySelector('.tm-menu__icon tm-icon svg')).not.toBeNull();
      expect(row('Grid').querySelector('.tm-menu__icon tm-icon')).toBeNull();
      expect(row('Large').querySelector('.tm-menu__icon tm-icon svg')).not.toBeNull();
    });

    it('isChecked refuses a row that is not checkable', async () => {
      const { fixture } = await setup((host) => [
        { id: 'a', label: 'Alpha', action: host.act('a') },
      ]);
      const menu = await open(fixture);

      expect(row('Alpha').hasAttribute('aria-checked')).toBe(false);
      await expect(menu.isChecked('Alpha')).rejects.toThrow(/not a checkbox or radio/);
    });

    it('keepOpen leaves the menu open on click, Enter and Space, and still reports the item', async () => {
      const wrap = signal(false);
      const { fixture, host } = await setup(() => []);
      const items = computed<readonly TmMenuEntry[]>(() => [
        {
          id: 'w',
          label: 'Wrap',
          role: 'checkbox',
          checked: wrap(),
          keepOpen: true,
          action: () => wrap.update((value) => !value),
        },
        { id: 'c', label: 'Close', action: host.act('c') },
      ]);
      const sync = () => host.items.set(items());
      sync();
      await settle(fixture);
      const menu = await open(fixture);

      await menu.clickItem('Wrap');
      sync();
      await settle(fixture);
      expect(host.menu().isOpen()).toBe(true);
      expect(await menu.isChecked('Wrap')).toBe(true);

      press('Enter');
      sync();
      await settle(fixture);
      expect(await menu.isChecked('Wrap')).toBe(false);

      const space = press(' ');
      sync();
      await settle(fixture);
      expect(space.defaultPrevented).toBe(true);
      expect(await menu.isChecked('Wrap')).toBe(true);

      expect(host.menu().isOpen()).toBe(true);
      expect(focusedLabel()).toBe('Wrap'); // the toggled row keeps focus
      expect(host.selections.map((item) => item.id)).toEqual(['w', 'w', 'w']);
    });

    it('warns in development about a radio item outside a group', async () => {
      const warn = vi.spyOn(console, 'warn').mockImplementation(() => undefined);
      const { fixture } = await setup((host) => [
        { id: 'loose', label: 'Loose', role: 'radio', action: host.act('x') },
        { group: true, items: [{ id: 'in', label: 'In', role: 'radio', action: host.act('y') }] },
      ]);
      await open(fixture);

      expect(warn).toHaveBeenCalledOnce();
      expect(warn.mock.calls[0][0]).toContain('"loose"');
      warn.mockRestore();
    });
  });

  describe('groups', () => {
    it('renders role=group named by its heading, with a divider above all but a leading group', async () => {
      const { fixture } = await setup((host) => [
        {
          group: true,
          label: 'First',
          items: [{ id: 'a', label: 'Alpha', action: host.act('a') }],
        },
        {
          group: true,
          label: 'Second',
          items: [{ id: 'b', label: 'Beta', action: host.act('b') }],
        },
        { id: 'c', label: 'Gamma', action: host.act('c') },
        { separator: true },
        { group: true, items: [{ id: 'd', label: 'Delta', action: host.act('d') }] },
      ]);
      const menu = await open(fixture);

      const groups = await menu.getGroups();
      expect(await Promise.all(groups.map((group) => group.getLabel()))).toEqual([
        'First',
        'Second',
        null,
      ]);
      expect(await groups[1].getItemLabels()).toEqual(['Beta']);
      expect(await menu.getItemLabels()).toEqual(['Alpha', 'Beta', 'Gamma', 'Delta']);

      const elements = Array.from(panel()!.querySelectorAll('.tm-menu__group'));
      expect(elements.every((group) => group.getAttribute('role') === 'group')).toBe(true);
      const heading = elements[0].querySelector('.tm-menu__heading')!;
      expect(elements[0].getAttribute('aria-labelledby')).toBe(heading.id);
      expect(heading.hasAttribute('tabindex')).toBe(false);
      expect(elements[2].hasAttribute('aria-labelledby')).toBe(false); // no heading, no name

      // One divider above the second group, the explicit one after Gamma,
      // and none doubled above the group that follows it.
      const kinds = Array.from(panel()!.querySelector('tm-menu-rows')!.children).map((child) =>
        child.classList.contains('tm-menu__separator')
          ? 'separator'
          : child.classList.contains('tm-menu__group')
            ? 'group'
            : 'item',
      );
      expect(kinds).toEqual(['group', 'separator', 'group', 'item', 'separator', 'group']);
    });

    it('opens a menu whose only rows sit in a group', async () => {
      const { fixture, host } = await setup((h) => [
        { group: true, items: [{ id: 'a', label: 'Alpha', action: h.act('a') }] },
      ]);
      await open(fixture);

      expect(host.menu().isOpen()).toBe(true);
      expect(focusedLabel()).toBe('Alpha');
    });
  });

  describe('links', () => {
    const entries = (host: RichHost): readonly TmMenuEntry[] => [
      { id: 'docs', label: 'Docs', href: '#docs' },
      { id: 'route', label: 'Settings', href: '#settings', action: host.act('route') },
      { id: 'ext', label: 'Status page', href: 'https://status.example/', target: '_blank' },
      { id: 'off', label: 'Billing', href: '#billing', disabledReason: 'Ask an administrator' },
      { id: 'item', label: 'Item', action: host.act('item') },
    ];

    it('renders an anchor with href, target and rel=noopener', async () => {
      const { fixture } = await setup(entries);
      const menu = await open(fixture);

      const docs = row('Docs');
      expect(docs.tagName).toBe('A');
      expect(docs.getAttribute('role')).toBe('menuitem');
      expect(docs.getAttribute('rel')).toBe('noopener');
      expect(docs.hasAttribute('target')).toBe(false);
      expect(await menu.getLinkHref('Docs')).toBe('#docs');
      expect(await menu.getLinkHref('Item')).toBeNull();

      const external = row('Status page');
      expect(external.getAttribute('target')).toBe('_blank');
      expect(external.querySelector('tm-icon.tm-menu__external svg')).not.toBeNull();
      // The note has no visible text: it is the tail of the row's name.
      expect(external.getAttribute('aria-label')).toBe('Status page (opens in a new tab)');
      expect(docs.hasAttribute('aria-label')).toBe(false);
      expect(docs.querySelector('.tm-menu__external')).toBeNull();
    });

    it('a disabled link carries no address and does nothing', async () => {
      const { fixture, host } = await setup(entries);
      const menu = await open(fixture);

      expect(await menu.getLinkHref('Billing')).toBeNull();
      expect(await menu.getDisabledReason('Billing')).toBe('Ask an administrator');
      clickLink(row('Billing'));
      await settle(fixture);
      expect(host.menu().isOpen()).toBe(true);
    });

    it('a plain click follows a link with no action, closes, and emits nothing', async () => {
      const { fixture, host } = await setup(entries);
      await open(fixture);

      const { prevented } = clickLink(row('Docs'));
      await settle(fixture);

      expect(prevented).toBe(false); // the browser navigates
      expect(host.menu().isOpen()).toBe(false);
      expect(host.selections).toEqual([]); // itemSelected is for items only
    });

    it('a plain click runs the action instead of navigating when the link has one', async () => {
      const { fixture, host } = await setup(entries);
      await open(fixture);

      const { prevented } = clickLink(row('Settings'));
      await settle(fixture);

      expect(prevented).toBe(true);
      expect(host.log).toEqual(['route']);
      expect(host.menu().isOpen()).toBe(false);
      expect(host.selections).toEqual([]);
    });

    it.each([
      ['Ctrl', { ctrlKey: true }],
      ['Meta', { metaKey: true }],
      ['Shift', { shiftKey: true }],
    ])('a %s-click stays native: no action, not cancelled, menu left open', async (_, init) => {
      const { fixture, host } = await setup(entries);
      await open(fixture);

      const { prevented } = clickLink(row('Settings'), init);
      await settle(fixture);

      expect(prevented).toBe(false);
      expect(host.log).toEqual([]);
      expect(host.menu().isOpen()).toBe(true);
    });

    it('a middle-click is left to the browser', async () => {
      const { fixture, host } = await setup(entries);
      await open(fixture);

      const { prevented } = clickLink(row('Settings'), { button: 1 }, 'auxclick');
      await settle(fixture);

      expect(prevented).toBe(false);
      expect(host.log).toEqual([]);
      expect(host.menu().isOpen()).toBe(true);
    });

    it('Enter is left to the browser, whose click then activates the link', async () => {
      const { fixture, host } = await setup(entries);
      await open(fixture);
      press('ArrowDown'); // Docs -> Settings
      await fixture.whenStable();
      expect(focusedLabel()).toBe('Settings');

      // aria would consume Enter (preventDefault), and the browser would
      // then never turn the key into a click on the anchor.
      const enter = press('Enter');
      await settle(fixture);
      expect(enter.defaultPrevented).toBe(false);
      expect(host.log).toEqual([]); // a synthetic keydown produces no click

      clickLink(row('Settings')); // what the browser does next
      await settle(fixture);
      expect(host.log).toEqual(['route']);
      expect(host.menu().isOpen()).toBe(false);
    });

    it('Space activates a link through its click', async () => {
      const { fixture, host } = await setup(entries);
      await open(fixture);
      press('ArrowDown');
      await fixture.whenStable();

      const space = press(' ');
      await settle(fixture);

      expect(space.defaultPrevented).toBe(true); // no page scroll
      expect(host.log).toEqual(['route']);
      expect(host.menu().isOpen()).toBe(false);
    });

    it('keeps the native context menu on links and suppresses it elsewhere', async () => {
      const { fixture } = await setup(entries);
      await open(fixture);

      const onLink = new MouseEvent('contextmenu', { bubbles: true, cancelable: true });
      row('Docs').querySelector('.tm-menu__label')!.dispatchEvent(onLink);
      expect(onLink.defaultPrevented).toBe(false);

      const onItem = new MouseEvent('contextmenu', { bubbles: true, cancelable: true });
      row('Item').dispatchEvent(onItem);
      expect(onItem.defaultPrevented).toBe(true);

      // A disabled link has no address to copy.
      const onDisabled = new MouseEvent('contextmenu', { bubbles: true, cancelable: true });
      row('Billing').dispatchEvent(onDisabled);
      expect(onDisabled.defaultPrevented).toBe(true);
    });
  });

  describe('submenus', () => {
    const entries = (host: RichHost): readonly TmMenuEntry[] => [
      { id: 'a', label: 'Alpha', action: host.act('a') },
      {
        id: 'lang',
        label: 'Language',
        detail: 'English',
        items: [
          {
            group: true,
            items: [
              { id: 'en', label: 'English', role: 'radio', checked: true, action: host.act('en') },
              { id: 'fr', label: 'Français', role: 'radio', lang: 'fr', action: host.act('fr') },
            ],
          },
          { separator: true },
          { id: 'pin', label: 'Pin', role: 'checkbox', keepOpen: true, action: host.act('pin') },
        ],
      },
      {
        id: 'exp',
        label: 'Export',
        disabledReason: 'Nothing to export',
        items: [{ id: 'csv', label: 'CSV', action: host.act('csv') }],
      },
      { id: 'z', label: 'Zeta', action: host.act('z') },
    ];

    it('marks the parent row and keeps the submenu out of sight until it expands', async () => {
      const { fixture } = await setup(entries);
      const menu = await open(fixture);

      const parent = row('Language');
      expect(parent.getAttribute('aria-haspopup')).toBe('true');
      expect(parent.getAttribute('aria-expanded')).toBe('false');
      expect(parent.querySelector('tm-icon.tm-menu__chevron svg')).not.toBeNull();
      expect(shownSubPanels()).toEqual([]);
      // The menu's own rows never include a submenu's.
      expect(await menu.getItemLabels()).toEqual(['Alpha', 'Language', 'Export', 'Zeta']);
      // A plain row is not a popup parent.
      expect(row('Alpha').querySelector('.tm-menu__chevron')).toBeNull();
    });

    it('names the submenu after its parent row and links the two', async () => {
      const { fixture } = await setup(entries);
      const menu = await open(fixture);
      const sub = await menu.openSubmenu('Language');

      const list = document.getElementById((await sub.getListId())!)!;
      expect(list.getAttribute('role')).toBe('menu');
      expect(list.getAttribute('aria-label')).toBe('Language');
      expect(row('Language').getAttribute('aria-controls')).toBe(list.id);
      expect(await sub.getItemLabels()).toEqual(['English', 'Français', 'Pin']);
      expect(await (await sub.getGroups())[0].getItemLabels()).toEqual(['English', 'Français']);
    });

    it.each([['ArrowRight'], ['Enter'], [' ']])(
      'opens with %j and moves focus to its first row',
      async (key) => {
        const { fixture } = await setup(entries);
        await open(fixture);
        press('ArrowDown'); // Language
        await fixture.whenStable();

        press(key);
        await settle(fixture);

        expect(row('Language').getAttribute('aria-expanded')).toBe('true');
        expect(shownSubPanels().length).toBe(1);
        expect(focusedLabel()).toBe('English');
      },
    );

    it('ArrowLeft closes the submenu and returns focus to its parent row', async () => {
      const { fixture, host } = await setup(entries);
      await open(fixture);
      press('ArrowDown');
      await fixture.whenStable();
      press('ArrowRight');
      await settle(fixture);

      press('ArrowLeft');
      await settle(fixture);

      expect(shownSubPanels()).toEqual([]);
      expect(focusedLabel()).toBe('Language');
      expect(host.menu().isOpen()).toBe(true);
    });

    it('Escape closes one level per press', async () => {
      const { fixture, host } = await setup(entries);
      await open(fixture);
      press('ArrowDown');
      await fixture.whenStable();
      press('ArrowRight');
      await settle(fixture);

      press('Escape');
      await settle(fixture);
      expect(shownSubPanels()).toEqual([]);
      expect(host.menu().isOpen()).toBe(true);
      expect(focusedLabel()).toBe('Language');

      press('Escape');
      await settle(fixture);
      expect(host.menu().isOpen()).toBe(false);
      expect(document.activeElement).toBe(host.trigger().nativeElement);
    });

    it('mirrors its keys under a right-to-left direction, live', async () => {
      const { fixture } = await setup(entries);
      document.documentElement.dir = 'rtl';
      await settle(fixture);
      await open(fixture);
      press('ArrowDown');
      await fixture.whenStable();

      press('ArrowRight'); // the collapse key in RTL: nothing to collapse
      await settle(fixture);
      expect(shownSubPanels()).toEqual([]);

      press('ArrowLeft');
      await settle(fixture);
      expect(shownSubPanels().length).toBe(1);
      expect(focusedLabel()).toBe('English');

      press('ArrowRight');
      await settle(fixture);
      expect(shownSubPanels()).toEqual([]);
      expect(focusedLabel()).toBe('Language');
    });

    it('opens on hover after the delay without taking focus, and closes when the pointer moves on', async () => {
      const { fixture } = await setup(entries);
      await open(fixture);

      row('Language').dispatchEvent(new MouseEvent('mouseover', { bubbles: true }));
      await fixture.whenStable();
      expect(shownSubPanels()).toEqual([]); // not before the delay
      await sleep(160);
      await settle(fixture);

      expect(shownSubPanels().length).toBe(1);
      // Hover opens it; focus stays on the parent row, not inside.
      expect(focusedLabel()).toBe('Language');

      row('Zeta').dispatchEvent(new MouseEvent('mouseover', { bubbles: true }));
      await sleep(160);
      await settle(fixture);
      expect(shownSubPanels()).toEqual([]);
    });

    it('opens on a click (a tap) and stays open', async () => {
      const { fixture, host } = await setup(entries);
      const menu = await open(fixture);

      const sub = await menu.openSubmenu('Language');
      await settle(fixture);

      expect(shownSubPanels().length).toBe(1);
      expect(host.menu().isOpen()).toBe(true);
      expect(host.selections).toEqual([]); // a parent row is never "selected"
      expect(await sub.isChecked('English')).toBe(true);
    });

    it('an activation inside closes the whole tree, reports the item, and restores focus', async () => {
      const { fixture, host } = await setup(entries);
      const menu = await open(fixture);
      const sub = await menu.openSubmenu('Language');

      await sub.clickItem('Français');
      await settle(fixture);

      expect(host.log).toEqual(['fr']);
      expect(host.selections.map((item) => item.id)).toEqual(['fr']);
      expect(host.menu().isOpen()).toBe(false);
      expect(document.querySelector('.tm-menu__panel')).toBeNull();
      expect(document.activeElement).toBe(host.trigger().nativeElement);
    });

    it('a keepOpen item inside leaves the submenu open too', async () => {
      const { fixture, host } = await setup(entries);
      const menu = await open(fixture);
      const sub = await menu.openSubmenu('Language');

      await sub.clickItem('Pin');
      await settle(fixture);
      expect(host.log).toEqual(['pin']);
      expect(shownSubPanels().length).toBe(1);

      press('End');
      await fixture.whenStable();
      expect(focusedLabel()).toBe('Pin');
      press('Enter');
      await settle(fixture);
      expect(host.log).toEqual(['pin', 'pin']);
      expect(shownSubPanels().length).toBe(1);
      expect(host.menu().isOpen()).toBe(true);
    });

    it('a parent row disabled with a reason never opens its submenu', async () => {
      const { fixture } = await setup(entries);
      const menu = await open(fixture);

      expect(await menu.getDisabledReason('Export')).toBe('Nothing to export');
      await expect(menu.openSubmenu('Export')).rejects.toThrow(/did not open/);

      press('End');
      await fixture.whenStable();
      press('ArrowUp'); // Export
      await fixture.whenStable();
      expect(focusedLabel()).toBe('Export');
      for (const key of ['ArrowRight', 'Enter', ' ']) {
        press(key);
        await settle(fixture);
      }
      row('Export').dispatchEvent(new MouseEvent('mouseover', { bubbles: true }));
      await sleep(160);
      await settle(fixture);

      expect(shownSubPanels()).toEqual([]);
      expect(row('Export').getAttribute('aria-expanded')).toBe('false');
    });

    it('openSubmenu refuses a row with no submenu', async () => {
      const { fixture } = await setup(entries);
      const menu = await open(fixture);

      await expect(menu.openSubmenu('Alpha')).rejects.toThrow(/has no submenu/);
    });

    it('refuses a submenu inside a submenu: a development error and a disabled row', async () => {
      const error = vi.spyOn(console, 'error').mockImplementation(() => undefined);
      const { fixture } = await setup((host) => [
        {
          id: 'outer',
          label: 'Outer',
          items: [
            {
              group: true,
              items: [
                {
                  id: 'inner',
                  label: 'Inner',
                  items: [{ id: 'deep', label: 'Deep', action: host.act('deep') }],
                },
              ],
            },
          ],
        },
      ]);
      const menu = await open(fixture);
      const sub = await menu.openSubmenu('Outer');

      expect(error).toHaveBeenCalledOnce();
      expect(error.mock.calls[0][0]).toContain('"inner"');
      const inner = (await sub.getItems())[0];
      expect(await inner.isDisabled()).toBe(true);
      expect(await inner.hasSubmenu()).toBe(false);
      expect(document.querySelectorAll('.tm-menu__panel--sub').length).toBe(1);
      error.mockRestore();
    });
  });

  describe('header', () => {
    it('renders above the list, outside role=menu, and describes the menu', async () => {
      const { fixture, host } = await setup((h) => [
        { id: 'a', label: 'Alpha', action: h.act('a') },
      ]);
      host.header.set('Signed in as Rana');
      await settle(fixture);
      const menu = await open(fixture);

      expect(await menu.getHeaderText()).toBe('Signed in as Rana');
      const block = panel()!.querySelector('.tm-menu__header')!;
      const list = panel()!.querySelector('.tm-menu__list')!;
      expect(list.contains(block)).toBe(false);
      expect(block.compareDocumentPosition(list) & Node.DOCUMENT_POSITION_FOLLOWING).not.toBe(0);
      expect(list.getAttribute('aria-describedby')).toBe(block.id);
    });

    it('a menu with no header has no description', async () => {
      const { fixture } = await setup((h) => [{ id: 'a', label: 'Alpha', action: h.act('a') }]);
      const menu = await open(fixture);

      expect(await menu.getHeaderText()).toBeNull();
      expect(panel()!.querySelector('.tm-menu__list')!.hasAttribute('aria-describedby')).toBe(
        false,
      );
    });

    it('warns in development when the header holds interactive content', async () => {
      const warn = vi.spyOn(console, 'warn').mockImplementation(() => undefined);
      const { fixture, host } = await setup((h) => [
        { id: 'a', label: 'Alpha', action: h.act('a') },
      ]);
      host.header.set('Signed in as Rana');
      host.interactiveHeader.set(true);
      await settle(fixture);
      await open(fixture);

      expect(warn).toHaveBeenCalled();
      expect(warn.mock.calls[0][0]).toContain('tmMenuHeader');
      warn.mockRestore();
    });
  });
});

describe('tmMenuTriggerFor', () => {
  const entries = (host: RichHost): readonly TmMenuEntry[] => [
    { id: 'a', label: 'Alpha', action: host.act('a') },
    { id: 'b', label: 'Beta', action: host.act('b') },
    { id: 'c', label: 'Gamma', action: host.act('c') },
  ];

  async function triggerHarness(
    fixture: ComponentFixture<RichHost>,
  ): Promise<TmMenuTriggerHarness> {
    return TestbedHarnessEnvironment.loader(fixture).getHarness(TmMenuTriggerHarness);
  }

  it('carries the menu-button attributes, closed and open', async () => {
    const { fixture, host } = await setup(entries);
    const button = host.trigger().nativeElement;
    const trigger = await triggerHarness(fixture);

    expect(button.getAttribute('aria-haspopup')).toBe('menu');
    expect(button.getAttribute('aria-expanded')).toBe('false');
    expect(button.hasAttribute('aria-controls')).toBe(false);
    expect(await trigger.isOpen()).toBe(false);
    await expect(trigger.getMenu()).rejects.toThrow(/no open menu/);

    await trigger.open();
    await settle(fixture);

    expect(button.getAttribute('aria-expanded')).toBe('true');
    const menu = await trigger.getMenu();
    expect(button.getAttribute('aria-controls')).toBe(await menu.getListId());
    expect(
      document.getElementById(button.getAttribute('aria-controls')!)!.getAttribute('role'),
    ).toBe('menu');
    expect(await menu.getItemLabels()).toEqual(['Alpha', 'Beta', 'Gamma']);
  });

  it('a click toggles, and opening puts focus on the first row', async () => {
    const { fixture, host } = await setup(entries);
    const trigger = await triggerHarness(fixture);

    await trigger.toggle();
    await settle(fixture);
    expect(host.menu().isOpen()).toBe(true);
    expect(focusedLabel()).toBe('Alpha');

    // The press that precedes the click must not dismiss the menu, or the
    // click would find it closed and re-open it.
    const button = host.trigger().nativeElement;
    button.dispatchEvent(new PointerEvent('pointerdown', { bubbles: true }));
    expect(host.menu().isOpen()).toBe(true);
    await trigger.toggle();
    await settle(fixture);
    expect(host.menu().isOpen()).toBe(false);
    expect(await trigger.isOpen()).toBe(false);
    expect(document.activeElement).toBe(button);
  });

  it('ArrowDown opens on the first row and ArrowUp on the last', async () => {
    const { fixture, host } = await setup(entries);
    const button = host.trigger().nativeElement;
    button.focus();

    const down = press('ArrowDown');
    await settle(fixture);
    expect(down.defaultPrevented).toBe(true);
    expect(focusedLabel()).toBe('Alpha');

    press('Escape');
    await settle(fixture);
    expect(host.menu().isOpen()).toBe(false);
    expect(document.activeElement).toBe(button);

    press('ArrowUp');
    await settle(fixture);
    expect(focusedLabel()).toBe('Gamma');
  });

  it('returns focus to the button after an activation', async () => {
    const { fixture, host } = await setup(entries);
    const trigger = await triggerHarness(fixture);
    await trigger.open();
    await settle(fixture);

    press('ArrowDown');
    await fixture.whenStable();
    press('Enter');
    await settle(fixture);

    expect(host.log).toEqual(['b']);
    expect(host.menu().isOpen()).toBe(false);
    expect(document.activeElement).toBe(host.trigger().nativeElement);
  });

  it('reports expanded only for the opening it made itself', async () => {
    const { fixture, host } = await setup(entries);
    const button = host.trigger().nativeElement;
    host.menu().open(host.other().nativeElement);
    await settle(fixture);

    expect(host.menu().isOpen()).toBe(true);
    expect(button.getAttribute('aria-expanded')).toBe('false');
    expect(button.hasAttribute('aria-controls')).toBe(false);

    // A click now moves the menu to the button rather than closing it.
    button.click();
    await settle(fixture);
    expect(host.menu().isOpen()).toBe(true);
    expect(button.getAttribute('aria-expanded')).toBe('true');
  });

  it('a button that is disabled or aria-disabled does not open its menu', async () => {
    const { fixture, host } = await setup(entries);
    const button = host.trigger().nativeElement;

    button.setAttribute('aria-disabled', 'true');
    button.click();
    button.focus();
    const down = press('ArrowDown');
    await settle(fixture);
    expect(host.menu().isOpen()).toBe(false);
    expect(down.defaultPrevented).toBe(false);

    button.removeAttribute('aria-disabled');
    button.click();
    await settle(fixture);
    expect(host.menu().isOpen()).toBe(true);
  });
});

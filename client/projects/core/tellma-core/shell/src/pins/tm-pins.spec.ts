// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { TestBed } from '@angular/core/testing';
import { type TmLaunchDocs, TmPreferences, tmPinnedBarVisibleKey, tmPinsKey, type TmPinItem, type TmPinsDoc } from '@tellma/core';

import { type TmAppRig, tmAppRig, tmNavigate, tmSettle, TM_T17_DOCS } from '../../../src/routes/tm-app.testing.util';
import { TM_PIN_LIMITS } from './tm-pin-rules';
import { type TmPinFolderView, TmPins, tmPinsVia, type TmPinView } from './tm-pins';

// The user's pins in a whole test app: each edit's next stored document and
// the telemetry it reports, with how it was made and the count's bucket;
// the routed page's pin and why a page cannot be pinned; the bar's switch;
// the views' signals kept across writes; and the reason a frozen tenant
// gives.

/** A pins document of items. */
function doc(...items: readonly unknown[]): TmPinsDoc {
  return { v: 1, items: items as readonly TmPinItem[] };
}

/** The stored pins: a pin, a folder of two, and a pin no route opens. */
const STORED = doc(
  { id: 'a0000001', url: 'centers', title: 'Cost centres' },
  {
    id: 'f0000001',
    folder: 'Daily',
    items: [
      { id: 'a0000002', url: 'customers', title: 'Customers' },
      { id: 'a0000003', url: 'approvals', title: 'Approvals' },
    ],
  },
  { id: 'a0000004', url: 'gone', title: 'Gone' },
);

/** The distribution's strings. */
const STRINGS = { en: { Acme_CreditReview: 'Credit review', acme_Customer_Plural: 'Customers', Acme_Approvals: 'Approvals' } };

/** Tenant 17's documents, with stored pins and preferences, and the tenant's state. */
function docs(pins: TmPinsDoc = STORED, preferences: Readonly<Record<string, string>> = {}, state: TmLaunchDocs['tenant']['state'] = 'Active'): TmLaunchDocs {
  const access = {
    ...TM_T17_DOCS.me.access,
    securables: [
      { resource: 'gl.Center', action: 'Read', hasFilter: false },
      { resource: 'acme.Customer', action: 'Read', hasFilter: false },
    ],
  };
  return {
    ...TM_T17_DOCS,
    tenant: { ...TM_T17_DOCS.tenant, state },
    me: { ...TM_T17_DOCS.me, access, preferences: { 'nav.pinned': JSON.stringify(pins), ...preferences } },
  };
}

/** The ids of a document's items, a folder's as `id[pin ids]`. */
function ids(view: TmPinsDoc): string[] {
  return view.items.map((item) => ('folder' in item ? `${item.id}[${item.items.map((inner) => inner.id).join(' ')}]` : item.id));
}

describe('the pins service', () => {
  let rig: TmAppRig;

  afterEach(() => TestBed.resetTestingModule());

  /** Starts the app at a URL. */
  async function start(url = '/17/credit-review', options: { readonly docs?: TmLaunchDocs } = {}): Promise<TmPins> {
    rig = await tmAppRig({ docs: options.docs ?? docs(), strings: STRINGS });
    await tmNavigate(url);
    await tmSettle();
    return TestBed.inject(TmPins);
  }

  /** The stored document. */
  const stored = (): TmPinsDoc => TestBed.inject(TmPreferences).get(tmPinsKey)();

  /** The properties of the events of a name. */
  const events = (name: string): unknown[] =>
    rig.telemetry
      .recent()
      .filter((item) => item.name === name)
      .map((item) => item.props);

  describe('the items', () => {
    it("lists the top level in order, each pin with its label, state and current flag, the current one the routed page's", async () => {
      const pins = await start('/17/customers?page=3');
      const [first, folder, last] = pins.items() as [TmPinView, TmPinFolderView, TmPinView];
      expect([first.id, first.href, first.folderId, first.label(), first.state(), first.current()]).toEqual(['a0000001', '/17/centers', null, 'Centers', 'ok', false]);
      expect(folder.folder).toBe('Daily');
      expect(folder.items.map((pin) => [pin.id, pin.folderId, pin.label(), pin.state(), pin.current()])).toEqual([
        ['a0000002', 'f0000001', 'Customers', 'ok', true],
        ['a0000003', 'f0000001', 'Approvals', 'no-access', false],
      ]);
      expect([last.label(), last.state()]).toEqual(['Gone', 'unavailable']);
      expect(pins.current()?.id).toBe('a0000002');
    });

    it('keeps each unchanged pin\'s view, and its signals, across a write', async () => {
      const pins = await start();
      const before = pins.items();
      pins.update('a0000001', { label: 'Mine' });
      const after = pins.items();
      expect(after[0]).not.toBe(before[0]);
      expect((after[1] as TmPinFolderView).items[0]).toBe((before[1] as TmPinFolderView).items[0]);
      expect(after[2]).toBe(before[2]);
    });
  });

  describe('the edits', () => {
    it('pins the routed page at the end with its path, its query without page and its title, reporting the add', async () => {
      const pins = await start('/17/credit-review?q=acme&page=2');
      expect(pins.canPinCurrent()).toBe(true);
      const pin = pins.pinCurrent();
      expect(stored().items.at(-1)).toEqual({ id: pin.id, url: 'credit-review?q=acme', title: 'Credit review' });
      expect(pin.id).toMatch(/^[0-9a-hjkmnp-tv-z]{8}$/);
      expect([pin.current(), pins.current()?.id]).toEqual([true, pin.id]);
      expect(events('tm.shell.pins-changed')).toEqual([{ op: 'add', via: 'control', count: '1-5' }]);
    });

    it('renames and files a pin, reporting each change', async () => {
      const pins = await start();
      pins.update('a0000001', { label: '  My centers ' });
      expect(stored().items[0]).toEqual({ id: 'a0000001', url: 'centers', title: 'Cost centres', label: 'My centers' });
      pins.update('a0000001', { label: '', folderId: 'f0000001' });
      expect(ids(stored())).toEqual(['f0000001[a0000002 a0000003 a0000001]', 'a0000004']);
      expect((stored().items[0] as { items: readonly unknown[] }).items[2]).toEqual({ id: 'a0000001', url: 'centers', title: 'Cost centres' });
      pins.update('a0000001', { folderId: 'f0000001' });
      expect(events('tm.shell.pins-changed')).toEqual([
        { op: 'rename', via: 'control', count: '1-5' },
        { op: 'rename', via: 'control', count: '1-5' },
        { op: 'move', via: 'control', count: '1-5' },
      ]);
      expect(() => pins.update('a0000002', { label: 'x'.repeat(TM_PIN_LIMITS.label + 1) })).toThrow(RangeError);
    });

    it('moves, removes, adds and removes folders, each with its document and its report', async () => {
      const pins = await start();
      tmPinsVia('drag', () => pins.move('a0000004', { folderId: null, index: 0 }));
      expect(ids(stored())).toEqual(['a0000004', 'a0000001', 'f0000001[a0000002 a0000003]']);
      tmPinsVia('menu', () => pins.remove('a0000004'));
      expect(ids(stored())).toEqual(['a0000001', 'f0000001[a0000002 a0000003]']);
      const weekly = tmPinsVia('popover', () => pins.addFolder(' Weekly '));
      expect(stored().items.at(-1)).toEqual({ id: weekly, folder: 'Weekly', items: [] });
      expect(() => pins.addFolder('daily')).toThrow(RangeError);
      tmPinsVia('dialog', () => pins.removeFolder('f0000001'));
      expect(ids(stored())).toEqual(['a0000001', 'a0000002', 'a0000003', `${weekly}[]`]);
      // An edit that changes nothing writes and reports nothing.
      pins.remove('zzzzzzzz');
      pins.move('a0000001', { folderId: null, index: 0 });
      expect(events('tm.shell.pins-changed')).toEqual([
        { op: 'move', via: 'drag', count: '1-5' },
        { op: 'remove', via: 'menu', count: '1-5' },
        { op: 'folder', via: 'popover', count: '1-5' },
        { op: 'folder', via: 'dialog', count: '1-5' },
      ]);
    });

    it('replaces the document, dropping what breaks the rules, as Save does', async () => {
      const pins = await start();
      tmPinsVia('dialog', () => pins.replace(doc({ id: 'b0000001', url: 'customers', title: 'Customers' }, { id: 'BAD', url: 'x', title: 'x' })));
      expect(stored()).toEqual(doc({ id: 'b0000001', url: 'customers', title: 'Customers' }));
      tmPinsVia('dialog', () => pins.replace(doc()));
      expect(events('tm.shell.pins-changed')).toEqual([
        { op: 'replace', via: 'dialog', count: '1-5' },
        { op: 'replace', via: 'dialog', count: '0' },
      ]);
    });

    it("buckets the count the change leaves", async () => {
      const many = doc(...Array.from({ length: 16 }, (_, n) => ({ id: `c${String(n).padStart(7, '0')}`, url: 'customers', title: `Pin ${n}` })));
      const pins = await start('/17/credit-review', { docs: docs(many) });
      pins.pinCurrent();
      tmPinsVia('menu', () => pins.remove('c0000000'));
      expect(events('tm.shell.pins-changed').map((props) => (props as { count: string }).count)).toEqual(['16+', '16+']);
    });
  });

  describe('refusals', () => {
    it('refuses a page with no pinnable route', async () => {
      const pins = await start('/17/nothing-here');
      expect([pins.canPinCurrent(), pins.ɵrefusal()]).toEqual([false, 'unpinnable']);
      expect(() => pins.pinCurrent()).toThrow(Error);
    });

    it("refuses a link past 512 characters, and a 31st pin", async () => {
      let pins = await start(`/17/credit-review?q=${'x'.repeat(TM_PIN_LIMITS.url)}`);
      expect(pins.ɵrefusal()).toBe('long');
      TestBed.resetTestingModule();
      const full = doc(...Array.from({ length: TM_PIN_LIMITS.pins }, (_, n) => ({ id: `c${String(n).padStart(7, '0')}`, url: 'customers', title: `Pin ${n}` })));
      pins = await start('/17/credit-review', { docs: docs(full) });
      expect(pins.ɵrefusal()).toBe('full');
      expect(() => pins.pinCurrent()).toThrow(Error);
      expect(stored().items).toHaveLength(TM_PIN_LIMITS.pins);
    });

    it("names the tenant while it is frozen, and nothing while it takes writes", async () => {
      let pins = await start('/17/credit-review', { docs: docs(STORED, {}, 'Frozen') });
      expect(pins.ɵfrozen()).toContain('Acme');
      TestBed.resetTestingModule();
      pins = await start();
      expect(pins.ɵfrozen()).toBeNull();
    });
  });

  describe('the bar', () => {
    it('shows the bar by default, and hides it, reporting the switch once', async () => {
      const pins = await start();
      expect(pins.visible()).toBe(true);
      pins.setVisible(false);
      pins.setVisible(false);
      expect([pins.visible(), TestBed.inject(TmPreferences).get(tmPinnedBarVisibleKey)()]).toEqual([false, false]);
      pins.setVisible(true);
      expect(events('tm.shell.pinned-bar-toggled')).toEqual([{ visible: false }, { visible: true }]);
    });
  });
});

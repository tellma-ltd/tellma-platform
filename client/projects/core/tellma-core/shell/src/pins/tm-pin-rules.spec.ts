// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { TestBed } from '@angular/core/testing';

import { TmNavigation, TmPreferences, tmPinsKey, type TmPin, type TmPinFolder, type TmPinItem, type TmPinsDoc } from '@tellma/core';

import { tmAppRig, tmNavigate, tmSettle, TM_T17_DOCS } from '../../../src/routes/tm-app.testing.util';
import { TM_PIN_LIMITS, tmCurrentPin, tmNewPinId, tmPinHref, tmPinLabel, tmPinsView, tmPinUrl } from './tm-pin-rules';

// The pinned bar's rules: the view's validation and caps, what a pin
// captures of a routed URL, the current-pin and label rules and the id
// generator; then stored pins in a whole test app, judged by the navigation
// registry.

/** A pin of an id and a URL, with the members a test sets. */
function pin(id: string, url: string, patch: Partial<TmPin> = {}): TmPin {
  return { id, url, title: `Title ${id}`, ...patch };
}

/** A folder of pins. */
function folder(id: string, name: string, items: readonly TmPin[]): TmPinFolder {
  return { id, folder: name, items };
}

/** A pins document of items. */
function doc(...items: readonly unknown[]): TmPinsDoc {
  return { v: 1, items: items as readonly TmPinItem[] };
}

/** An 8-character id from a number. */
function id(n: number): string {
  return `p${String(n).padStart(7, '0')}`;
}

/** The ids of a document's items, a folder's as `id[pin ids]`. */
function ids(view: TmPinsDoc): string[] {
  return view.items.map((item) => ('folder' in item ? `${item.id}[${item.items.map((inner) => inner.id).join(' ')}]` : item.id));
}

describe("the pinned bar's rules", () => {
  describe('the view', () => {
    it('returns a valid document itself', () => {
      const valid = doc(pin('a1b2c3d4', 'customers'), folder('f1b2c3d4', 'Daily', [pin('b1b2c3d4', 'centers?f.CenterType=in:Service', { label: 'Service' })]));
      expect(tmPinsView(valid)).toBe(valid);
    });

    it('drops the items that break a rule and keeps the rest in order', () => {
      const view = tmPinsView(
        doc(
          pin('a0000001', 'customers'),
          pin('A0000002', 'customers'),
          pin('a000003', 'customers'),
          pin('a0000004', '/17/customers'),
          pin('a0000005', 'x'.repeat(TM_PIN_LIMITS.url + 1)),
          pin('a0000006', 'x'.repeat(TM_PIN_LIMITS.url)),
          pin('a0000007', 'customers', { title: 'é'.repeat(TM_PIN_LIMITS.title + 1) }),
          // Lengths count characters, so 80 emoji fit.
          pin('a0000008', 'customers', { title: '📌'.repeat(TM_PIN_LIMITS.title) }),
          pin('a0000009', 'customers', { label: '   ' }),
          pin('a0000010', 'customers', { label: 'x'.repeat(TM_PIN_LIMITS.label + 1) }),
          pin('a0000001', 'centers'),
          'customers',
          null,
          { id: 'a0000011', url: 'customers' },
        ),
      );
      expect(ids(view)).toEqual(['a0000001', 'a0000006', 'a0000008']);
    });

    it('keeps folders one level deep, with names of 1 to 40 characters and valid pins', () => {
      const view = tmPinsView(
        doc(
          folder('f0000001', 'Daily', [pin('a0000001', 'customers'), pin('a0000002', '/x'), folder('f0000009', 'Nested', []) as unknown as TmPin]),
          folder('f0000002', ' ', []),
          folder('f0000003', 'x'.repeat(TM_PIN_LIMITS.folder + 1), []),
          { id: 'f0000004', folder: 'No items' },
          folder('f0000005', 'Repeats an id', [pin('a0000001', 'centers'), pin('f0000001', 'centers')]),
        ),
      );
      expect(ids(view)).toEqual(['f0000001[a0000001]', 'f0000005[]']);
    });

    it('keeps the first 30 pins, in folders too, and the first 10 folders', () => {
      const loose = Array.from({ length: 20 }, (_, n) => pin(id(n), 'customers'));
      const filed = folder('f0000000', 'Filed', Array.from({ length: 15 }, (_, n) => pin(id(100 + n), 'centers')));
      const view = tmPinsView(doc(...loose, filed, pin(id(200), 'customers')));
      expect(view.items).toHaveLength(21);
      expect((view.items[20] as TmPinFolder).items.map((inner) => inner.id)).toEqual(Array.from({ length: 10 }, (_, n) => id(100 + n)));

      const folders = Array.from({ length: 12 }, (_, n) => folder(`f${String(n).padStart(7, '0')}`, `Folder ${n}`, []));
      expect(tmPinsView(doc(...folders)).items).toHaveLength(TM_PIN_LIMITS.folders);
    });
  });

  describe('capture', () => {
    it('keeps the path after the tenant, the query without page and the #d= fragment', () => {
      expect(tmPinUrl('/17/customers?f.CustomerType=in:Corporate&page=3')).toBe('customers?f.CustomerType=in:Corporate');
      expect(tmPinUrl('/17/customers?page=2')).toBe('customers');
      expect(tmPinUrl('/17/centers?view=tree&pages=2&p.AsOf=2026-11-01&child=7')).toBe('centers?view=tree&pages=2&p.AsOf=2026-11-01&child=7');
      expect(tmPinUrl('/17/list/sales-invoice-lines?page=2#d=eyJm')).toBe('list/sales-invoice-lines#d=eyJm');
      expect(tmPinUrl('/17/customers#history')).toBe('customers');
      expect(tmPinUrl('/17')).toBe('');
      expect(tmPinUrl('/17?view=board')).toBe('?view=board');
    });

    it('captures nothing outside a tenant', () => {
      expect(tmPinUrl('/tenants')).toBeNull();
      expect(tmPinUrl('/0/customers')).toBeNull();
      expect(tmPinUrl('customers')).toBeNull();
    });

    it("opens a pin in the routed tenant, the home page's at the tenant's root", () => {
      expect(tmPinHref(17, 'customers?q=acme')).toBe('/17/customers?q=acme');
      expect(tmPinHref(17, '')).toBe('/17');
      expect(tmPinHref(17, '?view=board')).toBe('/17?view=board');
      expect(tmPinHref(17, '#d=eyJm')).toBe('/17#d=eyJm');
    });
  });

  describe('the current pin', () => {
    const items: readonly TmPinItem[] = [
      pin('a0000001', 'customers'),
      folder('f0000001', 'Daily', [pin('a0000002', 'customers?f.CustomerType=in:Corporate'), pin('a0000003', 'centers?view=tree')]),
      pin('a0000004', 'customers?f.CustomerType=in:Corporate'),
    ];

    it('takes the first pin whose URL is the capture, in folders too', () => {
      expect(tmCurrentPin(items, 'customers?f.CustomerType=in:Corporate')?.id).toBe('a0000002');
      expect(tmCurrentPin(items, 'customers')?.id).toBe('a0000001');
    });

    it('falls back to a pin of the same path with no query or fragment', () => {
      expect(tmCurrentPin(items, 'customers?q=acme')?.id).toBe('a0000001');
      expect(tmCurrentPin(items, 'centers')).toBeNull();
      expect(tmCurrentPin(items, 'centers?view=list')).toBeNull();
      expect(tmCurrentPin(items, null)).toBeNull();
    });
  });

  describe('labels', () => {
    const entryLabel = (href: string): string | undefined => (href === '/17/centers' ? 'Centers' : undefined);

    it("takes the user's label, else a plain URL's entry label, else the stored title", () => {
      expect(tmPinLabel(pin('a0000001', 'centers', { label: 'My centers' }), entryLabel, 17)).toBe('My centers');
      expect(tmPinLabel(pin('a0000002', 'centers', { title: 'Cost centres' }), entryLabel, 17)).toBe('Centers');
      expect(tmPinLabel(pin('a0000003', 'centers?view=tree', { title: 'Centers tree' }), entryLabel, 17)).toBe('Centers tree');
      expect(tmPinLabel(pin('a0000004', 'gone', { title: 'Gone' }), entryLabel, 17)).toBe('Gone');
    });
  });

  describe('ids', () => {
    afterEach(() => vi.restoreAllMocks());

    it('creates 8 base32 characters that no item holds', () => {
      const fill = (value: number) => (array: Uint8Array) => array.fill(value);
      vi.spyOn(crypto, 'getRandomValues')
        .mockImplementationOnce(fill(0) as typeof crypto.getRandomValues)
        .mockImplementationOnce(fill(31 + 32) as typeof crypto.getRandomValues);
      expect(tmNewPinId(new Set(['00000000']))).toBe('zzzzzzzz');
      vi.restoreAllMocks();
      for (let n = 0; n < 50; n++) {
        expect(tmNewPinId(new Set())).toMatch(/^[0-9a-hjkmnp-tv-z]{8}$/);
      }
    });
  });

  describe('in the app', () => {
    it('opens stored pins, a drill-through list included, and judges each by the navigation registry', async () => {
      const stored = doc(
        pin('a0000001', 'centers', { title: 'Cost centres' }),
        pin('a0000002', 'list/sales-invoice-lines#d=eyJm', { title: 'Invoice lines of Acme' }),
        pin('a0000003', 'customers?f.CustomerType=in:Corporate', { title: 'Corporate customers' }),
        pin('a0000004', 'gone', { title: 'Gone' }),
      );
      const access = {
        ...TM_T17_DOCS.me.access,
        securables: [
          { resource: 'gl.Center', action: 'Read', hasFilter: false },
          { resource: 'acme.SalesInvoiceLine', action: 'Read', hasFilter: false },
        ],
      };
      const me = { ...TM_T17_DOCS.me, access, preferences: { 'nav.pinned': JSON.stringify(stored) } };
      await tmAppRig({ docs: { ...TM_T17_DOCS, me } });
      await tmNavigate('/17/centers');
      await tmSettle();
      const navigation = TestBed.inject(TmNavigation);
      const view = tmPinsView(TestBed.inject(TmPreferences).get(tmPinsKey)());
      expect(
        view.items.map((item) => {
          const shown = item as TmPin;
          return [tmPinLabel(shown, (href) => navigation.byHref(href)?.label, 17), navigation.resolve(tmPinHref(17, shown.url))];
        }),
      ).toEqual([
        ['Centers', 'ok'],
        ['Invoice lines of Acme', 'ok'],
        ['Corporate customers', 'no-access'],
        ['Gone', 'unavailable'],
      ]);
      expect(tmCurrentPin(view.items, tmPinUrl('/17/centers?page=4'))?.id).toBe('a0000001');
    });
  });
});

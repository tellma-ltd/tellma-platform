// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { Location } from '@angular/common';
import type { Type } from '@angular/core';
import { TestBed } from '@angular/core/testing';

import { TM_DEV_MODE } from '../app/tm-dev-mode';
import { tmFeature } from '../declarations/tm-feature';
import type { TmDecimal } from '../json/tm-json';
import {
  tmAppRig,
  tmLoadTestPage,
  tmNavigate,
  tmRouteTestFeature,
  tmSettle,
  tmSettleUntil,
  TmTestPage,
  type TmAppRig,
} from '../routes/tm-app.testing.util';
import { TM_ROUTE_HOSTS } from '../routes/tm-route-hosts';
import { TM_ROUTE_CASES } from '../routes/tm-route-cases.testing.util';
import { tmParseListPayload } from './tm-search-query';

describe('TmUrls', () => {
  let rig: TmAppRig;

  beforeEach(async () => {
    rig = await tmAppRig();
    await tmNavigate('/17/centers');
  });

  afterEach(() => vi.restoreAllMocks());

  describe('the builders', () => {
    it('use the routed tenant', () => {
      expect(rig.urls.tenantId()).toBe(17);
      expect(rig.urls.home()).toBe('/17');
      expect(rig.urls.search('gl.Center')).toBe('/17/centers');
      expect(rig.urls.details('gl.Center', 5)).toBe('/17/centers/5');
      expect(rig.urls.details('gl.Center', 5, { collection: 'Lines', id: 42 })).toBe('/17/centers/5?child=Lines:42');
      expect(rig.urls.create('gl.Center')).toBe('/17/centers/new');
      expect(rig.urls.create('gl.Center', { clone: 5 })).toBe('/17/centers/new?clone=5');
      expect(rig.urls.page('me')).toBe('/17/me');
      expect(rig.urls.page('settings/general')).toBe('/17/settings/general');
      expect(rig.urls.blob('customer-image', 88, { variant: 'thumb' })).toBe('/17/blobs/customer-image/88?variant=thumb');
      expect(rig.urls.blob('customer-image', 88, { download: true })).toBe('/17/blobs/customer-image/88?download=true');
    });

    it('follow the routed tenant into another tenant', async () => {
      await rig.router.navigateByUrl('/18/centers');
      expect(rig.urls.tenantId()).toBe(18);
      expect(rig.urls.home()).toBe('/18');
    });

    it('build into any tenant with forTenant', () => {
      const other = rig.urls.forTenant(18);
      expect(other.home()).toBe('/18');
      expect(other.search('gl.Center', { q: 'jed' })).toBe('/18/centers?q=jed');
      expect(other.details('acme.Customer', '9007199254740993' as TmDecimal)).toBe('/18/customers/9007199254740993');
    });

    it('leave out the search page defaults the declaration sets', () => {
      expect(rig.urls.search('gl.Center', { tab: 'all', cols: ['Code', 'Name'], size: 50, page: 1, q: 'jed' })).toBe('/17/centers?q=jed');
      expect(rig.urls.search('gl.Center', { tab: 'mine', cols: ['Name'], size: 100 })).toBe('/17/centers?tab=mine&cols=Name&size=100');
      expect(rig.urls.search('gl.Center', { filter: 'Code > 1' })).toBe('/17/centers#d=eyJmaWx0ZXIiOiJDb2RlID4gMSJ9');
    });

    it('throw in development outside a tenant', async () => {
      await rig.router.navigateByUrl('/tenants');
      expect(rig.urls.tenantId()).toBeNull();
      expect(() => rig.urls.home()).toThrow(/outside a tenant route/);
      expect(rig.urls.forTenant(17).home()).toBe('/17');
    });

    it('throw in development for an entity with no route', () => {
      expect(() => rig.urls.search('acme.Nothing')).toThrow(/acme.Nothing/);
      expect(() => rig.urls.details('core.Blob', 1)).toThrow(/core.Blob/);
    });

    describe('in production', () => {
      beforeEach(async () => {
        TestBed.resetTestingModule();
        rig = await tmAppRig({ providers: [{ provide: TM_DEV_MODE, useValue: () => false }] });
        await tmNavigate('/17/centers');
      });

      it('answer the tenant home for an entity with no route', () => {
        expect(rig.urls.search('acme.Nothing')).toBe('/17');
        expect(rig.urls.details('core.Blob', 1)).toBe('/17');
        expect(rig.urls.create('acme.Nothing')).toBe('/17');
        expect(rig.telemetry.recent().filter((item) => item.name === 'tm.urls.no-tenant')).toEqual([]);
      });

      it('answer the picker outside a tenant, with a telemetry event', async () => {
        await rig.router.navigateByUrl('/tenants');
        expect(rig.urls.home()).toBe('/tenants');
        expect(rig.urls.search('gl.Center')).toBe('/tenants');
        expect(rig.telemetry.recent().filter((item) => item.name === 'tm.urls.no-tenant')).toHaveLength(2);
      });
    });

    it('build the tenantless pages', () => {
      expect(rig.urls.picker()).toBe('/tenants');
      expect(rig.urls.picker({ notice: 'tenant-not-found', from: 17 })).toBe('/tenants?notice=tenant-not-found&from=17');
      expect(rig.urls.signedOut()).toBe('/signed-out');
    });
  });

  describe('target', () => {
    it('opens the details page of a stack that has one', () => {
      expect(rig.urls.target('gl.Center', 5)).toBe('/17/centers/5');
      expect(rig.urls.target('GL.center', 5)).toBe('/17/centers/5');
    });

    it('is null for a stack whose declaration has no details page, a child or a read-only root', () => {
      expect(rig.urls.target('acme.Currency', 5)).toBeNull();
      expect(rig.urls.target('acme.SalesInvoiceLine', 5)).toBeNull();
      expect(rig.urls.target('core.Blob', 5)).toBeNull();
      expect(rig.urls.target('acme.Nothing', 5)).toBeNull();
    });
  });

  describe('the drill-through list', () => {
    it('carries its payload in the fragment, never its filter or columns in the query', () => {
      const url = rig.urls.list('sales-invoice-lines', { filter: 'Amount > 0', cols: ['Code'] }, { q: 'x', filter: 'ignored', cols: ['Name'] });
      expect(url).toMatch(/^\/17\/list\/sales-invoice-lines\?q=x#d=[A-Za-z0-9_-]+$/);
      expect(rig.urls.parseList(new URLSearchParams('q=x'), url.slice(url.indexOf('#')))).toEqual({
        payload: { filter: 'Amount > 0', cols: ['Code'] },
        query: { q: 'x' },
      });
    });

    it('parses a malformed d to a null payload', () => {
      expect(rig.urls.parseList(new URLSearchParams(''), '#d=!!')).toEqual({ payload: null, query: {} });
    });

    it('keeps a payload with Arabic text and & through navigateByUrl, to parse from the raw hash', async () => {
      const payload = { filter: "Customer.Name = 'مؤسسة جدة & أبناؤه'" };
      const url = rig.urls.list('sales-invoice-lines', payload);
      expect(await rig.router.navigateByUrl(url)).toBe(true);
      const address = TestBed.inject(Location).path(true);
      expect(address).toBe(url);
      expect(tmParseListPayload(address.slice(address.indexOf('#')))).toEqual(payload);
    });
  });

  describe('describe', () => {
    it.each(TM_ROUTE_CASES.map((row) => [row.url, row.described] as const))('classifies %s as the router routes it', (url, described) => {
      if (described === null) {
        expect(rig.urls.describe(url)).toBeNull();
      } else {
        expect(rig.urls.describe(url)).toMatchObject(described);
      }
    });

    it.each([
      ['/', { kind: 'launch', template: '/', pinnable: false }],
      ['/17/centers?q=jed', { kind: 'search', tenantId: 17, entity: 'gl.Center', template: '/:t/centers', pinnable: true }],
      ['/17/centers/5', { kind: 'details', tenantId: 17, entity: 'gl.Center', id: 5, template: '/:t/centers/:id', pinnable: true }],
    ])('classifies %s exactly', (url, expected) => {
      expect(rig.urls.describe(url)).toEqual(expected);
    });

    it('classifies pages and lists', () => {
      expect(rig.urls.describe('/17/centers/5/map')).toMatchObject({ kind: 'page', template: '/:t/centers/:id/map', page: { path: 'centers/:id/map' } });
      expect(rig.urls.describe('/17/approvals')).toMatchObject({ kind: 'page', securable: ['acme.Approval', 'Read'] });
      expect(rig.urls.describe('/17/list/sales-invoice-lines')).toEqual({
        kind: 'list',
        tenantId: 17,
        entity: 'acme.SalesInvoiceLine',
        template: '/:t/list/sales-invoice-lines',
        pinnable: true,
      });
    });

    it('tells two lists of one segment apart by d', () => {
      const one = rig.urls.describe(rig.urls.list('sales-invoice-lines', { filter: 'Amount > 0' }))!;
      const two = rig.urls.describe(rig.urls.list('sales-invoice-lines', { filter: 'Amount < 0' }))!;
      expect(one.template).toBe(two.template);
      expect(one.hash).toMatch(/^#d=/);
      expect(one.hash).not.toBe(two.hash);
    });
  });

  describe('preload', () => {
    it('loads a route’s chunks once at idle and runs no guard', async () => {
      const idle: (() => void)[] = [];
      vi.spyOn(window, 'requestIdleCallback').mockImplementation((callback) => {
        idle.push(() => callback({ didTimeout: false, timeRemaining: () => 50 }));
        return idle.length;
      });
      // Guards run only inside a navigation, so no navigation event means no guard ran.
      const events: string[] = [];
      const subscription = rig.router.events.subscribe((event) => events.push(event.constructor.name));
      // The centers' search page loads its declared results component as it starts.
      await tmSettleUntil(() => document.querySelector('tm-search-host') !== null, 'the search page starts');
      tmLoadTestPage.mockClear();

      rig.urls.preload('/17/centers/5');
      expect(tmLoadTestPage).not.toHaveBeenCalled();
      idle.splice(0).forEach((run) => run());
      await tmSettle();
      expect(tmLoadTestPage).toHaveBeenCalledTimes(1);

      rig.urls.preload('/17/centers/6');
      rig.urls.preload('/17/centers/new');
      idle.splice(0).forEach((run) => run());
      await tmSettle();
      // The details page is loaded, and create is the same layout here.
      expect(tmLoadTestPage).toHaveBeenCalledTimes(1);
      expect(events).toEqual([]);
      subscription.unsubscribe();
    });

    // --- The tenant home: its host's chunk, and the app's home hatch when a feature declares one ---
    describe('of the tenant home', () => {
      /** The idle callbacks the spec runs. */
      let idle: (() => void)[];

      beforeEach(() => {
        idle = [];
        vi.spyOn(window, 'requestIdleCallback').mockImplementation((callback) => {
          idle.push(() => callback({ didTimeout: false, timeRemaining: () => 50 }));
          return idle.length;
        });
      });

      it("loads the home host's chunk and the app's home hatch, once each", async () => {
        const hatch = vi.fn((): Promise<Type<unknown>> => Promise.resolve(TmTestPage));
        TestBed.resetTestingModule();
        rig = await tmAppRig({ features: [tmRouteTestFeature(), tmFeature({ key: 'shop', home: hatch })] });
        await tmNavigate('/17/centers');
        const host = vi.spyOn(TM_ROUTE_HOSTS, 'home');

        rig.urls.preload('/17');
        rig.urls.preload('/17');
        expect(hatch).not.toHaveBeenCalled();
        idle.splice(0).forEach((run) => run());
        await tmSettle();
        expect(hatch).toHaveBeenCalledTimes(1);
        expect(host).toHaveBeenCalledTimes(1);
      });

      it("loads only the home host's chunk when no feature declares a hatch", async () => {
        // The centers' search page loads its declared results component as it starts.
        await tmSettleUntil(() => document.querySelector('tm-search-host') !== null, 'the search page starts');
        await tmSettle();
        const host = vi.spyOn(TM_ROUTE_HOSTS, 'home');
        tmLoadTestPage.mockClear();

        rig.urls.preload('/17');
        expect(() => idle.splice(0).forEach((run) => run())).not.toThrow();
        await tmSettle();
        expect(host).toHaveBeenCalledTimes(1);
        expect(tmLoadTestPage).not.toHaveBeenCalled();
      });
    });
  });
});

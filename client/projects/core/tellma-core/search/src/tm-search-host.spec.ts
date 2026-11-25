// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { Location } from '@angular/common';
import type { SpyLocation } from '@angular/common/testing';
import { signal, type Provider } from '@angular/core';
import { TestKey } from '@angular/cdk/testing';
import { TestbedHarnessEnvironment } from '@angular/cdk/testing/testbed';
import { TestBed } from '@angular/core/testing';
import { Router } from '@angular/router';
import {
  TmApiError,
  TmLocalization,
  TmUrls,
  type TmEntityDeclarationInput,
  type TmJson,
  type TmJsonObject,
  type TmQueryRowSet,
} from '@tellma/core';
import { TmFakeApi, TmPageContextHarness, TmSearchPageHarness, type TmFakeApiCall } from '@tellma/core-testing';
import { TM_DEVICE_TIME_ZONE } from '@tellma/core-ui';
import { TM_QUERYEX_STRINGS_AR } from '@tellma/locale-ar/queryex-strings';

import { tmEntityRig, type TmEntityRig, type TmEntityRigOptions } from '../../src/entity/tm-entity.testing.util';
import { tmSettle, tmSettleUntil } from '../../src/routes/tm-app.testing.util';
import { ɵTM_SEARCH_STRINGS_EN } from './tm-search-strings';
import { tmSearchTestHash } from './tm-search.testing.util';

// The search host over the entity rig's customers, routed at
// /17/customers and read through the fake API: the first load's skeleton
// and the rows that follow, the links, the headers, the order, the paging
// and its history, every state of the results region with the Columns
// button above it, the offline rules, the re-read rule on a language
// switch, Create, Refresh, the page's context and its page actions, and
// the strings' Arabic.

/** One customer as the fake server holds it, by select term. */
type TmCustomerRow = Readonly<Record<string, TmJson>>;

/**
 * The customers the fake server holds: `count` of them, coded in order.
 *
 * @param count - How many.
 * @returns The rows.
 */
function customers(count: number): readonly TmCustomerRow[] {
  return Array.from({ length: count }, (_, index) => {
    const id = index + 1;
    return {
      Id: id,
      Code: `C-${String(id).padStart(3, '0')}`,
      Name: `Customer ${id}`,
      Name2: `عميل ${id}`,
      'Currency.Id': 'SAR',
      'Currency.Code': 'SAR',
      CreatedAt: '2026-01-15T12:00:00.0000000+00:00',
      CreditLimit: '1000.00',
      IsActive: true,
    };
  });
}

/**
 * The answer of a query over rows: the page the body's skip and take name,
 * each row's terms in the select's order, and the count.
 *
 * @param all - The rows.
 * @param extra - Members beside the rows and the count, such as a capped count.
 * @returns The responder.
 */
function answer(all: readonly TmCustomerRow[], extra: TmJsonObject = {}): (call: TmFakeApiCall) => TmJsonObject {
  return (call) => {
    const body = call.body as TmJsonObject;
    const terms = String(body['select']).split(',');
    const skip = Number(body['skip'] ?? 0);
    const take = Number(body['take'] ?? all.length);
    const rows: TmQueryRowSet = {
      columns: terms.map((term) => ({ name: term, type: 'String', nullable: true, groupingKey: false, path: term.split('.') })),
      rows: all.slice(skip, skip + take).map((row) => terms.map((term) => row[term] ?? null)),
    };
    return { rows, count: all.length, ...extra } as unknown as TmJsonObject;
  };
}

/** Customers' search members under test: three columns ordered by code. */
const CUSTOMER: Partial<TmEntityDeclarationInput<'acme.Customer'>> = { columns: ['Code', 'Name', 'Currency'], orderBy: 'Code' };

/** What a spec opens the page with. */
interface TmOpenOptions extends TmEntityRigOptions {
  /** The rows the server holds; three when omitted. */
  readonly rows?: readonly TmCustomerRow[];
  /** The server's answer, in place of the rows'. */
  readonly respond?: (call: TmFakeApiCall) => unknown;
}

/** What a spec drives. */
interface TmOpened {
  /** The rig. */
  readonly rig: TmEntityRig;
  /** The fake API. */
  readonly api: TmFakeApi;
  /** The page's harness. */
  readonly page: TmSearchPageHarness;
  /** The router's mock location, whose changes tell a push from a replace. */
  readonly location: SpyLocation;
}

/**
 * The rendered search host.
 *
 * @returns Its element, or `null`.
 */
function host(): HTMLElement | null {
  return document.querySelector('tm-search-host');
}

/**
 * Whether the page is settled: rendered, and no read runs.
 *
 * @returns Whether it is.
 */
function settled(): boolean {
  const region = host()?.querySelector('.tm-search-host__region');
  return region !== null && region !== undefined && region.getAttribute('aria-busy') !== 'true';
}

/**
 * The texts of the rendered rows' data cells, row by row.
 *
 * @returns The texts.
 */
function rowTexts(): string[][] {
  const rows = [...(host()?.querySelectorAll('[role="rowgroup"] [role="row"]') ?? [])];
  return rows.map((row) => [...row.querySelectorAll('[role="gridcell"][data-tm-cell]')].map((cell) => (cell.textContent ?? '').trim()));
}

/**
 * The query reads the fake API received.
 *
 * @param api - The fake API.
 * @returns Their bodies, oldest first.
 */
function reads(api: TmFakeApi): TmJsonObject[] {
  return api
    .calls()
    .filter((call) => call.route === 'customers/query')
    .map((call) => call.body as TmJsonObject);
}

/**
 * Opens the customers' search page at a URL and waits for its first read to settle.
 *
 * @param url - The URL, or a function that prints it once the rig's providers are in place.
 * @param options - The rig's options, the rows and the answer.
 * @returns What the spec drives.
 */
async function open(url: string | (() => string) = '/17/customers', options: TmOpenOptions = {}): Promise<TmOpened> {
  const rig = await tmEntityRig({ access: { isSystem: true }, customer: CUSTOMER, ...options });
  rig.api.respond('customers/query', options.respond ?? answer(options.rows ?? customers(3)));
  await rig.router.navigateByUrl(typeof url === 'string' ? url : url());
  await tmSettleUntil(settled, 'the search page settles');
  const page = await TestbedHarnessEnvironment.loader(rig.router.fixture).getHarness(TmSearchPageHarness);
  return { rig, api: rig.api, page, location: TestBed.inject(Location) as SpyLocation };
}

/**
 * Waits until the results region's error view shows: the error views load with the first error they present.
 */
async function errorViews(): Promise<void> {
  await tmSettleUntil(() => host()?.querySelector('tm-search-state tm-error-state') != null, 'the error views load');
}

/**
 * Waits until the page settles again after a change.
 *
 * @param description - What the spec waits for.
 */
async function resettle(description = 'the search page settles again'): Promise<void> {
  await tmSettleUntil(settled, description);
}

/**
 * The header a grid column of a key shows.
 *
 * @param key - The column's key.
 * @returns The header element.
 */
function header(key: string): HTMLElement {
  return host()!.querySelector<HTMLElement>(`[role="columnheader"][data-key="${key}"]`)!;
}

/**
 * The address the mock location holds.
 *
 * @returns The path with its query.
 */
function address(): string {
  return TestBed.inject(Location).path(true);
}

/**
 * The page-context harness, which reads the routed page's context and invokes its actions.
 *
 * @returns The harness.
 */
function contexts(): TmPageContextHarness {
  return TestBed.inject(TmPageContextHarness);
}

/**
 * A `400 query-invalid` with one diagnostic on a clause.
 *
 * @param clause - The clause, such as `filters[0]`.
 * @param start - Where the problem starts in the clause's text.
 * @param length - The problem's length.
 * @returns The error.
 */
function invalid(clause: string, start: number, length: number): TmApiError {
  return new TmApiError({
    kind: 'query-invalid',
    status: 400,
    code: 'query-invalid',
    problem: {
      code: 'query-invalid',
      instance: 'trace-1',
      status: 400,
      errors: [{ code: 'Queryex.UnknownProperty', message: 'Unknown property Bogus.', path: clause }],
      errorDetails: { diagnostics: [{ clause, code: 'Queryex.UnknownProperty', start, length, location: null, arguments: { name: 'Bogus' } }] },
    },
  });
}

describe('TmSearchHost', () => {
  describe('the first load', () => {
    it('draws skeleton rows under the headers, then the rows the query reads', async () => {
      // The first read waits, so the skeleton shows before the rows land.
      let release!: () => void;
      const held = new Promise<void>((resolve) => (release = resolve));
      const rig = await tmEntityRig({ access: { isSystem: true }, customer: CUSTOMER });
      const respond = answer(customers(3));
      rig.api.respond('customers/query', async (call: TmFakeApiCall) => {
        await held;
        return respond(call);
      });
      await rig.router.navigateByUrl('/17/customers');
      await tmSettleUntil(() => host()?.querySelector('[data-tm-skeleton]') != null, 'the skeleton shows');
      expect(host()!.querySelector('.tm-search-host__region')!.getAttribute('aria-busy')).toBe('true');
      expect(host()!.querySelector('[data-tm-columns]')).not.toBeNull();

      release();
      await resettle();
      const page = await TestbedHarnessEnvironment.loader(rig.router.fixture).getHarness(TmSearchPageHarness);
      expect(await page.isFirstLoad()).toBe(false);
      expect(await page.getTitle()).toBe('Customers');
      expect(await (await page.getGrid())!.getHeaderTexts()).toEqual(['Code', 'Name', 'Currency']);
      expect(rowTexts()).toEqual([
        ['C-001', 'Customer 1', 'SAR'],
        ['C-002', 'Customer 2', 'SAR'],
        ['C-003', 'Customer 3', 'SAR'],
      ]);
      expect(reads(rig.api)).toEqual([
        expect.objectContaining({
          select: 'Id,Code,Name,Currency.Id,Currency.Code,ImageId,Image.Variants,IsActive',
          orderBy: 'Code',
          skip: 0,
          take: 50,
          includeCount: true,
        }),
      ]);
    });

    it('links the first shown column of the display name to the row’s details page, and opens a row there with its way back', async () => {
      const { page, location } = await open();
      const links = host()!.querySelectorAll<HTMLAnchorElement>('a.tm-search-host__link');
      // The code prefixes the customers' display name, so its column holds the link.
      expect([...links].map((link) => [link.getAttribute('href'), link.textContent?.trim()])[0]).toEqual(['/17/customers/1', 'C-001']);
      expect(host()!.querySelectorAll('[role="gridcell"] a.tm-search-host__link')).toHaveLength(3);

      await (await page.getGrid())!.activateRow(1);
      await tmSettleUntil(() => address() === '/17/customers/2', 'the second row opens');
      expect(location.getState()).toEqual(expect.objectContaining({ tmFrom: '/17/customers', tmPage: 1 }));
    });

    it('preloads the target of a row under the pointer and of the highlighted row, and the create page', async () => {
      const preload = vi.spyOn(TmUrls.prototype, 'preload');
      onTestFinished(() => preload.mockRestore());
      const { page } = await open();
      expect(preload).toHaveBeenCalledWith('/17/customers/new');
      host()!
        .querySelectorAll<HTMLElement>('[role="rowgroup"] [role="row"]')[2]!
        .querySelector('[role="gridcell"]')!
        .dispatchEvent(new PointerEvent('pointerover', { bubbles: true }));
      expect(preload).toHaveBeenCalledWith('/17/customers/3');
      preload.mockClear();
      await (await page.getGrid())!.pressKeys(TestKey.DOWN_ARROW);
      await tmSettle();
      expect(preload.mock.calls.some(([url]) => /^\/17\/customers\/[12]$/.test(url))).toBe(true);
    });

    it('remembers the display names the rows carry, and reports the read’s timing', async () => {
      const { rig } = await open();
      expect(rig.names.label('acme.Customer', 2)()).toContain('Customer 2');
      const timing = rig.telemetry.recent().find((item) => item.name === 'tm.search.query');
      expect(timing?.props).toEqual(expect.objectContaining({ source: 'load', view: 'grid', tree: 'none', outcome: 'ok' }));
    });

    it('replaces the address with the canonical URL', async () => {
      const { location } = await open('/17/customers?page=1');
      expect(address()).toBe('/17/customers');
      expect(location.urlChanges.at(-1)).toBe('replace: /17/customers');
    });
  });

  describe('headers', () => {
    it("label an instant column with the zone while the tenant's differs from the device's", async () => {
      const zone = (device: string): Provider => ({ provide: TM_DEVICE_TIME_ZONE, useValue: signal(device) });
      await open('/17/customers', { customer: { columns: ['Code', 'CreatedAt'] }, providers: [zone('UTC')] });
      expect(header('CreatedAt').textContent).toContain('(GMT+3)');
      expect(header('Code').textContent).not.toContain('GMT');
    });

    it('show no zone while the zones agree', async () => {
      const zone: Provider = { provide: TM_DEVICE_TIME_ZONE, useValue: signal('Asia/Riyadh') };
      await open('/17/customers', { customer: { columns: ['Code', 'CreatedAt'] }, providers: [zone] });
      expect(header('CreatedAt').textContent).not.toContain('GMT');
    });
  });

  describe('order', () => {
    it('cycles a header through ascending, descending and the default order, replacing the address', async () => {
      const { api, page, location } = await open();
      const grid = (await page.getGrid())!;
      const name = Number(header('Name').getAttribute('data-col'));
      await grid.clickColumnHeader(name);
      await resettle();
      expect(await grid.getSort()).toEqual({ key: 'Name', direction: 'asc' });
      expect(location.urlChanges.at(-1)).toBe('replace: /17/customers?order=Name');

      await grid.clickColumnHeader(name);
      await resettle();
      expect(await grid.getSort()).toEqual({ key: 'Name', direction: 'desc' });
      expect(location.urlChanges.at(-1)).toBe('replace: /17/customers?order=Name:desc');

      await grid.clickColumnHeader(name);
      await resettle();
      expect(address()).toBe('/17/customers');
      expect(reads(api).map((body) => body['orderBy'])).toEqual(['Code', 'Name', 'Name desc', 'Code']);
    });
  });

  describe('paging', () => {
    it('pushes a history entry per page, and reads that page', async () => {
      const { api, page, location } = await open('/17/customers', { rows: customers(120) });
      const pager = await page.getPager();
      expect(await pager.getStatusText()).toContain('120');
      await pager.next();
      await resettle();
      expect(location.urlChanges.at(-1)).toBe('/17/customers?page=2');
      expect(reads(api).at(-1)).toEqual(expect.objectContaining({ skip: 50, take: 50 }));
      expect(rowTexts()[0]?.[0]).toBe('C-051');
    });

    it('follows Back to the page before', async () => {
      const { page, location } = await open('/17/customers', { rows: customers(120) });
      await (await page.getPager()).next();
      await resettle();
      expect(rowTexts()[0]?.[0]).toBe('C-051');
      // The app's bootstrap listens for Back and Forward; the test bed's router starts without it.
      TestBed.inject(Router).setUpLocationChangeListener();
      location.back();
      await tmSettleUntil(() => rowTexts()[0]?.[0] === 'C-001', 'the first page shows again');
      expect(address()).toBe('/17/customers');
    });

    it('shows a capped count as a lower bound', async () => {
      const { page } = await open('/17/customers', { rows: customers(60), respond: answer(customers(60), { count: 10_000, countCapped: true }) });
      expect(await (await page.getPager()).getStatusText()).toContain('+');
    });

    it('stops at the last page the server’s window reaches', async () => {
      const { page } = await open('/17/customers?page=2000', { respond: answer(customers(100_050), {}) });
      expect(await (await page.getPager()).isNextDisabled()).toBe(true);
    });

    it('loads the first page instead when the server refuses one past its window', async () => {
      let first = true;
      const respond = answer(customers(3));
      const { api, location } = await open('/17/customers?page=3', {
        respond: (call) => {
          if (first) {
            first = false;
            throw new TmApiError({ kind: 'limit', status: 413, code: 'limit' });
          }
          return respond(call);
        },
      });
      expect(address()).toBe('/17/customers');
      expect(location.urlChanges.at(-1)).toBe('replace: /17/customers');
      expect(reads(api).map((body) => body['skip'])).toEqual([100, 0]);
    });
  });

  describe('states', () => {
    it('offer the first record when the entity has none', async () => {
      const { page } = await open('/17/customers', { rows: [] });
      expect(await page.getStateHeading()).toBe('Nothing here yet');
      expect(await page.getStateActions()).toEqual(['New customer']);
      expect(await page.hasColumnsButton()).toBe(true);
    });

    it('offer to clear the search when nothing matches it', async () => {
      const { page } = await open('/17/customers?q=zzz', { rows: [] });
      expect(await page.getStateHeading()).toBe('No matches');
      expect(await page.getStateActions()).toEqual(['Clear search']);
      expect(await page.hasColumnsButton()).toBe(true);
      await page.clickStateAction('Clear search');
      await resettle();
      expect(address()).toBe('/17/customers');
    });

    it('offer to clear the filters when nothing matches them', async () => {
      const { page } = await open(() => '/17/customers' + tmSearchTestHash('CreditLimit > 0'), { rows: [] });
      expect(await page.getStateHeading()).toBe('No matches');
      expect(await page.getStateActions()).toEqual(['Clear filters']);
    });

    it("show an invalid filter's first diagnostic and the code it points at", async () => {
      const { page } = await open(() => '/17/customers' + tmSearchTestHash('Bogus > 0'), {
        respond: () => {
          throw invalid('filters[0]', 0, 5);
        },
      });
      expect(await page.getStateHeading()).toBe('This filter expression is not valid');
      expect(await page.getStateToken()).toBe('Bogus');
      expect(await page.hasColumnsButton()).toBe(true);
    });

    it('show a calculation that fails for some rows, and why', async () => {
      const { page } = await open('/17/customers', {
        respond: () => {
          throw new TmApiError({
            kind: 'validation',
            status: 422,
            code: 'Query.ArithmeticFailed',
            problem: {
              code: 'Query.ArithmeticFailed',
              instance: 'trace-1',
              status: 422,
              errors: [{ code: 'Query.ArithmeticFailed', arguments: { reason: 'divide-by-zero' } }],
            },
          });
        },
      });
      expect(await page.getStateHeading()).toBe("A column or filter can't be calculated for some rows");
      expect(await (await page.getState())!.getDetail()).toContain('It divides by zero.');
      expect(await page.hasColumnsButton()).toBe(true);
    });

    it("show any other failure's error view, whose Try again reads again and keeps the view until it lands", async () => {
      let fail = true;
      let release!: () => void;
      const held = new Promise<void>((resolve) => (release = resolve));
      const respond = answer(customers(2));
      const { api, page } = await open('/17/customers', {
        respond: async (call) => {
          if (fail) {
            throw new TmApiError({ kind: 'server', status: 500, code: 'server', traceId: 'trace-9' });
          }
          await held;
          return respond(call);
        },
      });
      await errorViews();
      expect(await page.getStateHeading()).not.toBeNull();
      expect(await page.hasColumnsButton()).toBe(true);
      const retry = (await page.getStateActions()).find((label) => /try again/i.test(label))!;
      fail = false;
      // Clicked on the element itself: a harness would wait for the read it starts.
      host()!
        .querySelectorAll<HTMLElement>('tm-search-state button')
        .forEach((button) => button.textContent?.trim() === retry && button.click());
      await tmSettleUntil(() => !settled(), 'the retry reads');
      expect(host()!.querySelector('tm-search-state')).not.toBeNull();
      release();
      await resettle();
      expect(rowTexts()).toHaveLength(2);
      expect(reads(api)).toHaveLength(2);
    });

    it("show that a drill-through list's link has no valid filter, and read nothing", async () => {
      const { api, page } = await open('/17/list/customers');
      expect(await page.getStateHeading()).toBe("This link's filter is not valid");
      expect(reads(api)).toEqual([]);
    });
  });

  describe('offline', () => {
    it('empties the region for a changed query read offline', async () => {
      const { api, page } = await open();
      api.respond('customers/query', () => {
        throw new TmApiError({ kind: 'offline' });
      });
      await contexts().invoke('search.query', { q: 'acme' });
      await resettle();
      expect(rowTexts()).toEqual([]);
      await errorViews();
      expect(await page.getStateHeading()).not.toBeNull();
    });

    it('keeps the rows through an offline Refresh, and reads again once the API is back', async () => {
      const { api, page } = await open();
      api.respond('customers/query', () => {
        throw new TmApiError({ kind: 'offline' });
      });
      api.setOnline(false);
      await page.refresh();
      await resettle();
      expect(rowTexts()).toHaveLength(3);
      expect(await page.getStateHeading()).toBeNull();

      api.respond('customers/query', answer(customers(4)));
      api.setOnline(true);
      await tmSettleUntil(() => rowTexts().length === 4, 'the reconnect reads again');
      expect(reads(api)).toHaveLength(3);
    });
  });

  describe('reads', () => {
    it('abort the read a newer query supersedes', async () => {
      const { api } = await open();
      api.respond('customers/query', () => new Promise<never>(() => undefined));
      void contexts().invoke('search.query', { q: 'a' });
      await tmSettle();
      void contexts().invoke('search.query', { q: 'ab' });
      await tmSettle();
      const [, first, second] = api.calls().filter((call) => call.route === 'customers/query');
      expect((first!.options as { readonly signal: AbortSignal }).signal.aborted).toBe(true);
      expect((second!.options as { readonly signal: AbortSignal }).signal.aborted).toBe(false);
    });

    it('read again on Refresh, as a refresh', async () => {
      const { api, page, rig } = await open();
      await page.refresh();
      await resettle();
      expect(reads(api)).toHaveLength(2);
      expect(reads(api)[1]).toEqual(reads(api)[0]);
      const sources = rig.telemetry
        .recent()
        .filter((item) => item.name === 'tm.search.query')
        .map((item) => item.props['source']);
      expect(sources).toEqual(['load', 'refresh']);
    });

    it('read once on a switch to Arabic under a text order on a tenant of English and Arabic', async () => {
      const { api } = await open('/17/customers?order=Name', { languages: 2 });
      TestBed.inject(TmLocalization).setLanguage('ar');
      await tmSettleUntil(() => reads(api).length === 2 && settled(), 'the switch reads again');
      await tmSettle();
      expect(reads(api)).toHaveLength(2);
      expect(reads(api)[1]!['orderBy']).not.toBe(reads(api)[0]!['orderBy']);
    });

    it('read nothing on a switch under the order of a navigation whose display is a scalar', async () => {
      const { api } = await open('/17/customers?order=Currency', { languages: 2 });
      TestBed.inject(TmLocalization).setLanguage('ar');
      await tmSettle();
      await resettle();
      expect(reads(api)).toHaveLength(1);
    });

    it('read once when a permissions move re-grants a column, and nothing when a move changes no request', async () => {
      const reader = { isSystem: false, securables: [{ resource: 'acme.Customer', action: 'Read', hasFilter: false }] };
      // A currency's creation time is outside what a customer's read projects of its currency.
      const { api, rig } = await open('/17/customers', { access: reader, customer: { ...CUSTOMER, columns: ['Code', 'Name', 'Currency.CreatedAt'] } });
      expect(reads(api)[0]!['select']).not.toContain('Currency.CreatedAt');
      // The default as the user may read it leaves the columns out of the address.
      expect(address()).toBe('/17/customers');

      // The same access under a new tag rebuilds the same request.
      await rig.move({ ...rig.docs, me: { ...rig.docs.me, access: { ...rig.docs.me.access, tag: 'A2' } } });
      await resettle();
      expect(reads(api)).toHaveLength(1);

      // The currencies' read reaches the column again, whose term the select gains.
      const granted = [...reader.securables, { resource: 'acme.Currency', action: 'Read', hasFilter: false }];
      await rig.move({ ...rig.docs, me: { ...rig.docs.me, access: { ...rig.docs.me.access, tag: 'A3', securables: granted } } });
      await tmSettleUntil(() => reads(api).length === 2 && settled(), 'the re-grant reads again');
      expect(reads(api)[1]!['select']).toContain('Currency.CreatedAt');
    });

    it('read nothing on a switch on a tenant of one language', async () => {
      // The user's Arabic is not the tenant's, so both languages read its one slot.
      const { api } = await open('/17/customers?order=Name', { language: 'ar' });
      TestBed.inject(TmLocalization).setLanguage('en');
      await tmSettle();
      await resettle();
      expect(reads(api)).toHaveLength(1);
    });
  });

  describe('Create', () => {
    it('links to the create page while the user may create', async () => {
      const { page } = await open();
      expect(await page.getCreate()).toEqual({ href: '/17/customers/new', label: 'New customer', disabledReason: null });
    });

    it('says why it is disabled while the user may not', async () => {
      const { page } = await open('/17/customers', { access: { isSystem: false, securables: [{ resource: 'acme.Customer', action: 'Read', hasFilter: false }] } });
      const create = await page.getCreate();
      expect(create?.href).toBeNull();
      expect(create?.disabledReason).toBeTruthy();
    });
  });

  describe('the page context', () => {
    it('publishes the query, the selection and the visible rows', async () => {
      await open('/17/customers?q=cust');
      const current = contexts().current()!;
      expect(current).toEqual(
        expect.objectContaining({ kind: 'search', entity: 'acme.Customer', routeTemplate: '/:t/customers', title: 'Customers', status: 'ready' }),
      );
      expect(current.state).toEqual({ query: { q: 'cust' }, view: 'grid', list: false, selectedIds: [], selectedCount: 0 });
      expect(current.visible?.columns).toEqual(['Code', 'Name', 'Currency']);
      expect(current.visible?.rowCount).toBe(3);
      expect(current.visible?.total).toBe(3);
    });

    it("registers the host's own page actions", async () => {
      const { api } = await open();
      expect(
        contexts()
          .actions()
          .map((action) => action.id)
          .sort(),
      ).toEqual(['search.create', 'search.open', 'search.query', 'search.refresh']);
      await contexts().invoke('search.query', { q: 'acme', page: 1 });
      await resettle();
      expect(address()).toBe('/17/customers?q=acme');
      expect(reads(api).at(-1)).toEqual(expect.objectContaining({ search: 'acme' }));

      expect(await contexts().invoke('search.open', { id: 2 })).toEqual({ outcome: 'done', data: { opened: true } });
      await tmSettleUntil(() => address() === '/17/customers/2', 'the row opens');
    });
  });
});

describe('the search strings', () => {
  /**
   * The key paths of a string tree.
   *
   * @param tree - The tree.
   * @param prefix - The path so far.
   * @returns The paths, sorted.
   */
  function keyPaths(tree: Readonly<Record<string, unknown>>, prefix = ''): string[] {
    return Object.entries(tree)
      .flatMap(([key, value]) => (typeof value === 'string' ? [`${prefix}${key}`] : keyPaths(value as Record<string, unknown>, `${prefix}${key}.`)))
      .sort();
  }

  it('have an Arabic for every English key, and no other', () => {
    expect(keyPaths(TM_QUERYEX_STRINGS_AR['search'] as Record<string, unknown>)).toEqual(keyPaths(ɵTM_SEARCH_STRINGS_EN));
  });

  it("take the English messages' arguments in Arabic", () => {
    const argumentsOf = (message: string): string[] => [...new Set([...message.matchAll(/\{(\w+)/g)].map((match) => match[1]!))].sort();
    const arabic = TM_QUERYEX_STRINGS_AR['search'] as Record<string, string>;
    for (const [key, english] of Object.entries(ɵTM_SEARCH_STRINGS_EN)) {
      expect([key, argumentsOf(arabic[key]!)]).toEqual([key, argumentsOf(english)]);
    }
  });
});

// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { LiveAnnouncer } from '@angular/cdk/a11y';
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
import { TmMenuHarness } from '@tellma/core-ui-testing';
import { TM_QUERYEX_STRINGS_AR } from '@tellma/locale-ar/queryex-strings';

import { tmEntityRig, type TmEntityRig, type TmEntityRigOptions } from '../../src/entity/tm-entity.testing.util';
import { tmSettle, tmSettleUntil, tmWaitUntil } from '../../src/routes/tm-app.testing.util';
import { ɵTM_SEARCH_ERROR_VIEWS } from './tm-search-state';
import { ɵTM_SEARCH_STRINGS_EN } from './tm-search-strings';
import { tmSearchTestHash } from './tm-search.testing.util';

// The search host over the entity rig's customers, routed at
// /17/customers and read through the fake API: the first load's skeleton
// and the rows that follow, the links, the headers, the order, the paging
// and its history, every state of the results region with the Columns
// button above it, the offline rules, the re-read rule on a language
// switch, the search field (its burst timing, the pending Enter, type-to-
// search, the undo anchor and the paused announcements), the row menu,
// Create, Refresh, the page's context and its page actions, and
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

/**
 * The answer of a query that searches the rows' names: the rows whose name
 * holds the body's search text, ignoring case, as {@link answer} pages them.
 *
 * @param all - The rows.
 * @returns The responder.
 */
function searched(all: readonly TmCustomerRow[]): (call: TmFakeApiCall) => TmJsonObject {
  return (call) => {
    const text = String((call.body as TmJsonObject)['search'] ?? '').toLowerCase();
    return answer(all.filter((row) => String(row['Name']).toLowerCase().includes(text)))(call);
  };
}

/**
 * A responder that answers after a delay on the page's clock.
 *
 * @param ms - The delay.
 * @param respond - The answer.
 * @returns The responder.
 */
function later(ms: number, respond: (call: TmFakeApiCall) => TmJsonObject): (call: TmFakeApiCall) => Promise<TmJsonObject> {
  return (call) => new Promise((resolve) => setTimeout(() => resolve(respond(call)), ms));
}

/** Fakes the page's timers and clock, so a spec counts the milliseconds between keys. */
function fakeClock(): void {
  vi.useFakeTimers({ toFake: ['setTimeout', 'clearTimeout', 'setInterval', 'clearInterval', 'performance'] });
}

/**
 * Moves the fake clock on and renders.
 *
 * @param ms - How far.
 */
async function step(ms: number): Promise<void> {
  await vi.advanceTimersByTimeAsync(ms);
  TestBed.tick();
}

/**
 * Moves the fake clock on in small steps until a condition holds.
 *
 * @param condition - What the spec waits for.
 * @param description - What it is, in the error.
 */
async function until(condition: () => boolean, description: string): Promise<void> {
  await tmWaitUntil(condition, () => step(10), description);
}

/**
 * The search field's input.
 *
 * @returns The input.
 */
function field(): HTMLInputElement {
  return host()!.querySelector<HTMLInputElement>('[data-tm-search]')!;
}

/**
 * Whether the results region's progress bar shows.
 *
 * @returns Whether it does.
 */
function barShown(): boolean {
  return !host()!.querySelector('tm-progress-bar')!.classList.contains('tm-progress-bar--hidden');
}

/**
 * The abort signal a fake API call was made with.
 *
 * @param call - The call.
 * @returns The signal.
 */
function signalOf(call: TmFakeApiCall): AbortSignal {
  return (call.options as { readonly signal: AbortSignal }).signal;
}

/**
 * Presses a key on an element as the browser dispatches it: a keydown that bubbles, then the keyup.
 *
 * @param target - The element with focus.
 * @param key - The key.
 * @returns Whether the keydown went unhandled (its default was not prevented).
 */
function press(target: Element, key: string): boolean {
  const unhandled = target.dispatchEvent(new KeyboardEvent('keydown', { key, bubbles: true, cancelable: true }));
  target.dispatchEvent(new KeyboardEvent('keyup', { key, bubbles: true }));
  return unhandled;
}

/**
 * Types a key into the focused search field as the browser does: the
 * keydown, the edit at the caret with its input event, then the keyup.
 *
 * @param key - The key, one character.
 */
function typeKey(key: string): void {
  const input = field();
  const unhandled = input.dispatchEvent(new KeyboardEvent('keydown', { key, bubbles: true, cancelable: true }));
  if (unhandled) {
    document.execCommand('insertText', false, key);
  }
  input.dispatchEvent(new KeyboardEvent('keyup', { key, bubbles: true }));
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

    it('renders Refresh and the pager, and the end slot that holds them, only once the first read answers', async () => {
      // The first read waits, so the trailing group is seen while the total is unknown.
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
      // The toolbar's end slot draws no box while it is empty, so its arrival moves nothing.
      const end = (): HTMLElement => host()!.querySelector<HTMLElement>('.tm-page-header__toolbar .tm-page-header__end')!;
      expect(host()!.querySelector('[data-tm-refresh]')).toBeNull();
      expect(host()!.querySelector('tm-pager')).toBeNull();
      expect(getComputedStyle(end()).display).toBe('none');

      release();
      await resettle();
      expect(host()!.querySelector('[data-tm-refresh]')).not.toBeNull();
      expect(host()!.querySelector('tm-pager')).not.toBeNull();
      expect(getComputedStyle(end()).display).toBe('flex');
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

    it('loads the last page with rows instead of one past the end of them', async () => {
      const { api, location, page } = await open('/17/customers?page=9', { respond: answer(customers(120)) });
      expect(address()).toBe('/17/customers?page=3');
      expect(location.urlChanges.at(-1)).toBe('replace: /17/customers?page=3');
      expect(reads(api).map((body) => body['skip'])).toEqual([400, 100]);
      expect(rowTexts()[0]?.[0]).toBe('C-101');
      expect(await page.getStateHeading()).toBeNull();
    });

    it('loads the first page for one past the end of a single page of rows', async () => {
      const { api, location } = await open('/17/customers?page=4', { respond: answer(customers(3)) });
      expect(address()).toBe('/17/customers');
      expect(location.urlChanges.at(-1)).toBe('replace: /17/customers');
      expect(reads(api).map((body) => body['skip'])).toEqual([150, 0]);
      expect(rowTexts().length).toBe(3);
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

    it('offer to clear the filters when nothing matches with inactive rows included', async () => {
      const { page } = await open('/17/customers?inactive=1', { rows: [] });
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

    it("show a line of their own with Try again while the error views can't load", async () => {
      let fail = true;
      const respond = answer(customers(2));
      const { api, page } = await open('/17/customers', {
        // The error views' chunk fails to load, as offline or after a deploy removed it.
        providers: [{ provide: ɵTM_SEARCH_ERROR_VIEWS, useValue: () => Promise.reject(new Error('chunk load failed')) }],
        respond: (call) => {
          if (fail) {
            throw new TmApiError({ kind: 'server', status: 500, code: 'server', traceId: 'trace-9' });
          }
          return respond(call);
        },
      });
      await tmSettleUntil(() => host()?.querySelector('.tm-search-state__failed') != null, 'the fallback line shows');
      const line = host()!.querySelector<HTMLElement>('.tm-search-state__failed')!;
      expect(line.textContent).toContain("The results couldn't be loaded.");
      expect(host()!.querySelector('tm-search-state tm-error-state')).toBeNull();
      expect(await page.hasColumnsButton()).toBe(true);
      fail = false;
      line.querySelector<HTMLElement>('button')!.click();
      await tmSettleUntil(() => rowTexts().length === 2, 'Try again reads the rows');
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
      expect(await page.hasColumnsButton()).toBe(true);
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
      expect(await page.hasColumnsButton()).toBe(true);

      api.respond('customers/query', answer(customers(4)));
      api.setOnline(true);
      await tmSettleUntil(() => rowTexts().length === 4, 'the reconnect reads again');
      expect(reads(api)).toHaveLength(3);
    });

    it('empties the region for a column added back while offline', async () => {
      const { api, page, rig } = await open('/17/customers?cols=Code,Name');
      expect(header('Currency')).toBeNull();
      api.respond('customers/query', () => {
        throw new TmApiError({ kind: 'offline' });
      });
      api.setOnline(false);
      // The Customers nav item opens the default columns, whose Currency the held rows lack.
      await rig.router.navigateByUrl('/17/customers');
      await resettle();
      expect(reads(api)).toHaveLength(2);
      expect(rowTexts()).toEqual([]);
      await errorViews();
      expect(await page.getStateHeading()).not.toBeNull();
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

    it('read nothing when a column is removed', async () => {
      const { api, rig } = await open();
      expect(header('Currency')).not.toBeNull();
      await rig.router.navigateByUrl('/17/customers?cols=Code,Name');
      await resettle();
      await tmSettle();
      expect(header('Currency')).toBeNull();
      expect(rowTexts()[0]).toHaveLength(2);
      expect(reads(api)).toHaveLength(1);
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

  describe('announcements', () => {
    it('announce the count of a settled result', async () => {
      const announce = vi.spyOn(LiveAnnouncer.prototype, 'announce').mockResolvedValue(undefined);
      onTestFinished(() => announce.mockRestore());
      await open();
      expect(announce).toHaveBeenCalledWith('3 results', 'polite');
    });

    it('announce a capped count as a lower bound', async () => {
      const announce = vi.spyOn(LiveAnnouncer.prototype, 'announce').mockResolvedValue(undefined);
      onTestFinished(() => announce.mockRestore());
      await open('/17/customers', { respond: answer(customers(60), { count: 10_000, countCapped: true }) });
      expect(announce).toHaveBeenCalledWith(expect.stringMatching(/^10,?000\+ results$/), 'polite');
    });

    it('announce that the rows on screen were kept through an offline read', async () => {
      const announce = vi.spyOn(LiveAnnouncer.prototype, 'announce').mockResolvedValue(undefined);
      onTestFinished(() => announce.mockRestore());
      const { api, page } = await open();
      api.respond('customers/query', () => {
        throw new TmApiError({ kind: 'offline' });
      });
      api.setOnline(false);
      announce.mockClear();
      await page.refresh();
      await resettle();
      expect(announce).toHaveBeenCalledWith("You're offline", 'polite');
      expect(announce.mock.calls.some(([message]) => /results/.test(String(message)))).toBe(false);
    });
  });

  describe('the search field', () => {
    // The field's keys run on the fake clock where a spec counts the
    // milliseconds between them; the page opens on the real one first.
    afterEach(() => vi.useRealTimers());

    it('names itself, ties itself to the title and the results, and caps its text', async () => {
      const { page } = await open('/17/customers?q=Customer');
      const input = field();
      expect(await page.getSearchText()).toBe('Customer');
      expect(await page.getSearchPlaceholder()).toBe('Search');
      expect(input.getAttribute('aria-label')).toBe('Search');
      expect(input.getAttribute('type')).toBe('search');
      expect(input.getAttribute('dir')).toBe('auto');
      expect(input.maxLength).toBe(200);
      // The description reads the page's title, then how to move through the results.
      const [title, hint] = input.getAttribute('aria-describedby')!.split(' ');
      expect(document.getElementById(title!)!.textContent!.trim()).toBe('Customers');
      expect(document.getElementById(hint!)!.textContent!.trim()).toBe('Arrow keys move through the results. Enter opens one.');
      // The field drives the grid's highlight, which it names as its active descendant.
      expect(document.getElementById(input.getAttribute('aria-controls')!)?.getAttribute('role')).toBe('grid');
      input.focus();
      press(input, 'ArrowDown');
      await tmSettle();
      const active = input.getAttribute('aria-activedescendant');
      expect(active).not.toBeNull();
      expect(host()!.querySelector(`[id="${active}"]`)).not.toBeNull();
      expect(await page.isClearSearchShown()).toBe(true);
    });

    it('reads twice for five keys typed 30 ms apart, aborting the first read, with the bar on from the first key', async () => {
      const { api } = await open();
      fakeClock();
      api.respond('customers/query', later(200, searched(customers(3))));
      field().focus();
      const keys = ['m', 'e', 'r', ' ', '1'];
      for (const [index, key] of keys.entries()) {
        if (index > 0) {
          await step(30);
        }
        typeKey(key);
        TestBed.tick();
        // The bar shows from the first key and stays through the burst: the rows on screen answer another text.
        expect(barShown()).toBe(true);
      }
      // The burst's trailing edge reads the whole text and aborts the leading edge's read, which still runs.
      await step(50);
      const calls = api.calls().filter((call) => call.route === 'customers/query');
      expect(calls.map((call) => (call.body as TmJsonObject)['search'] ?? null)).toEqual([null, 'm', 'mer 1']);
      expect(signalOf(calls[1]!).aborted).toBe(true);
      expect(signalOf(calls[2]!).aborted).toBe(false);
      expect(barShown()).toBe(true);
      await until(() => rowTexts().length === 1, 'the result for the whole text lands');
      expect(rowTexts()[0]![1]).toBe('Customer 1');
      // The result landed with focus in the field: its first row is highlighted.
      expect(field().getAttribute('aria-activedescendant')).not.toBeNull();
    });

    it('holds the bar for at least its minimum time when the result lands at once', async () => {
      const { api } = await open();
      fakeClock();
      api.respond('customers/query', searched(customers(3)));
      field().focus();
      typeKey('2');
      TestBed.tick();
      expect(barShown()).toBe(true);
      await until(() => rowTexts().length === 1, 'the result lands');
      await step(100);
      expect(barShown()).toBe(true);
      await step(200);
      expect(barShown()).toBe(false);
    });

    it('opens the first row of the result for the field’s text on an Enter that comes before it', async () => {
      const { api, rig, location } = await open();
      fakeClock();
      api.respond('customers/query', later(100, searched(customers(3))));
      field().focus();
      typeKey('r');
      await step(10);
      typeKey(' ');
      typeKey('3');
      press(field(), 'Enter');
      await until(() => address() === '/17/customers/3', 'the pending Enter opens the first row of the result for "r 3"');
      expect(location.getState()).toEqual(expect.objectContaining({ tmFrom: '/17/customers', tmPage: 1 }));
      expect(rig.telemetry.recent().find((item) => item.name === 'tm.search.open')?.props).toEqual({ via: 'pending', newTab: false });
    });

    it('opens the highlighted row on Enter over a fresh result', async () => {
      const { page } = await open();
      field().focus();
      press(field(), 'ArrowDown');
      press(field(), 'ArrowDown');
      await tmSettle();
      const active = await (await page.getGrid())!.getActiveRowIndex();
      expect(active).not.toBeNull();
      press(field(), 'Enter');
      await tmSettleUntil(() => address() === `/17/customers/${active! + 1}`, 'Enter opens the highlighted row');
    });

    it('disarms a pending Enter on a pointer press and on another key', async () => {
      const { api } = await open();
      fakeClock();
      api.respond('customers/query', later(100, searched(customers(3))));
      field().focus();
      typeKey('2');
      press(field(), 'Enter');
      document.body.dispatchEvent(new PointerEvent('pointerdown', { bubbles: true }));
      await until(() => rowTexts().length === 1, 'the result for "2" lands');
      await step(50);
      expect(address()).toBe('/17/customers?q=2');

      typeKey('2');
      press(field(), 'Enter');
      press(field(), 'ArrowLeft');
      await until(() => rowTexts().length === 0 && settled(), 'the result for "22" lands');
      await step(50);
      expect(address()).toBe('/17/customers?q=22');
    });

    it('says there are no matches for a pending Enter whose result is empty, once the keys pause', async () => {
      const announce = vi.spyOn(LiveAnnouncer.prototype, 'announce').mockResolvedValue(undefined);
      onTestFinished(() => announce.mockRestore());
      const { api } = await open();
      fakeClock();
      api.respond('customers/query', later(100, searched(customers(3))));
      announce.mockClear();
      field().focus();
      typeKey('z');
      press(field(), 'Enter');
      await until(() => settled() && rowTexts().length === 0, 'the empty result lands');
      await step(400);
      expect(announce).toHaveBeenCalledWith('No matches', 'polite');
      expect(address()).toBe('/17/customers?q=z');
    });

    it('announces a result only once no key was pressed for half a second', async () => {
      const announce = vi.spyOn(LiveAnnouncer.prototype, 'announce').mockResolvedValue(undefined);
      onTestFinished(() => announce.mockRestore());
      const { api } = await open();
      fakeClock();
      api.respond('customers/query', searched(customers(3)));
      announce.mockClear();
      field().focus();
      typeKey('2');
      await until(() => rowTexts().length === 1, 'the result lands');
      await step(300);
      expect(announce).not.toHaveBeenCalled();
      // A key that changes nothing still puts the announcement off.
      press(field(), 'Shift');
      await step(400);
      expect(announce).not.toHaveBeenCalled();
      await step(150);
      expect(announce).toHaveBeenCalledWith('1 result', 'polite');
    });

    it('takes a printable key the grid leaves unhandled, replacing the field’s text', async () => {
      const { page, rig } = await open('/17/customers?q=Customer', { respond: searched(customers(3)) });
      await (await page.getGrid())!.pressKeys(TestKey.DOWN_ARROW);
      const cell = document.activeElement!;
      expect(cell.getAttribute('role')).toBe('gridcell');
      press(cell, '2');
      expect(document.activeElement).toBe(field());
      expect(field().value).toBe('2');
      await tmSettleUntil(() => address() === '/17/customers?q=2', 'the typed key searches');
      await resettle();
      expect(rowTexts()).toHaveLength(1);
      expect(rig.telemetry.recent().some((item) => item.name === 'tm.search.type-to-search')).toBe(true);
    });

    it('takes a paste the grid leaves unhandled, on one line', async () => {
      const { page } = await open('/17/customers', { respond: searched(customers(3)) });
      await (await page.getGrid())!.pressKeys(TestKey.DOWN_ARROW);
      const data = new DataTransfer();
      data.setData('text/plain', 'Customer\n3');
      document.activeElement!.dispatchEvent(new ClipboardEvent('paste', { clipboardData: data, bubbles: true, cancelable: true }));
      expect(field().value).toBe('Customer 3');
      await tmSettleUntil(() => address() === '/17/customers?q=Customer%203', 'the paste searches');
    });

    it('undoes an accidental key with no request, back to the page and the highlighted row it left', async () => {
      const { api, page, rig } = await open('/17/customers?page=2', { respond: searched(customers(60)) });
      const grid = (await page.getGrid())!;
      await grid.pressKeys(TestKey.DOWN_ARROW, TestKey.DOWN_ARROW, TestKey.DOWN_ARROW);
      const active = await grid.getActiveRowIndex();
      expect(rowTexts()[0]![0]).toBe('C-051');

      press(document.activeElement!, 'C');
      await tmSettleUntil(() => address() === '/17/customers?q=C', 'the accidental key searches');
      await resettle();
      expect(rowTexts()[0]![0]).toBe('C-001');
      expect(reads(api)).toHaveLength(2);

      // Undo in the field takes the key back: the anchor's rows return from memory.
      expect(document.activeElement).toBe(field());
      document.execCommand('undo');
      await tmSettleUntil(() => address() === '/17/customers?page=2', 'the undo restores the page');
      await resettle();
      expect(field().value).toBe('');
      expect(rowTexts()[0]![0]).toBe('C-051');
      expect(reads(api)).toHaveLength(2);
      expect(await grid.getActiveRowIndex()).toBe(active);
      const timing = rig.telemetry
        .recent()
        .filter((item) => item.name === 'tm.search.query')
        .at(-1);
      expect(timing?.props).toEqual(expect.objectContaining({ source: 'anchor', outcome: 'ok' }));
    });

    it('clears on its clear button as an undoable edit, keeping focus in the field', async () => {
      const { api, page } = await open('/17/customers?q=Customer', { respond: searched(customers(3)) });
      await page.clearSearchField();
      await tmSettleUntil(() => address() === '/17/customers', 'the clear searches everything');
      expect(document.activeElement).toBe(field());
      expect(await page.getSearchText()).toBe('');
      expect(await page.isClearSearchShown()).toBe(false);
      document.execCommand('undo');
      await tmSettleUntil(() => address() === '/17/customers?q=Customer', 'the undo restores the text');
      expect(reads(api)).toHaveLength(2);
    });

    it('shows the text of a query from elsewhere: Clear search, a link and Back', async () => {
      const { page, rig, location } = await open('/17/customers?q=zzz', { respond: searched(customers(3)) });
      await page.clickStateAction('Clear search');
      await resettle();
      expect(await page.getSearchText()).toBe('');
      await rig.router.navigateByUrl('/17/customers?q=Customer%202');
      await resettle();
      expect(await page.getSearchText()).toBe('Customer 2');
      // The app's bootstrap listens for Back and Forward; the test bed's router starts without it.
      TestBed.inject(Router).setUpLocationChangeListener();
      location.back();
      await tmSettleUntil(() => rowTexts().length === 3, 'Back returns to the query before');
      await resettle();
      expect(address()).toBe('/17/customers');
      expect(await page.getSearchText()).toBe('');
    });
  });

  describe('the row menu', () => {
    it('opens the highlighted row in a new tab and copies its link', async () => {
      const announce = vi.spyOn(LiveAnnouncer.prototype, 'announce').mockResolvedValue(undefined);
      onTestFinished(() => announce.mockRestore());
      const opened = vi.spyOn(window, 'open').mockReturnValue(null);
      onTestFinished(() => opened.mockRestore());
      const written = vi.spyOn(navigator.clipboard, 'writeText').mockResolvedValue(undefined);
      onTestFinished(() => written.mockRestore());
      const { page } = await open();
      const grid = (await page.getGrid())!;
      await grid.pressKeys(TestKey.DOWN_ARROW);
      const row = (await grid.getActiveRowIndex())! + 1;

      let menu = await grid.openContextMenu();
      expect(await menu.getItemLabels()).toEqual(expect.arrayContaining(['Open in new tab', 'Copy link']));
      await menu.clickItem('Open in new tab');
      expect(opened).toHaveBeenCalledWith(`/17/customers/${row}`, '_blank', 'noopener');

      menu = await grid.openContextMenu();
      await menu.clickItem('Copy link');
      await tmSettle();
      expect(written).toHaveBeenCalledWith(new URL(`/17/customers/${row}`, document.baseURI).href);
      expect(announce).toHaveBeenCalledWith('Link copied', 'polite');
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

  describe('the More menu', () => {
    it('loads with a menu page action, opens from More and runs it', async () => {
      const run = vi.fn(() => Promise.resolve());
      const { rig, page } = await open('/17/customers', {
        customer: { ...CUSTOMER, pageActions: [{ id: 'acme.customer.export', labelKey: 'Acme_Export', group: 'menu', run }] },
        labels: { en: { Acme_Export: 'Export' } },
      });
      // The menu's code loads once there is a menu action; More shows when it has.
      await tmSettleUntil(() => host()?.querySelector('[data-tm-more]') != null, 'the More menu loads');
      expect(await page.hasMore()).toBe(true);

      host()!.querySelector<HTMLElement>('[data-tm-more]')!.click();
      await tmSettle();
      const menu = await TestbedHarnessEnvironment.documentRootLoader(rig.router.fixture).getHarness(TmMenuHarness);
      expect(await menu.getItemLabels()).toEqual(['Export']);
      await menu.clickItem('Export');
      await tmSettleUntil(() => run.mock.calls.length > 0, 'the action runs');
      expect(run).toHaveBeenCalledTimes(1);
    });

    it('never renders the menu for a page without a menu action', async () => {
      const { page } = await open();
      expect(await page.hasMore()).toBe(false);
      // The deferred block's content holds the menu itself, so no menu means the block stayed unloaded.
      expect(host()!.querySelector('.tm-search-host__more tm-menu')).toBeNull();
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

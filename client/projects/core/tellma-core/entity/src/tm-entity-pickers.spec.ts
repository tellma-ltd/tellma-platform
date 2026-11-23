// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { signal, type Type } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { TmApiError, type TmDataType, type TmJson, type TmJsonObject, type TmQueryRowSet } from '@tellma/core';
import type { TmFakeApiCall } from '@tellma/core-testing';
import type { TmEntityPickerPage } from '@tellma/core-ui/entity-picker';

import { TM_DISPLAY_NAME_GOLDEN as golden } from '../../src/entity/tm-display-name-golden.testing.util';
import { tmEntityRig, type TmEntityRig, type TmEntityRigOptions } from '../../src/entity/tm-entity.testing.util';
import { TM_T17, tmSettle } from '../../src/routes/tm-app.testing.util';
import { TmEntityPickers, ɵtmFindable, ɵtmPasteMatch, ɵTM_SEARCH_MATCH, type ɵTmFindablePlan, type ɵTmPickerTerm } from './tm-entity-pickers';
import { TmLazyPickerPage } from './tm-lazy-picker-page';

// The generic entity picker binding over the entity registry's test app:
// the query it sends, the cached search of a cacheable list, the labels and
// descriptions, the pages it offers by the user's grants, its telemetry,
// the grid's paste resolver, and the findable rule's rows in the shared
// display-name golden.

/** A user who may do everything. */
const SYSTEM: TmEntityRigOptions = { access: { isSystem: true } };

/** Bidi controls a pasted or typed text may carry: an RTL isolate and its pop. */
const RLI = '⁧';
const PDI = '⁩';

/**
 * Wire values at a dotted path of a wire row, as the server reads its
 * columns: each segment's JSON name, camel-cased.
 *
 * @param row - The row.
 * @param path - The path, such as `Parent.Code`.
 * @returns The value, or `null`.
 */
function at(row: TmJsonObject, path: string): TmJson {
  let value: TmJson | undefined = row;
  for (const segment of path.split('.')) {
    const json = segment.charAt(0).toLowerCase() + segment.slice(1);
    value = typeof value === 'object' && value !== null && !Array.isArray(value) ? (value as TmJsonObject)[json] : undefined;
  }
  return value ?? null;
}

/**
 * Answers a query route from a table: every row, whatever the filter, in
 * the select's columns, so the client's own matching decides.
 *
 * @param table - The rows.
 * @returns The responder.
 */
function serve(table: readonly TmJsonObject[]): (call: TmFakeApiCall) => { rows: TmQueryRowSet } {
  return (call) => {
    const select = String((call.body as { select: string }).select).split(',');
    return {
      rows: {
        columns: select.map((term) => ({
          name: term,
          type: table.some((row) => typeof at(row, term) === 'number') ? 'Numeric' : 'String',
          nullable: true,
          path: term.split('.'),
          groupingKey: false,
        })),
        rows: table.map((row) => select.map((term) => at(row, term))),
      },
    };
  };
}

/** A search's answer, as the binding gives it: a page of rows. */
interface TmSearchPage {
  /** The rows. */
  readonly items: readonly TmJson[];
  /** Whether more rows match. */
  readonly hasMore?: boolean;
}

/** A page offered by a binding, as its config object. */
type TmPageConfig = Exclude<TmEntityPickerPage, Type<unknown>>;

/**
 * A binding's page as its config object.
 *
 * @param page - The page.
 * @returns The config.
 */
function config(page: TmEntityPickerPage | undefined): TmPageConfig {
  expect(typeof page).toBe('object');
  return page as TmPageConfig;
}

/** The bodies of the reads of a route, oldest first. */
function bodies(rig: TmEntityRig, route: string): Record<string, unknown>[] {
  return rig.api
    .calls()
    .filter((call) => call.route === route)
    .map((call) => call.body as Record<string, unknown>);
}

/** The binding service of the rig's app. */
function pickers(): TmEntityPickers {
  return TestBed.inject(TmEntityPickers);
}

/** Customers named `1001 - Customer 1` and on, as many as asked. */
function customers(count: number): TmJsonObject[] {
  return Array.from({ length: count }, (_, index) => ({ id: index + 1, code: String(1001 + index), name: `Customer ${index + 1}` }));
}

/** The picker-search timings recorded so far. */
function searchTimings(rig: TmEntityRig): Record<string, unknown>[] {
  return rig.telemetry
    .recent()
    .filter((item) => item.name === 'tm.entity.picker-search')
    .map((item) => {
      const { ms, ...props } = item.props as Record<string, unknown>;
      expect(typeof ms).toBe('number');
      return { kind: item.kind, ...props };
    });
}

/** Currencies of a cached list: three active ones and an inactive one. */
const CURRENCIES: readonly TmJsonObject[] = [
  { id: 1, code: 'USD', name: 'US Dollar', isActive: true },
  { id: 2, code: 'SAR', name: 'Saudi Riyal', isActive: true },
  { id: 3, code: 'SAD', name: 'Old Saudi', isActive: false },
  { id: 4, code: 'EUR', name: 'Euro', isActive: true },
];

/**
 * Stores the currency list as a sync does, so the launch cache holds it.
 *
 * @param rig - The rig.
 */
async function cacheCurrencies(rig: TmEntityRig): Promise<void> {
  await rig.store.put(TM_T17, 'entity:acme.Currency', { tag: 'L1', savedAt: Date.now(), data: CURRENCIES });
  await rig.move(rig.docs, { 'acme.Currency': 'L1' });
}

describe('TmEntityPickers', () => {
  describe('the server search', () => {
    it('reads the query route: the select, the text without bidi controls and trimmed, the order, one row more than a page', async () => {
      const rig = await tmEntityRig({ ...SYSTEM, languages: 2 });
      rig.api.respond('customers/query', serve(customers(21)));
      const binding = pickers().binding('acme.Customer');

      const result = (await binding.search(`  ${RLI}Customer${PDI}  `, new AbortController().signal)) as TmSearchPage;
      const [body] = bodies(rig, 'customers/query');
      expect(body).toEqual({ select: 'Id,Name,Name2,Code', search: 'Customer', orderBy: 'Code, Name', take: 21 });
      expect([result.items.length, result.hasMore]).toEqual([20, true]);

      // Each row is remembered by name, so a chosen one shows at once.
      expect(binding.displayWith(3)).toBe('1003 - Customer 3');
    });

    it('caps the text at the entity’s longest search, browses with none, and reports no more on a short page', async () => {
      const rig = await tmEntityRig(SYSTEM);
      rig.api.respond('customers/query', serve(customers(4)));
      const binding = pickers().binding('acme.Customer');

      await binding.search('x'.repeat(250), new AbortController().signal);
      const result = (await binding.search('   ', new AbortController().signal)) as TmSearchPage;
      const [capped, browse] = bodies(rig, 'customers/query');
      expect((capped!['search'] as string).length).toBe(200);
      expect('search' in browse!).toBe(false);
      expect([result.items.length, result.hasMore]).toEqual([4, false]);
    });

    it('sends the declared filter and the field’s as separate leaves, and the declared order', async () => {
      const rig = await tmEntityRig({ ...SYSTEM, pickers: { 'acme.Customer': { filter: "CustomerType = 'Retail'", orderBy: 'Code:desc' } } });
      rig.api.respond('customers/query', serve([]));
      const filter = signal<string | null>("Code <> '0'");
      const binding = pickers().binding('acme.Customer', { filter });

      await binding.search('a', new AbortController().signal);
      filter.set('  ');
      await binding.search('a', new AbortController().signal);
      const [both, declared] = bodies(rig, 'customers/query');
      expect(both).toMatchObject({ filters: ["CustomerType = 'Retail'", "Code <> '0'"], orderBy: 'Code desc' });
      expect(declared!['filters']).toEqual(["CustomerType = 'Retail'"]);
    });

    it('times each search, an error too, and an abort not at all', async () => {
      const rig = await tmEntityRig(SYSTEM);
      const binding = pickers().binding('acme.Customer');
      rig.api.respond('customers/query', serve([]));
      await binding.search('a', new AbortController().signal);

      rig.api.respond('customers/query', () => {
        throw new TmApiError({ kind: 'server', status: 500 });
      });
      await expect(binding.search('b', new AbortController().signal)).rejects.toBeInstanceOf(TmApiError);

      const aborted = new AbortController();
      rig.api.respond('customers/query', () => new Promise(() => undefined));
      const pending = binding.search('c', aborted.signal);
      aborted.abort();
      await expect(pending).rejects.toBeDefined();

      expect(searchTimings(rig)).toEqual([
        { kind: 'timing', cached: false, outcome: 'ok' },
        { kind: 'timing', cached: false, outcome: 'error' },
      ]);
    });

    it('waits for the Entities chunk before its first search', async () => {
      const rig = await tmEntityRig({ ...SYSTEM, load: false });
      rig.api.respond('customers/query', serve(customers(1)));
      const binding = pickers().binding('acme.Customer');
      const result = (await binding.search('', new AbortController().signal)) as TmSearchPage;
      expect(result.items).toEqual([{ id: 1, code: '1001', name: 'Customer 1' }]);
    });

    it('refuses an entity the metadata does not list', async () => {
      await tmEntityRig(SYSTEM);
      expect(() => pickers().binding('acme.Ghost')).toThrowError("The metadata lists no entity 'acme.Ghost'.");
    });
  });

  describe('the cached search', () => {
    it('searches a cacheable list in memory at once: active rows that contain the text or that the findable rule finds, by name', async () => {
      const rig = await tmEntityRig(SYSTEM);
      await cacheCurrencies(rig);
      const binding = pickers().binding('acme.Currency');
      const search = (text: string): readonly TmJson[] => {
        const result = binding.search(text, new AbortController().signal) as TmSearchPage;
        // A synchronous answer: no debounce, no spinner, and offline too.
        expect(result).not.toBeInstanceOf(Promise);
        return result.items.map((item) => (item as TmJsonObject)['code']!);
      };

      expect(search('')).toEqual(['EUR', 'SAR', 'USD']);
      expect(search(`${RLI}sa${PDI}`)).toEqual(['SAR']);
      expect(search('dollar')).toEqual(['USD']);
      expect(search('#4')).toEqual(['EUR']);
      expect(search('4')).toEqual(['EUR']);
      expect(bodies(rig, 'currencies/query')).toEqual([]);
      expect(searchTimings(rig)[0]).toEqual({ kind: 'timing', cached: true, outcome: 'ok' });
    });

    it('queries the server while the list is absent, and for a field with a filter', async () => {
      const rig = await tmEntityRig(SYSTEM);
      rig.api.respond('currencies/query', serve(CURRENCIES.slice(0, 2)));
      await pickers().binding('acme.Currency').search('s', new AbortController().signal);
      expect(bodies(rig, 'currencies/query')).toHaveLength(1);

      await cacheCurrencies(rig);
      await pickers()
        .binding('acme.Currency', { filter: signal('DecimalPlaces = 2') })
        .search('s', new AbortController().signal);
      const [absent, filtered, ...more] = bodies(rig, 'currencies/query');
      expect(absent).toMatchObject({ search: 's' });
      expect(filtered).toMatchObject({ filters: ['DecimalPlaces = 2'], search: 's' });
      expect(more).toEqual([]);
    });
  });

  describe('labels and descriptions', () => {
    it('labels a row by its display name, and its parts apart', async () => {
      await tmEntityRig(SYSTEM);
      const customer = pickers().binding('acme.Customer');
      expect(customer.itemId({ id: 7, code: '1101', name: 'Cash' })).toBe(7);
      expect(customer.itemLabel({ id: 7, code: '1101', name: 'Cash' })).toBe('1101 - Cash');
      expect(customer.itemLabelParts({ id: 7, code: '1101', name: 'Cash' })).toEqual(['1101', 'Cash']);
      expect(pickers().binding('acme.Currency').itemLabelParts({ id: 1, code: 'USD' })).toEqual(['USD']);
    });

    it('describes a tree’s rows by their parent, and shows one line for a target with nothing to describe', async () => {
      const rig = await tmEntityRig(SYSTEM);
      rig.api.respond('centers/query', serve([{ id: 5, code: 'B-1', parent: { id: 1, code: 'B' } }]));
      const centers = pickers().binding('gl.Center');
      const { items } = (await centers.search('', new AbortController().signal)) as TmSearchPage;
      expect(bodies(rig, 'centers/query')[0]!['select']).toBe('Id,Parent.Id,Parent.Code,Code');
      expect(centers.itemDescription!(items[0]!)).toBe('B');
      expect(pickers().binding('acme.Customer').itemDescription).toBeUndefined();
    });

    it('describes a row by the declared paths, a related row by its name, the empty parts left out', async () => {
      const rig = await tmEntityRig({ ...SYSTEM, pickers: { 'acme.Customer': { description: ['Rank', 'Currency'] } } });
      rig.names.remember('acme.Currency', [{ id: 3, code: 'SAR' }]);
      const binding = pickers().binding('acme.Customer');
      expect(binding.itemDescription!({ id: 7, rank: 'Gold', currencyId: 3 })).toBe('Gold · SAR');
      expect(binding.itemDescription!({ id: 7, rank: 'Gold', currency: { id: 4, code: 'EUR' } })).toBe('Gold · EUR');
      expect(binding.itemDescription!({ id: 7, rank: '', currencyId: null })).toBeNull();
    });

    it('names a chosen id once its row is read, reading an unknown one', async () => {
      const rig = await tmEntityRig(SYSTEM);
      rig.api.respond('customers/query', serve([{ id: 9, code: '1109', name: 'Bank' }]));
      const binding = pickers().binding('acme.Customer');
      expect(binding.displayWith(9)).toBeNull();
      await vi.waitFor(async () => {
        await tmSettle();
        expect(binding.displayWith(9)).toBe('1109 - Bank');
      });
    });
  });

  describe('the pages', () => {
    it('offers advanced search, Create and Edit to a user who may save a target with a details page', async () => {
      await tmEntityRig(SYSTEM);
      const filter = signal<string | null>(null);
      const binding = pickers().binding('acme.Customer', { filter });
      expect(binding.createLabel).toBe('New customer…');
      expect(binding.advancedSearch).toMatchObject({ component: TmLazyPickerPage, size: 'lg', context: { entity: 'acme.Customer', filter: null, kind: 'search' } });
      expect(binding.create).toMatchObject({ title: 'New customer', context: { kind: 'create' } });
      expect(binding.edit).toMatchObject({ context: { kind: 'edit' } });
      expect(config(binding.edit).title).toBeUndefined();

      // The context is read when the picker launches the page.
      filter.set("Code = '1'");
      expect(config(binding.advancedSearch).context).toEqual({ entity: 'acme.Customer', filter: "Code = '1'", kind: 'search' });
    });

    it('offers no Create when turned off, no Edit to a multi-picker, and neither for a target with no details page', async () => {
      await tmEntityRig({ ...SYSTEM, pickers: { 'gl.Center': { create: false } } });
      const off = pickers().binding('acme.Customer', { create: false });
      expect([off.create, off.createLabel, off.edit === undefined]).toEqual([undefined, undefined, false]);
      const declared = pickers().binding('gl.Center');
      expect([declared.create, declared.edit === undefined]).toEqual([undefined, false]);
      expect(pickers().binding('acme.Customer', { multiple: true }).edit).toBeUndefined();
      const currency = pickers().binding('acme.Currency');
      expect([currency.advancedSearch === undefined, currency.create, currency.edit]).toEqual([false, undefined, undefined]);
    });

    it('offers nothing to a user who may not query the target', async () => {
      await tmEntityRig();
      const binding = pickers().binding('acme.Customer');
      expect([binding.advancedSearch, binding.create, binding.edit, binding.createLabel]).toEqual([undefined, undefined, undefined, undefined]);
    });
  });

  describe('the paste resolver', () => {
    it('resolves pasted names in one query: names, prefixes and halves, the bidi controls stripped', async () => {
      const rig = await tmEntityRig({ ...SYSTEM, languages: 2 });
      rig.api.respond(
        'customers/query',
        serve([
          { id: 7, code: '1101', name: 'Cash', name2: 'النقد' },
          { id: 8, code: '1102', name: 'Bank' },
          { id: 9, code: '2001', name: 'Twin' },
          { id: 10, code: '2002', name: 'Twin' },
        ]),
      );
      const resolve = pickers().ɵpasteResolver('acme.Customer', signal(" IsActive = true "));
      const labels = ['1101 - Cash', 'cash', 'النقد', `${RLI}Bank${PDI}`, '2002', '', 'Nope', 'Twin', "O'Brien"];
      const resolved = await resolve(labels, { locale: 'en', signal: new AbortController().signal });

      expect(Object.fromEntries(resolved)).toEqual({
        '1101 - Cash': { value: 7 },
        cash: { value: 7 },
        النقد: { value: 7 },
        [`${RLI}Bank${PDI}`]: { value: 8 },
        '2002': { value: 10 },
        '': { error: 'notFound' },
        Nope: { error: 'notFound' },
        Twin: { error: 'ambiguous' },
        "O'Brien": { error: 'notFound' },
      });
      const [body, ...more] = bodies(rig, 'customers/query');
      expect(more).toEqual([]);
      const names = "('1101 - Cash', 'cash', 'النقد', 'Bank', '2002', 'Nope', 'Twin', 'O''Brien')";
      expect(body).toEqual({
        select: 'Id,Name,Name2,Code',
        filters: ['IsActive = true', `Name in ${names} or Name2 in ${names} or Code in ${names} or (Code = '1101' and (Name = 'Cash' or Name2 = 'Cash'))`],
        take: 10000,
      });
    });

    it('splits the names over as few queries as the filter length allows', async () => {
      const rig = await tmEntityRig(SYSTEM);
      rig.api.respond('customers/query', serve([]));
      const labels = Array.from({ length: 400 }, (_, index) => `Customer number ${String(index).padStart(10, '0')}`);
      const resolved = await pickers().ɵpasteResolver('acme.Customer', signal(null))(labels, { locale: 'en', signal: new AbortController().signal });
      const filters = bodies(rig, 'customers/query').map((body) => (body['filters'] as string[])[0]!);
      expect(filters.length).toBeGreaterThan(1);
      expect(filters.every((filter) => filter.length <= 8192)).toBe(true);
      expect(filters.length).toBeLessThan(10);
      expect(resolved.size).toBe(400);
    });
  });
});

/** A findable row of the shared golden. */
interface GoldenFindable {
  readonly name: string;
  readonly rule: {
    readonly key: string;
    readonly twins?: readonly string[];
    readonly source?: string;
    readonly sourceType?: string;
    readonly prefix?: string;
    readonly prefixType?: string;
  };
  readonly shape: 'Primary' | 'PrimaryAndSecondary' | 'All';
  readonly rows: readonly TmJsonObject[];
  readonly text: string;
  readonly search: readonly number[];
  readonly paste: readonly number[];
}

/** The slots each of the golden's shapes admits. */
const SLOTS = { Primary: 1, PrimaryAndSecondary: 2, All: 3 } as const;

/**
 * The findable plan a golden rule implies under its shape: the key, the
 * admitted twins or the scalar source, and the prefix.
 *
 * @param row - The golden row.
 * @returns The plan.
 */
function findablePlan(row: GoldenFindable): ɵTmFindablePlan {
  const term = (name: string, type: string): ɵTmPickerTerm => ({ name, json: name.charAt(0).toLowerCase() + name.slice(1), type: type as TmDataType });
  const { rule } = row;
  return {
    key: term(rule.key, 'integer'),
    slots: rule.twins !== undefined ? rule.twins.slice(0, SLOTS[row.shape]).map((twin) => term(twin, 'string')) : [term(rule.source!, rule.sourceType!)],
    prefix: rule.prefix === undefined ? null : term(rule.prefix, rule.prefixType ?? 'string'),
  };
}

describe('the findable golden', () => {
  const rows = (golden as unknown as { readonly findable: readonly GoldenFindable[] }).findable;

  it.each(rows.map((row) => [row.name, row] as const))('a search: %s', (_, row) => {
    const found = ɵtmFindable(findablePlan(row), row.text, ɵTM_SEARCH_MATCH);
    expect(row.rows.filter(found).map((candidate) => candidate['id'])).toEqual(row.search);
  });

  it.each(rows.map((row) => [row.name, row] as const))('a paste: %s', (_, row) => {
    const pasted = ɵtmPasteMatch(findablePlan(row), row.text, 'en');
    expect(row.rows.filter(pasted).map((candidate) => candidate['id'])).toEqual(row.paste);
  });
});

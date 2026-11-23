// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { tmTestEntities } from '@tellma/core-testing';
import { tmGregorianCalendar } from '@tellma/core-ui/l10n';

import { TmApiError } from '../api/tm-api-error';
import type { TmDecimal, TmJsonObject } from '../json/tm-json';
import { TM_T17 } from '../routes/tm-app.testing.util';
import type { TmAccessSummary } from '../state/launch/tm-launch-docs';
import { TmCacheRefresher } from '../state/sync/tm-cache-refresher';
import golden from './display-name-golden.json';
import { TmDisplayName } from './tm-display-name';
import {
  ɵtmDisplayNameParts,
  ɵtmDisplayNamePlanOf,
  ɵtmDisplayNameSelect,
  ɵtmDisplayNameText,
  type TmDisplayNameParts,
  type ɵTmDisplayNamePlan,
} from './tm-display-names';
import { tmEntityDocs, tmEntityRig, type TmEntityRig, type TmEntityRigOptions } from './tm-entity.testing.util';
import type { TmEntityDescriptor } from './tm-entity-descriptors';
import type { TmDataType, TmDisplayNameRule } from './tm-entity-metadata';
import type { TmQueryRowSet } from './tm-rows-to-objects';

// Display names: the shared golden's rows through the registry's own plan,
// then the registry over the server's recorded model: rendering from wire
// rows, the select a display name reads, names by id from the held rows,
// the cacheable lists and the background reads, and the rules that keep or
// drop the held rows.

/** A rule as the golden spells it. */
interface GoldenRule {
  readonly key: string;
  readonly twins?: readonly (string | null)[];
  readonly source?: string;
  readonly sourceType?: string;
  readonly prefix?: string;
}

/** The slots each of the golden's shapes admits. */
const SLOTS = { Primary: 1, PrimaryAndSecondary: 2, All: 3 } as const;

/**
 * The descriptor facts a golden rule implies: the key, each twin the group
 * has with its slot, or the scalar source, and the prefix.
 */
function goldenDescriptor(rule: GoldenRule): TmEntityDescriptor {
  const property = (name: string, type: string, multilingual?: { group: string; slot: number }) => ({
    name,
    json: name.charAt(0).toLowerCase() + name.slice(1),
    type: type as TmDataType,
    ...(multilingual && { multilingual }),
  });
  const properties = [
    property(rule.key, 'integer'),
    ...(rule.twins ?? []).flatMap((twin, index) => (twin === null ? [] : [property(twin, 'string', { group: rule.twins![0]!, slot: index + 1 })])),
    ...(rule.source !== undefined && rule.source !== rule.key ? [property(rule.source, rule.sourceType!)] : []),
    ...(rule.prefix !== undefined ? [property(rule.prefix, 'string')] : []),
  ];
  return {
    key: { property: rule.key },
    properties,
    property: (name: string) => properties.find((candidate) => candidate.name.toLowerCase() === name.toLowerCase()),
  } as unknown as TmEntityDescriptor;
}

/** A golden rule as the Core chunk's summary carries it. */
function summaryRule(rule: GoldenRule): TmDisplayNameRule {
  return { source: rule.twins?.[0] ?? rule.source!, multilingual: rule.twins !== undefined, ...(rule.prefix !== undefined && { prefix: rule.prefix }) };
}

/**
 * The plan of a golden rule under a shape, as the registry builds it: from
 * the descriptor once loaded, or from the summary alone before.
 */
function goldenPlan(rule: GoldenRule, shape: keyof typeof SLOTS, loaded = true): ɵTmDisplayNamePlan {
  return ɵtmDisplayNamePlanOf(summaryRule(rule), SLOTS[shape], loaded ? goldenDescriptor(rule) : null);
}

/** One listed securable. */
function grant(resource: string, action: string): TmAccessSummary['securables'][number] {
  return { resource, action, hasFilter: false };
}

/** A customer row as the wire carries it. */
const CASH: TmJsonObject = { id: 7, code: '1101', name: 'Cash', name2: 'النقد' };

/** A query's row set of customers, the display name's columns only, under one language. */
function customerRows(...rows: readonly (readonly [number, string, string])[]): TmQueryRowSet {
  return {
    columns: [
      { name: 'Id', type: 'Numeric', nullable: false, path: ['Id'], groupingKey: false },
      { name: 'Name', type: 'String', nullable: true, path: ['Name'], groupingKey: false },
      { name: 'Code', type: 'String', nullable: true, path: ['Code'], groupingKey: false },
    ],
    rows: rows.map(([id, name, code]) => [id, name, code]),
  };
}

describe('display names', () => {
  describe('the shared golden', () => {
    it.each(golden.render.map((row) => [row.name, row] as const))('%s', (_, row) => {
      const plan = goldenPlan(row.rule as GoldenRule, row.shape as keyof typeof SLOTS);
      const parts = ɵtmDisplayNameParts(plan, row.row as unknown as TmJsonObject, row.slot as 1 | 2 | 3, { locale: row.locale, calendar: tmGregorianCalendar() }, (id) => `#${id}`);
      expect(ɵtmDisplayNameText(parts)).toBe(row.expected);
    });

    it.each(golden.expansion.map((row) => [row.name, row] as const))('%s', (_, row) => {
      const plan = goldenPlan(row.rule as GoldenRule, row.shape as keyof typeof SLOTS);
      expect(ɵtmDisplayNameSelect(plan, row.navigation ?? undefined)).toEqual(row.columns);
    });

    it.each(golden.expansion.filter((row) => !(row.rule.twins ?? []).includes(null)).map((row) => [row.name, row] as const))(
      'before the Entities chunk loads: %s',
      (_, row) => {
        // Before the load the twins follow the naming convention, which every rule with all its twins obeys.
        const plan = goldenPlan(row.rule as GoldenRule, row.shape as keyof typeof SLOTS, false);
        expect(ɵtmDisplayNameSelect(plan, row.navigation ?? undefined)).toEqual(row.columns);
      },
    );

    it('has the client-only rows the server skips', () => {
      expect(golden.render.filter((row) => 'clientOnly' in row && row.clientOnly).map((row) => row.locale)).toEqual(['ar-SA']);
    });
  });

  describe('TmDisplayNames', () => {
    let rig: TmEntityRig;

    /** Starts the app, with a user who may read customers unless the options say otherwise. */
    async function start(options: TmEntityRigOptions = {}): Promise<TmEntityRig> {
      rig = await tmEntityRig({ access: { securables: [grant('acme.Customer', 'Read')] }, ...options });
      return rig;
    }

    afterEach(() => TestBed.resetTestingModule());

    describe('of and parts', () => {
      it('render a wire row in the active slot, memoized per row', async () => {
        const { names } = await start({ languages: 2 });

        expect(names.of('acme.Customer', CASH)()).toBe('1101 - Cash');
        expect(names.parts('acme.Customer', CASH)()).toEqual({ prefix: '1101', name: 'Cash' });
        expect(names.of('acme.Customer', CASH)).toBe(names.of('acme.Customer', CASH));
        expect(names.of('acme.Customer', { ...CASH, code: null })()).toBe('Cash');
        expect(names.of('acme.Customer', { id: 12 })()).toBe('#12');
      });

      it('follow the user’s language into its slot', async () => {
        const { names } = await start({ languages: 2, language: 'ar' });
        expect(names.of('acme.Customer', CASH)()).toBe('1101 - النقد');
      });

      it('render before the Entities chunk loads, from the summary', async () => {
        const { names, entities } = await start({ languages: 2, load: false });
        const text = names.of('acme.Customer', CASH);
        expect(text()).toBe('1101 - Cash');

        await entities.load();
        expect(text()).toBe('1101 - Cash');
      });

      it('render a scalar source', async () => {
        const { names } = await start();
        expect(names.of('acme.Currency', { id: 3, code: 'USD' })()).toBe('USD');
        expect(names.of('core.Job', { id: 1234567 })()).toBe('1234567');
      });
    });

    describe('select', () => {
      it('reads the key, the admitted twins and the prefix', async () => {
        const two = await start({ languages: 2 });
        expect(two.names.select('acme.Customer')).toEqual(['Id', 'Name', 'Name2', 'Code']);
        expect(two.names.select('acme.Customer', 'Customer')).toEqual(['Customer.Id', 'Customer.Name', 'Customer.Name2', 'Customer.Code']);
        expect(two.names.select('gl.Center', 'Parent')).toEqual(['Parent.Id', 'Parent.Code']);
        expect(two.names.select('core.Job')).toEqual(['Id']);
        TestBed.resetTestingModule();

        const three = await start({ languages: 3 });
        expect(three.names.select('acme.Customer')).toEqual(['Id', 'Name', 'Name2', 'Name3', 'Code']);
      });

      it('needs the Entities chunk in development', async () => {
        const { names } = await start({ load: false });
        expect(() => names.select('acme.Customer')).toThrow(/TmDisplayNames\.select needs the Entities chunk/);
      });

      it('refuses an unknown entity', async () => {
        const { names } = await start();
        expect(() => names.select('acme.Ghost')).toThrow("The metadata lists no entity 'acme.Ghost'.");
      });
    });

    describe('label', () => {
      it('names a remembered row, whichever form its id takes', async () => {
        const { names } = await start();
        const label = names.label('acme.Customer', 7);
        expect(label()).toBeNull();

        names.remember('acme.Customer', [CASH]);
        expect(label()).toBe('1101 - Cash');
        // An int key given as decimal text is coerced.
        expect(names.label('acme.Customer', '7' as TmDecimal)()).toBe('1101 - Cash');
        expect(names.labelParts('acme.Customer', 7)()).toEqual({ prefix: '1101', name: 'Cash' });
      });

      it('names a long key’s row given its id as a number', async () => {
        const { chunk } = tmTestEntities();
        const long = {
          ...chunk,
          entities: chunk.entities.map((entity) => (entity.name === 'acme.Customer' ? { ...entity, key: { property: 'Id', type: 'long' as const } } : entity)),
        };
        const { names } = await start({ chunk: long });

        names.remember('acme.Customer', [{ ...CASH, id: '1284' }]);
        expect(names.label('acme.Customer', 1284)()).toBe('1101 - Cash');
        expect(names.label('acme.Customer', '1284' as TmDecimal)()).toBe('1101 - Cash');
      });

      it('names a row by id where the server is never asked', async () => {
        const { names } = await start();

        // core.Blob has no query route; the user may not read gl.Center.
        expect(names.label('core.Blob', 5)()).toBe('#5');
        expect(names.label('gl.Center', 5)()).toBe('#5');
        expect(names.label('acme.Ghost', 5)()).toBe('#5');
      });

      it('names a cacheable entity’s row from its list', async () => {
        const { names, store, move, docs } = await start({ access: { securables: [grant('acme.Currency', 'Read')] } });
        await store.put(TM_T17, 'entity:acme.Currency', { tag: 'L1', savedAt: Date.now(), data: [{ id: 3, code: 'USD' }] });
        await move(docs, { 'acme.Currency': 'L1' });

        expect(names.label('acme.Currency', 3)()).toBe('USD');
        expect(names.label('acme.Currency', '3' as TmDecimal)()).toBe('USD');
      });

      it('remembers a row set and related rows', async () => {
        const { names } = await start();

        names.remember('acme.Customer', customerRows([8, 'Bank', '1102']));
        names.rememberRelated({ 'acme.Currency': [{ id: 4, code: 'EUR' }], 'acme.Ghost': [{ id: 1 }] });
        expect(names.label('acme.Customer', 8)()).toBe('1102 - Bank');
        expect(names.label('acme.Currency', 4)()).toBe('EUR');
      });

      it('holds at most 5,000 rows per entity, the least recently used going first', async () => {
        const { names } = await start();
        const rows = Array.from({ length: 5000 }, (_, index) => ({ id: index + 1, code: `C${index + 1}`, name: null }));
        names.remember('acme.Customer', rows);

        // Reading the first row makes the second the least recently used.
        expect(names.label('acme.Customer', 1)()).toBe('C1');
        names.remember('acme.Customer', [{ id: 5001, code: 'C5001', name: null }]);
        expect(names.label('acme.Customer', 1)()).toBe('C1');
        expect(names.label('acme.Customer', 2)()).toBeNull();
        expect(names.label('acme.Customer', 5001)()).toBe('C5001');
      });
    });

    describe('tag moves', () => {
      it('drop the held rows when the tenant’s languages change', async () => {
        const { names, move } = await start({ languages: 2 });
        names.remember('acme.Customer', [CASH]);
        const label = names.label('acme.Customer', 7);
        expect(label()).toBe('1101 - Cash');

        const docs = tmEntityDocs({ languages: 3, access: { securables: [grant('acme.Customer', 'Read')] } });
        await move({ ...docs, me: { ...docs.me, tags: { ...docs.me.tags, settings: 'S2' } } });
        expect(label()).toBeNull();
      });

      it('keep them through any other settings move and a permissions move', async () => {
        const { names, move, docs } = await start({ languages: 2 });
        names.remember('acme.Customer', [CASH]);
        const label = names.label('acme.Customer', 7);

        await move({ ...docs, settings: { ...docs.settings, timeZone: 'Asia/Dubai' }, me: { ...docs.me, tags: { ...docs.me.tags, settings: 'S2' } } });
        expect(label()).toBe('1101 - Cash');

        await move({ ...docs, me: { ...docs.me, access: { ...docs.me.access, tag: 'A2', securables: [] }, tags: { ...docs.me.tags, permissions: 'A2' } } });
        expect(label()).toBe('1101 - Cash');
      });
    });

    describe('resolve', () => {
      it('reads a frame’s unknown ids in one background query', async () => {
        const { names, api, telemetry } = await start();
        // A query answers with a page of rows.
        api.respond('customers/query', { rows: customerRows([7, 'Cash', '1101'], [8, 'Bank', '1102']) });
        names.remember('acme.Customer', [{ id: 6, code: 'Known', name: null }]);

        names.resolve('acme.Customer', [7, 8, 6]);
        names.resolve('acme.Customer', ['9' as TmDecimal, 7]);
        await vi.waitFor(() => expect(names.label('acme.Customer', 9)()).toBe('#9'));

        const queries = api.calls().filter((call) => call.route === 'customers/query');
        expect(queries).toHaveLength(1);
        expect(queries[0]?.body).toEqual({ select: 'Id,Name,Code', filters: ['Id in (7, 8, 9)'], take: 3, includeInactive: true });
        const options = queries[0]?.options as { readonly background?: boolean; readonly tenantId?: number; readonly signal?: AbortSignal };
        expect([options.background, options.tenantId]).toEqual([true, 17]);
        // The signal the tenant's background reads abort with when the tab leaves it.
        expect(options.signal).toBe(TestBed.inject(TmCacheRefresher).ɵsignal(17));
        expect(names.label('acme.Customer', 7)()).toBe('1101 - Cash');
        expect(names.label('acme.Customer', 8)()).toBe('1102 - Bank');
        expect(telemetry.recent().filter((item) => item.name === 'tm.entity.names-resolved').map((item) => item.props)).toEqual([expect.objectContaining({ count: '2-10' })]);

        // Known and missing ids are never asked again.
        names.resolve('acme.Customer', [7, 9]);
        await new Promise((resolve) => setTimeout(resolve, 50));
        expect(api.calls().filter((call) => call.route === 'customers/query')).toHaveLength(1);
      });

      it('never asks about ids a cacheable entity’s loaded list holds', async () => {
        const { names, api, store, move, docs } = await start({ access: { securables: [grant('acme.Currency', 'Read')] } });
        await store.put(TM_T17, 'entity:acme.Currency', { tag: 'L1', savedAt: Date.now(), data: [{ id: 3, code: 'USD' }] });
        await move(docs, { 'acme.Currency': 'L1' });
        api.respond('currencies/query', { rows: { columns: [], rows: [] } });

        names.resolve('acme.Currency', [3, '3' as TmDecimal]);
        await new Promise((resolve) => setTimeout(resolve, 50));
        expect(api.calls().filter((call) => call.route === 'currencies/query')).toEqual([]);

        // An id the list lacks is still asked, alone.
        names.resolve('acme.Currency', [3, 9]);
        await vi.waitFor(() => expect(names.label('acme.Currency', 9)()).toBe('#9'));
        expect(api.calls().filter((call) => call.route === 'currencies/query').map((call) => (call.body as { filters: unknown }).filters)).toEqual([['Id in (9)']]);
      });

      it('never asks about an entity the user cannot query', async () => {
        const { names, api } = await start({ access: { securables: [] } });
        api.respond('customers/query', { rows: customerRows() });

        names.resolve('acme.Customer', [7]);
        names.resolve('core.Blob', [5]);
        await new Promise((resolve) => setTimeout(resolve, 50));
        expect(api.calls().filter((call) => call.route.endsWith('/query'))).toEqual([]);
      });

      it('names refused ids by id, and leaves failed ones unknown', async () => {
        const { names, api } = await start();
        api.respond('customers/query', () => {
          throw new TmApiError({ kind: 'forbidden', status: 403, code: 'forbidden' });
        });
        names.resolve('acme.Customer', [7]);
        await vi.waitFor(() => expect(names.label('acme.Customer', 7)()).toBe('#7'));

        api.respond('customers/query', () => {
          throw new TmApiError({ kind: 'server', status: 500, code: 'server' });
        });
        names.resolve('acme.Customer', [8]);
        await vi.waitFor(() => expect(api.calls().filter((call) => call.route === 'customers/query')).toHaveLength(2));
        await new Promise((resolve) => setTimeout(resolve, 10));
        expect(names.label('acme.Customer', 8)()).toBeNull();
      });
    });
  });

  describe('tm-display-name', () => {
    /** A host for the component. */
    @Component({
      imports: [TmDisplayName],
      template: `<span dir="rtl"><tm-display-name [parts]="parts()" /></span>`,
    })
    class Host {
      /** The parts to render. */
      readonly parts = signal<TmDisplayNameParts | null>(null);
    }

    it('isolates the prefix and the name, each in a bdi', async () => {
      const fixture = TestBed.createComponent(Host);
      const element = (): HTMLElement => fixture.nativeElement.querySelector('tm-display-name') as HTMLElement;
      await fixture.whenStable();
      expect(element().innerHTML.replaceAll(/<!--.*?-->/g, '')).toBe('');

      fixture.componentInstance.parts.set({ prefix: '1101', name: 'النقد' });
      await fixture.whenStable();
      expect([...element().querySelectorAll('bdi')].map((bdi) => bdi.textContent)).toEqual(['1101', 'النقد']);
      expect(element().textContent).toBe('1101 - النقد');

      fixture.componentInstance.parts.set({ prefix: null, name: '#7' });
      await fixture.whenStable();
      expect([...element().querySelectorAll('bdi')].map((bdi) => bdi.textContent)).toEqual(['#7']);
    });
  });
});

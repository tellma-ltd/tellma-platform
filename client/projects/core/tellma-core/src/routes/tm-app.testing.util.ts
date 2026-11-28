// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { ApplicationInitStatus, ChangeDetectionStrategy, Component, input, type Provider, type Type } from '@angular/core';
import { provideLocationMocks } from '@angular/common/testing';
import { TestBed } from '@angular/core/testing';
import { Router } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import {
  provideTellmaStateTesting,
  provideTellmaTesting,
  TmFakeApi,
  TmFakeTelemetry,
  tmTestEntitiesChunk,
  tmTestMetadata,
  tmWarmTenant,
} from '@tellma/core-testing';

import { TM_API_UPDATES, type TmApiUpdateCheck } from '../api/tm-api-seams';
import { TM_CORE_WIDGETS } from '../dashboards/tm-core-widgets';
import { tmWidget, type TmWidgetDefinition } from '../dashboards/tm-widget';
import { provideTellma } from '../app/provide-tellma';
import type { TmAppConfig, TmLanguageDefinition } from '../app/tm-app-config';
import { tmFeature, type TmFeature } from '../declarations/tm-feature';
import { tmPage } from '../declarations/tm-page';
import { tmEntity } from '../entity/tm-entity-declaration';
import { tmEnglish } from '../l10n/tm-english';
import type { TmEntitySummary } from '../entity/tm-entity-metadata';
import type { TmMembership } from '../launch/tm-membership';
import type { TmClientMetadata } from '../metadata/tm-client-metadata';
import type { TmMetadataLoaders } from '../metadata/tm-metadata-loaders';
import type { TmLaunchDocs, TmMeResult, TmTenantFacts, TmTenantSettingsForClient } from '../state/launch/tm-launch-docs';
import { TmClientStore, type TmStoredDoc } from '../state/store/tm-client-store';
import type { TmTenantPartition } from '../state/store/tm-store-layout';
import { TmUrls } from '../urls/tm-urls';

// A whole app over the fake API for the route specs: provideTellma with the
// test metadata and a test feature, the in-memory client state, and tenant
// 17's launch documents stored unless the spec asks for a cold entry. The
// feature declares a details page for centers and customers, none for
// currencies, a page, a studio page under an entity segment, and a gated page.

/** A page component for every declared page, which shows its inputs. */
@Component({
  selector: 'tm-test-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `<h1 tmPageTitle>Test page {{ entity() }} {{ id() }}</h1>`,
})
export class TmTestPage {
  /** The routed entity. */
  readonly entity = input<string>('');
  /** The routed id. */
  readonly id = input<string>('');
}

/** Loads the test page. */
export const tmLoadTestPage = vi.fn((): Promise<Type<unknown>> => Promise.resolve(TmTestPage));

/** The signed-in test user's sub. */
export const TM_TEST_SUB = 'test-user';

/** Tenant 17's partition. */
export const TM_T17: TmTenantPartition = { kind: 'tenant', sub: TM_TEST_SUB, tenantId: 17 };

/** A summary of a test entity beyond the fixture's. */
function summary(name: string, segment: string, kind: TmEntitySummary['kind']): TmEntitySummary {
  const label = name.replace('.', '_');
  return {
    name,
    segment,
    labelKey: label,
    pluralLabelKey: `${label}_Plural`,
    newLabelKey: `${label}_New`,
    labelFallback: name,
    operations: ['query', 'details', 'save'],
    capabilities: [],
    displayName: { source: 'Name', multilingual: false },
    kind,
  };
}

/**
 * The test metadata, with a stack that has no details page, a child entity,
 * and the stored dashboards. The stored reports (`core.Report`) are the
 * platform's own, already in the test metadata as the server emits them.
 */
export function tmRouteTestMetadata(): TmClientMetadata {
  const base = tmTestMetadata();
  return tmTestMetadata({
    entities: [
      ...base.entities,
      summary('acme.Currency', 'currencies', 'stack'),
      summary('acme.SalesInvoiceLine', 'sales-invoice-lines', 'child'),
      summary('core.Dashboard', 'dashboards', 'stack'),
    ],
  });
}

/**
 * The route test metadata and a feature that give assignments their own
 * search page at `/17/assignments`, with the lists' two tabs, so the home's
 * assignment lists link to it.
 *
 * @returns The metadata, and the feature that declares the page.
 */
export function tmAssignmentsTestRoute(): { readonly metadata: TmClientMetadata; readonly feature: TmFeature } {
  const base = tmRouteTestMetadata();
  return {
    metadata: { ...base, entities: [...base.entities, summary('core.Assignment', 'assignments', 'stack')] },
    feature: tmFeature({
      key: 'acme-assignments',
      entities: [
        tmEntity('core.Assignment', {
          tabs: [
            { key: 'to-me', labelKey: 'Tab_ToMe' },
            { key: 'by-me', labelKey: 'Tab_ByMe' },
          ],
          search: tmLoadTestPage,
        }),
      ],
    }),
  };
}

/**
 * A feature that redeclares a core widget type with a permission the test
 * user lacks, as an app may, so the grid hides or locks its tiles.
 *
 * @param key - The core widget type.
 * @returns The feature.
 */
export function tmGatedCoreWidget(key: string): TmFeature {
  const core = TM_CORE_WIDGETS.find((widget) => widget.key === key);
  if (core === undefined) {
    throw new Error(`No core widget type '${key}'.`);
  }
  return tmFeature({
    key: 'acme-gate',
    overrides: [tmWidget(key, { ...(core as unknown as TmWidgetDefinition), requires: ['acme.Approval', 'Read'] })],
  });
}

/** The test feature's declarations. */
export function tmRouteTestFeature(): TmFeature {
  return tmFeature({
    key: 'acme',
    entities: [
      tmEntity('gl.Center', {
        tabs: [{ key: 'all', labelKey: 'Tab_All' }],
        columns: ['Code', 'Name'],
        search: tmLoadTestPage,
        details: { page: tmLoadTestPage, create: tmLoadTestPage },
      }),
      tmEntity('acme.Customer', { details: tmLoadTestPage }),
      tmEntity('acme.Currency', {}),
      // Stored reports and dashboards, whose details pages draw their own skeletons.
      tmEntity('core.Report', { details: tmLoadTestPage }),
      tmEntity('core.Dashboard', { details: tmLoadTestPage }),
    ],
    pages: [
      tmPage('credit-review', { component: tmLoadTestPage, titleKey: 'Acme_CreditReview' }),
      tmPage('centers/:id/map', { component: tmLoadTestPage, titleKey: 'Acme_CenterMap' }),
      tmPage('approvals', { component: tmLoadTestPage, titleKey: 'Acme_Approvals', securable: ['acme.Approval', 'Read'] }),
    ],
  });
}

/** Tenant 17's settings. */
const SETTINGS: TmTenantSettingsForClient = {
  tenantId: 17,
  names: ['Acme', null],
  languages: [{ code: 'en', nativeName: 'English', symbol: 'En', isRightToLeft: false }],
  calendars: ['gc'],
  timeZone: 'Asia/Riyadh',
  entries: {},
};

/** Tenant 17's facts. */
const TENANT: TmTenantFacts = { id: 17, category: 'Live', state: 'Active', realtime: true };

/** Tenant 17's envelope. */
const ME: TmMeResult = {
  user: { id: 42, kind: 'Individual', state: 'Joined', name: 'Farah', modifiedAt: '2026-11-01T00:00:00.0000000+00:00' },
  preferencesTag: 'P1',
  preferences: {},
  access: { tag: 'A1', formatVersion: 1, isSystem: false, securables: [], problems: [] },
  tags: { settings: 'S1', preferences: 'P1', permissions: 'A1', entities: 'E1' },
};

/** The answer of a cold `client/sync` into tenant 17. */
export const TM_COLD_SYNC = {
  tenant: TENANT,
  tags: { settings: 'S1', preferences: 'P1', permissions: 'A1', entities: 'E1' },
  settings: SETTINGS,
  me: ME,
  staleEntities: [],
};

/** A live, active membership. */
export function tmTestMembership(id: number, patch: Partial<TmMembership> = {}): TmMembership {
  return {
    id,
    name: `Tenant ${id}`,
    name2: null,
    name3: null,
    languages: ['en'],
    category: 'Live',
    state: 'Active',
    liveTenantId: null,
    isActive: true,
    ...patch,
  } as TmMembership;
}

/** Tenant 17's launch documents, which the rig stores with a recent sync unless the entry is cold. */
export const TM_T17_DOCS: TmLaunchDocs = { settings: SETTINGS, me: ME, tenant: TENANT };

/** A stored document. */
function stored<T>(data: T): TmStoredDoc<T> {
  return { tag: null, savedAt: Date.now() - 60_000, data };
}

/** What a route spec drives and inspects. */
export interface TmAppRig {
  /** The router. */
  readonly router: Router;
  /** The app's URLs. */
  readonly urls: TmUrls;
  /** The fake API. */
  readonly api: TmFakeApi;
  /** The client store. */
  readonly store: TmClientStore;
  /** The fake telemetry. */
  readonly telemetry: TmFakeTelemetry;
}

/** How a route spec sets the app up. */
export interface TmAppRigOptions {
  /** Whether tenant 17's launch documents are missing, so its entry is cold. */
  readonly cold?: boolean;
  /** Tenant 17's launch documents, when not `TM_T17_DOCS`. */
  readonly docs?: TmLaunchDocs;
  /** The memberships stored for the user, if any. */
  readonly tenants?: readonly TmMembership[];
  /** What the worker's update check answers. */
  readonly update?: TmApiUpdateCheck;
  /** More providers, after the rig's. */
  readonly providers?: readonly Provider[];
  /** The app's features, when not the test feature alone. */
  readonly features?: readonly TmFeature[];
  /** The app's languages, when not English alone. */
  readonly languages?: readonly TmLanguageDefinition[];
  /** The distribution's strings by language, merged over the test strings in English. */
  readonly strings?: Readonly<Record<string, Readonly<Record<string, string>>>>;
  /** The Core chunk, when not the route test metadata. */
  readonly metadata?: TmClientMetadata;
}

/**
 * The test app's configuration: resolving metadata loaders over the route
 * test metadata, English strings, and the test feature, unless the options
 * name others.
 *
 * @param options - The features, languages, strings and Core chunk, when not the defaults.
 * @returns The configuration.
 */
export function tmRouteTestConfig(options: Pick<TmAppRigOptions, 'features' | 'languages' | 'strings' | 'metadata'> = {}): TmAppConfig {
  const metadata = options.metadata ?? tmRouteTestMetadata();
  // English always holds the test strings; each language's own strings merge over them.
  const strings: Record<string, Readonly<Record<string, string>>> = {
    ...options.strings,
    en: {
      Distribution_DisplayName: 'Acme',
      gl_Center_Plural: 'Centers',
      gl_Center: 'Center',
      // A destructive page action's label and confirmation.
      Acme_Center_Delete: 'Delete center',
      Acme_Center_DeleteTitle: 'Delete {name}?',
      Acme_Center_DeleteMessage: '{name} and its {count} children will be deleted.',
      ...options.strings?.['en'],
    },
  };
  const loaders: TmMetadataLoaders = {
    core: () => Promise.resolve(metadata),
    entities: () => Promise.resolve(tmTestEntitiesChunk()),
    contracts: () => Promise.resolve({ fingerprints: {}, long: [], idempotent: [] }),
    strings: Object.fromEntries(Object.entries(strings).map(([code, table]) => [code, () => Promise.resolve(table)])),
  };
  return {
    metadata: loaders,
    languages: options.languages ?? [tmEnglish()],
    features: options.features ?? [tmRouteTestFeature()],
  };
}

/**
 * Configures and starts the app's providers, waits for its initializers,
 * and stores what the options ask for.
 *
 * @param options - The rig's options.
 * @returns The rig.
 */
export async function tmAppRig(options: TmAppRigOptions = {}): Promise<TmAppRig> {
  TestBed.configureTestingModule({
    providers: [
      provideTellma(tmRouteTestConfig(options)),
      provideTellmaTesting(),
      provideTellmaStateTesting(),
      // The router writes to a mock location, never the test page's address.
      provideLocationMocks(),
      { provide: TM_API_UPDATES, useValue: { check: () => Promise.resolve(options.update ?? 'none') } },
      ...(options.providers ?? []),
    ],
  });
  await TestBed.inject(ApplicationInitStatus).donePromise;
  const store = TestBed.inject(TmClientStore);
  if (options.cold !== true) {
    await tmWarmTenant(store, TM_T17, options.docs ?? TM_T17_DOCS);
  }
  if (options.tenants !== undefined) {
    await store.put({ kind: 'user', sub: TM_TEST_SUB }, 'tenants', stored(options.tenants));
  }
  const api = TestBed.inject(TmFakeApi);
  api.respond('client/sync', TM_COLD_SYNC);
  api.respond('/bff/user', { tenants: options.tenants ?? [] });
  api.respond('/api/distribution-info', {});
  return {
    router: TestBed.inject(Router),
    urls: TestBed.inject(TmUrls),
    api,
    store,
    telemetry: TestBed.inject(TmFakeTelemetry),
  };
}

/**
 * Navigates the rig's app, rendered in a router harness, to a URL.
 *
 * @param url - The URL.
 * @returns The harness.
 */
export async function tmNavigate(url: string): Promise<RouterTestingHarness> {
  const harness = await RouterTestingHarness.create();
  await harness.navigateByUrl(url);
  return harness;
}

/** Runs the pending tasks and renders, so loads started by a navigation land. */
export async function tmSettle(): Promise<void> {
  for (let i = 0; i < 5; i++) {
    await new Promise((resolve) => setTimeout(resolve));
    TestBed.tick();
  }
}

/**
 * How long a wait on state may run before it fails. It stays below the test
 * timeout (5 s), so the wait reports its condition and stops before the
 * runner abandons the test; a wait left running would drive the next test.
 */
export const TM_WAIT_DEADLINE_MS = 4_000;

/**
 * The page's own clock, taken before a spec fakes the timers: a bare
 * `vi.useFakeTimers()` fakes `performance.now()` too, which would stop a
 * deadline measured with it.
 */
const realNow = performance.now.bind(performance);

/**
 * Runs a step at a time until a condition holds, for at most
 * {@link TM_WAIT_DEADLINE_MS} of real time. The deadline is measured with
 * the page's own `performance.now()`, taken before any spec fakes the
 * timers, so it holds under `vi.useFakeTimers()` too, whatever it fakes.
 *
 * @param condition - The state the spec waits for.
 * @param step - One turn of the wait, such as a real task and a render, or a zero advance of faked timers.
 * @param description - What the condition waits for, in the error; the condition's source when omitted.
 * @throws Error naming the condition when it has not held by the deadline; the wait stops there.
 */
export async function tmWaitUntil(condition: () => boolean, step: () => Promise<unknown>, description = String(condition)): Promise<void> {
  const started = realNow();
  while (!condition()) {
    const waited = realNow() - started;
    if (waited > TM_WAIT_DEADLINE_MS) {
      throw new Error(`The condition did not hold within ${Math.round(waited)} ms: ${description}`);
    }
    await step();
  }
}

/**
 * Runs the pending tasks and renders until a condition holds. A load through
 * a lazily loaded chunk lands only once the chunk's first fetch does, which
 * no number of turns bounds, so a spec waits on the state the load produces.
 * The wait fails after {@link TM_WAIT_DEADLINE_MS}, naming the condition.
 *
 * @param condition - What the load produces.
 * @param description - What the condition waits for, in the error; the condition's source when omitted.
 * @throws Error naming the condition when it has not held by the deadline.
 */
export async function tmSettleUntil(condition: () => boolean, description = String(condition)): Promise<void> {
  await tmSettle();
  // A task at a time, rendering after each, until the state is there.
  await tmWaitUntil(
    condition,
    async () => {
      await new Promise((resolve) => setTimeout(resolve));
      TestBed.tick();
    },
    description,
  );
}

/** The browser's own timer, taken before a spec fakes the timers. */
const realSetTimeout = globalThis.setTimeout;

/**
 * Runs work and collects what was left unhandled meanwhile: an error thrown
 * in a task or a microtask, and a rejection no code handled. Each is kept
 * from the test runner, so the spec's own assertion reports it. The page's
 * report of a rejection is a task of its own, so the collection ends a real
 * task after the work, even under faked timers.
 *
 * @param work - The work, which waits for what it started.
 * @returns The errors and the rejections' reasons, in order.
 */
export async function tmUnhandled(work: () => Promise<void>): Promise<unknown[]> {
  const caught: unknown[] = [];
  const error = (event: ErrorEvent): void => {
    caught.push(event.error);
    event.preventDefault();
  };
  const rejection = (event: PromiseRejectionEvent): void => {
    caught.push(event.reason);
    event.preventDefault();
  };
  window.addEventListener('error', error);
  window.addEventListener('unhandledrejection', rejection);
  try {
    await work();
    await new Promise((resolve) => realSetTimeout(resolve, 10));
  } finally {
    window.removeEventListener('error', error);
    window.removeEventListener('unhandledrejection', rejection);
  }
  return caught;
}

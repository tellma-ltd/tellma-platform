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
import { provideTellma } from '../app/provide-tellma';
import type { TmAppConfig } from '../app/tm-app-config';
import { tmFeature, type TmFeature } from '../declarations/tm-feature';
import { tmPage } from '../declarations/tm-page';
import { tmEntity } from '../entity/tm-entity-declaration';
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

/** The test metadata, with a stack that has no details page and a child entity. */
export function tmRouteTestMetadata(): TmClientMetadata {
  const base = tmTestMetadata();
  return tmTestMetadata({
    entities: [
      ...base.entities,
      summary('acme.Currency', 'currencies', 'stack'),
      summary('acme.SalesInvoiceLine', 'sales-invoice-lines', 'child'),
    ],
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
  /** The memberships stored for the user, if any. */
  readonly tenants?: readonly TmMembership[];
  /** What the worker's update check answers. */
  readonly update?: TmApiUpdateCheck;
  /** More providers, after the rig's. */
  readonly providers?: readonly Provider[];
}

/**
 * The test app's configuration: resolving metadata loaders over the route
 * test metadata, English strings, and the test feature.
 *
 * @returns The configuration.
 */
export function tmRouteTestConfig(): TmAppConfig {
  const metadata = tmRouteTestMetadata();
  const loaders: TmMetadataLoaders = {
    core: () => Promise.resolve(metadata),
    entities: () => Promise.resolve(tmTestEntitiesChunk()),
    contracts: () => Promise.resolve({ fingerprints: {}, long: [], idempotent: [] }),
    strings: { en: () => Promise.resolve({ Distribution_DisplayName: 'Acme', gl_Center_Plural: 'Centers', gl_Center: 'Center' }) },
  };
  return {
    metadata: loaders,
    languages: [{ code: 'en', dir: 'ltr', strings: () => Promise.resolve({ tm: { home: { title: 'Home' } }, tmUi: {} }) }],
    features: [tmRouteTestFeature()],
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
      provideTellma(tmRouteTestConfig()),
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
    await tmWarmTenant(store, TM_T17, TM_T17_DOCS);
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

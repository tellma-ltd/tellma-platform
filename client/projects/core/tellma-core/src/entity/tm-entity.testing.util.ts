// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { provideLocationMocks } from '@angular/common/testing';
import { ApplicationInitStatus, type Provider } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import type { RouterTestingHarness } from '@angular/router/testing';
import {
  provideTellmaStateTesting,
  provideTellmaTesting,
  TmFakeApi,
  TmFakeTelemetry,
  tmTestEntities,
  tmTestMetadata,
  tmWarmTenant,
} from '@tellma/core-testing';

import type { TmSecurableMetadata } from '../admin/tm-admin-metadata';
import { TM_API_UPDATES } from '../api/tm-api-seams';
import { provideTellma } from '../app/provide-tellma';
import type { TmCalendarDefinition, TmLanguageDefinition, TmStringTree } from '../app/tm-app-config';
import { TM_DEV_MODE } from '../app/tm-dev-mode';
import { tmFeature } from '../declarations/tm-feature';
import { tmEnglish } from '../l10n/tm-english';
import { TmLocalization } from '../l10n/tm-localization';
import type { TmEntitiesChunk } from '../metadata/tm-client-metadata';
import { TM_T17, tmLoadTestPage, tmNavigate, tmRouteTestConfig, tmSettle } from '../routes/tm-app.testing.util';
import type { TmAccessSummary, TmLaunchDocs, TmMeResult, TmTenantFacts, TmTenantSettingsForClient } from '../state/launch/tm-launch-docs';
import { TmClientStore } from '../state/store/tm-client-store';
import { TmDisplayNames } from './tm-display-names';
import { TmEntities } from './tm-entities';
import { tmEntity, type TmEntityActionDeclaration, type TmPickerDeclaration } from './tm-entity-declaration';
import { TmPermissions } from './tm-permissions';

// The entity registry's test app: the server's recorded entity model (the
// fixture @tellma/core-testing ships), a feature declaring three of its
// entities, the securables the permission labels read, English and Arabic
// strings for the labels under test, and tenant 17 with one to three
// content languages, routed and warm.

/** The tenant's content languages, in slot order: English, Arabic, French. */
const CONTENT_LANGUAGES: TmTenantSettingsForClient['languages'] = [
  { code: 'en', nativeName: 'English', symbol: 'En', isRightToLeft: false },
  { code: 'ar', nativeName: 'العربية', symbol: 'ع', isRightToLeft: true },
  { code: 'fr', nativeName: 'Français', symbol: 'Fr', isRightToLeft: false },
];

/**
 * A securable of the test distribution.
 *
 * @param resource - The resource.
 * @param action - The action.
 * @param labels - The resource's and the action's label keys, and their fallbacks.
 * @param sensitive - Whether the securable is sensitive.
 * @returns The securable.
 */
function securable(
  resource: string,
  action: string,
  labels: Pick<TmSecurableMetadata, 'resourceLabelKey' | 'actionLabelKey' | 'actionLabelFallback'>,
  sensitive = false,
): TmSecurableMetadata {
  return { resource, action, isSensitive: sensitive, feature: resource.split('.')[0]!, featureLabelKey: 'Feature', ...labels };
}

/** The securables section: the Access tab's names of the pairs the tests label. */
const SECURABLES: readonly TmSecurableMetadata[] = [
  securable('acme.Customer', 'Read', { resourceLabelKey: 'Acme_Customer_Plural', actionLabelKey: 'Action_Read' }),
  securable('acme.Customer', 'Save', { resourceLabelKey: 'Acme_Customer_Plural', actionLabelKey: 'Action_Save' }),
  securable('acme.Customer', 'Delete', { resourceLabelKey: 'Acme_Customer_Plural', actionLabelKey: 'Action_Delete' }),
  securable(
    'acme.Customer',
    'SetCreditLimit',
    { resourceLabelKey: 'Acme_Customer_Plural', actionLabelKey: 'Acme_Customer_SetCreditLimit', actionLabelFallback: 'Set credit limit' },
    true,
  ),
  securable('core.Settings.General', 'Read', { resourceLabelKey: 'SettingCategory_general', actionLabelKey: 'Action_Read' }),
];

/** The English labels under test. */
const ENGLISH: Readonly<Record<string, string>> = {
  Distribution_DisplayName: 'Acme',
  Acme_Customer: 'Customer',
  Acme_Customer_Plural: 'Customers',
  Acme_Customer_New: 'New customer',
  Acme_Customer_Currency: 'Currency',
  Acme_Customer_Image: 'Image',
  Acme_Currency: 'Currency',
  Action_Read: 'Read',
  Action_Save: 'Create and edit',
  Action_Delete: 'Delete',
  Code: 'Code',
  Name: 'Name',
  Parent: 'Parent',
  Gl_CustomerType_Retail: 'Retail',
  SettingCategory_general: 'General',
  Securable_Settings: 'Settings: {category}',
  Entity_DisplayNameFallback: '#{id}',
};

/** The Arabic labels under test. */
const ARABIC_STRINGS: Readonly<Record<string, string>> = {
  Distribution_DisplayName: 'أكمي',
  Acme_Customer_Plural: 'العملاء',
  Name: 'الاسم',
  Entity_DisplayNameFallback: '#{id}',
};

/**
 * A language of the app, with the platform's own strings a test gives it:
 * English answers for every other.
 *
 * @param code - The language.
 * @param dir - Its direction.
 * @param tm - Its platform strings; none when omitted.
 * @returns The language's definition.
 */
function language(code: string, dir: 'ltr' | 'rtl', tm: TmStringTree = {}): TmLanguageDefinition {
  return { code, dir, strings: () => Promise.resolve({ tm, tmUi: {} }) };
}

/** How a test sets the entity registry's app up. */
export interface TmEntityRigOptions {
  /** The tenant's number of content languages; one when omitted. */
  readonly languages?: 1 | 2 | 3;
  /** The user's language; English when omitted. */
  readonly language?: 'en' | 'ar' | 'am';
  /** The server's labels beside the ones under test, by language, such as a patched chunk's enum labels; none when omitted. */
  readonly labels?: Partial<Readonly<Record<'en' | 'ar' | 'am', Readonly<Record<string, string>>>>>;
  /** The platform's own strings in Arabic and Amharic, by language; none when omitted, so English answers. */
  readonly tmStrings?: Partial<Readonly<Record<'ar' | 'am', TmStringTree>>>;
  /** The user's access summary, beside a non-system user with no grants. */
  readonly access?: Partial<TmAccessSummary>;
  /** The tenant's state; `Active` when omitted. */
  readonly state?: TmTenantFacts['state'];
  /** The Entities chunk, the recorded one when omitted. */
  readonly chunk?: TmEntitiesChunk;
  /** Whether the app runs in development; it does when omitted. */
  readonly devMode?: boolean;
  /** Whether the rig loads the Entities chunk before it returns; it does when omitted. */
  readonly load?: boolean;
  /** Providers beside the app's, such as a test's own seam; none when omitted. */
  readonly providers?: readonly Provider[];
  /** The picker options each declared entity declares; none when omitted. */
  readonly pickers?: Partial<Readonly<Record<'acme.Customer' | 'gl.Center' | 'acme.Currency', TmPickerDeclaration<string>>>>;
  /** The actions `acme.Customer` declares; `set-credit-limit` as a button when omitted. */
  readonly actions?: readonly TmEntityActionDeclaration<'acme.Customer'>[];
  /** The tenant's calendars, the first its default, such as `['uq', 'gc']`; `['gc']` when omitted. */
  readonly calendars?: readonly string[];
  /** The app's own calendars beside the platform's, such as a test calendar; none when omitted. */
  readonly calendarDefinitions?: readonly TmCalendarDefinition[];
}

/** What an entity registry spec drives and inspects. */
export interface TmEntityRig {
  /** The entity registry. */
  readonly entities: TmEntities;
  /** The display names. */
  readonly names: TmDisplayNames;
  /** The permissions. */
  readonly permissions: TmPermissions;
  /** The fake API. */
  readonly api: TmFakeApi;
  /** The fake telemetry. */
  readonly telemetry: TmFakeTelemetry;
  /** The client store. */
  readonly store: TmClientStore;
  /** Tenant 17's launch documents as stored. */
  readonly docs: TmLaunchDocs;
  /** The router harness the app is rendered in, routed at `/17`; a test navigates it further, since a test makes one harness. */
  readonly router: RouterTestingHarness;
  /**
   * Stores new launch documents for tenant 17, as a sync does, and settles.
   *
   * @param docs - The documents.
   * @param entityTags - Each cacheable list's current wire tag, by entity name.
   */
  move(docs: TmLaunchDocs, entityTags?: Readonly<Record<string, string>>): Promise<void>;
}

/**
 * Tenant 17's launch documents under the options.
 *
 * @param options - The rig's options.
 * @returns The documents.
 */
export function tmEntityDocs(options: TmEntityRigOptions = {}): TmLaunchDocs {
  const me: TmMeResult = {
    user: {
      id: 42,
      kind: 'Individual',
      state: 'Joined',
      name: 'Farah',
      modifiedAt: '2026-11-01T00:00:00.0000000+00:00',
      ...(options.language !== undefined && options.language !== 'en' && { preferredLanguage: options.language }),
    },
    preferencesTag: 'P1',
    preferences: {},
    access: { tag: 'A1', formatVersion: 1, isSystem: false, securables: [], problems: [], ...options.access },
    tags: { settings: 'S1', preferences: 'P1', permissions: 'A1', entities: 'E1' },
  };
  const settings: TmTenantSettingsForClient = {
    tenantId: 17,
    names: ['Acme Trading', 'أكمي للتجارة', null],
    languages: CONTENT_LANGUAGES.slice(0, options.languages ?? 1),
    calendars: options.calendars ?? ['gc'],
    timeZone: 'Asia/Riyadh',
    entries: {},
  };
  return { settings, me, tenant: { id: 17, category: 'Live', state: options.state ?? 'Active', realtime: true } };
}

/**
 * Starts the entity registry's test app, routed into a warm tenant 17,
 * with the Entities chunk loaded unless the options say otherwise.
 *
 * @param options - The rig's options.
 * @returns The rig.
 */
export async function tmEntityRig(options: TmEntityRigOptions = {}): Promise<TmEntityRig> {
  const { summaries, chunk } = tmTestEntities();
  const base = tmRouteTestConfig();
  const pickers = options.pickers ?? {};
  TestBed.configureTestingModule({
    providers: [
      provideTellma({
        metadata: {
          ...base.metadata,
          core: () => Promise.resolve(tmTestMetadata({ entities: summaries, securables: SECURABLES })),
          entities: () => Promise.resolve(options.chunk ?? chunk),
          strings: {
            en: () => Promise.resolve({ ...ENGLISH, ...options.labels?.en }),
            ar: () => Promise.resolve({ ...ARABIC_STRINGS, ...options.labels?.ar }),
            am: () => Promise.resolve({ ...options.labels?.am }),
          },
        },
        languages: [tmEnglish(), language('ar', 'rtl', options.tmStrings?.ar), language('am', 'ltr', options.tmStrings?.am)],
        ...(options.calendarDefinitions && { calendars: options.calendarDefinitions }),
        features: [
          tmFeature({
            key: 'acme',
            entities: [
              tmEntity('acme.Customer', {
                details: tmLoadTestPage,
                actions: options.actions ?? [{ name: 'set-credit-limit', button: true }],
                ...(pickers['acme.Customer'] && { picker: pickers['acme.Customer'] as never }),
              }),
              tmEntity('gl.Center', { details: { page: tmLoadTestPage }, ...(pickers['gl.Center'] && { picker: pickers['gl.Center'] as never }) }),
              tmEntity('acme.Currency', { ...(pickers['acme.Currency'] && { picker: pickers['acme.Currency'] as never }) }),
            ],
          }),
        ],
      }),
      provideTellmaTesting(),
      provideTellmaStateTesting(),
      provideLocationMocks(),
      { provide: TM_API_UPDATES, useValue: { check: () => Promise.resolve('none') } },
      { provide: TM_DEV_MODE, useValue: () => options.devMode !== false },
      ...(options.providers ?? []),
    ],
  });
  await TestBed.inject(ApplicationInitStatus).donePromise;

  // --- Tenant 17, warm and routed ---
  const store = TestBed.inject(TmClientStore);
  const docs = tmEntityDocs(options);
  await tmWarmTenant(store, TM_T17, docs);
  const api = TestBed.inject(TmFakeApi);
  api.respond('client/sync', { tenant: docs.tenant, tags: docs.me.tags, settings: docs.settings, me: docs.me, staleEntities: [] });
  api.respond('/bff/user', { tenants: [] });
  api.respond('/api/distribution-info', {});
  const router = await tmNavigate('/17');
  await TestBed.inject(TmLocalization).ɵready();
  const entities = TestBed.inject(TmEntities);
  if (options.load !== false) {
    await entities.load();
  }
  await tmSettle();
  return {
    entities,
    names: TestBed.inject(TmDisplayNames),
    permissions: TestBed.inject(TmPermissions),
    api,
    telemetry: TestBed.inject(TmFakeTelemetry),
    store,
    docs,
    router,
    move: async (moved, entityTags) => {
      await tmWarmTenant(store, TM_T17, moved, { ...(entityTags && { entityTags }) });
      await tmSettle();
    },
  };
}

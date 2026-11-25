// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import type {
  TmClientMetadata,
  TmEntitiesChunk,
  TmEntityMetadata,
  TmEntityOperation,
  TmEntitySummary,
  TmEnumMetadata,
  TmNavigationMetadata,
  TmPropertyMetadata,
} from '@tellma/core';

import recordedChunk from '../fixtures/entity-model/entities-chunk.json';
import recordedSummaries from '../fixtures/entity-model/core-entities.json';

// The test client metadata: a small, coherent Acme distribution with the
// `core`, `gl` and `acme` features, two languages, the platform's entities,
// a customer and a center, one coded report and one widget type. Its Core
// and Entities chunks agree with each other, so a test patches only the
// facts it is about.
//
// The platform's entities and enums (`core.*`) are never hand-written: they
// are taken from the entity model the server's suite records, which is what
// the server emits for them, so a spec cannot lean on a `core` field the
// product never sends. The customer and the center are the test
// distribution's own entities, written here.

/** The operations of a stack every operation reaches. */
const STACK_OPERATIONS: readonly TmEntityOperation[] = ['query', 'details', 'save', 'delete', 'import', 'export'];

/** The facts of one test entity, from which its summary and metadata are both built. */
interface TestEntity {
  /** The entity's summary, as the Core chunk carries it. */
  readonly summary: TmEntitySummary;
  /** Its properties beside `Id`. */
  readonly properties: readonly TmPropertyMetadata[];
  /** Its navigations. */
  readonly navigations: readonly TmNavigationMetadata[];
  /** The names of its actions. */
  readonly actions: readonly string[];
}

/**
 * Whether a name is the platform's own: an entity or enum of the `core` feature.
 *
 * @param item - The entity, summary or enum.
 * @returns Whether its name starts with `core.`.
 */
function isPlatform(item: { readonly name: string }): boolean {
  return item.name.startsWith('core.');
}

/** The platform's entity summaries, as the server emits them into the Core chunk. */
const PLATFORM_SUMMARIES: readonly TmEntitySummary[] = (recordedSummaries as unknown as readonly TmEntitySummary[]).filter(isPlatform);

/** The platform's entities, as the server emits them into the Entities chunk. */
const PLATFORM_ENTITIES: readonly TmEntityMetadata[] = (recordedChunk as unknown as TmEntitiesChunk).entities.filter(isPlatform);

/** The platform's named enums, which its entities' properties and action fields name. */
const PLATFORM_ENUMS: readonly TmEnumMetadata[] = (recordedChunk as unknown as TmEntitiesChunk).enums.filter(isPlatform);

/**
 * Orders items by name, as the server orders both chunks' entities.
 *
 * @param items - The items.
 * @returns A new array, ordered.
 */
function byName<T extends { readonly name: string }>(items: readonly T[]): T[] {
  return [...items].sort((a, b) => (a.name < b.name ? -1 : a.name > b.name ? 1 : 0));
}

/**
 * A test property.
 *
 * @param entity - The owning entity's name, for its label key.
 * @param name - The property's name.
 * @param type - Its type.
 * @returns The property.
 */
function property(entity: string, name: string, type: TmPropertyMetadata['type'] = 'string'): TmPropertyMetadata {
  return {
    name,
    json: name.charAt(0).toLowerCase() + name.slice(1),
    type,
    queryable: true,
    nullable: name !== 'Id',
    labelKey: `${entity.replace('.', '_')}_${name}`,
    excel: true,
    ownership: name === 'Id' ? 'database-owned' : 'editable',
  };
}

/**
 * A test navigation, with its foreign key property.
 *
 * @param entity - The owning entity's name.
 * @param name - The navigation's name.
 * @param target - The entity it reaches.
 * @returns The navigation.
 */
function navigation(entity: string, name: string, target: string): TmNavigationMetadata {
  return {
    name,
    json: name.charAt(0).toLowerCase() + name.slice(1),
    foreignKey: `${name}Id`,
    target,
    kind: 'reference',
    nullable: true,
    labelKey: `${entity.replace('.', '_')}_${name}`,
  };
}

/**
 * A test entity summary: a stack every operation reaches.
 *
 * @param name - The entity's name.
 * @param segment - Its route segment.
 * @returns The summary.
 */
function summary(name: string, segment: string): TmEntitySummary {
  const label = name.replace('.', '_');
  return {
    name,
    segment,
    labelKey: label,
    pluralLabelKey: `${label}_Plural`,
    newLabelKey: `${label}_New`,
    operations: STACK_OPERATIONS,
    capabilities: [],
    displayName: { source: 'Name', multilingual: false },
    kind: 'stack',
  };
}

/**
 * The test distribution's own entities, ordered by name; the platform's join them from the recording.
 * `gl.Center` follows the server's test model, whose center has a `Manager`, and not the real
 * general ledger module's center, which has none.
 */
const ENTITIES: readonly TestEntity[] = [
  {
    summary: summary('acme.Customer', 'customers'),
    properties: [property('acme.Customer', 'Name'), property('acme.Customer', 'Code'), property('acme.Customer', 'CreditLimit', 'decimal'), property('acme.Customer', 'CenterId', 'integer')],
    navigations: [navigation('acme.Customer', 'Center', 'gl.Center')],
    actions: ['set-credit-limit'],
  },
  {
    summary: summary('gl.Center', 'centers'),
    properties: [property('gl.Center', 'Name'), property('gl.Center', 'Code'), property('gl.Center', 'ManagerId', 'integer')],
    navigations: [navigation('gl.Center', 'Manager', 'core.User')],
    actions: [],
  },
];

/**
 * One test distribution entity's metadata, built from its facts.
 *
 * @param entity - The entity's facts.
 * @returns Its metadata, as the Entities chunk carries it.
 */
function metadata(entity: TestEntity): TmEntityMetadata {
  return {
    ...entity.summary,
    key: { property: 'Id', type: 'int' },
    properties: [property(entity.summary.name, 'Id', 'integer'), ...entity.properties],
    navigations: entity.navigations,
    children: [],
    actions: entity.actions.map((name) => ({
      name,
      kind: 'entity',
      builtIn: false,
      action: 'Update',
      labelKey: `${entity.summary.name.replace('.', '_')}_Action_${name}`,
      mutation: true,
      idempotent: false,
      destructive: false,
      singleTarget: true,
      supportsFilter: false,
      result: 'entities',
    })),
    relatedSelect: [],
    defaultOrderBy: 'Id desc',
    searchable: ['Name'],
    prefixSearchable: [],
    naturalKeys: [],
    extras: [],
    limits: {
      maxTake: 500,
      countCap: 10000,
      maxSkipWindow: 10000,
      maxSearchLength: 200,
      maxIds: 1000,
      maxExtras: 20,
      maxFilterLength: 4000,
    },
  };
}

/**
 * The test Core chunk: the Acme distribution, with every section filled.
 * Its `core.*` entity summaries are the platform's own, as the server emits
 * them; its other entities belong to the test distribution. The top-level
 * members `patch` sets replace the fixture's whole.
 *
 * @param patch - The members to replace.
 * @returns The chunk.
 *
 * @example
 * ```ts
 * const metadata = tmTestMetadata({ calendars: ['gc', 'uq'] });
 * ```
 */
export function tmTestMetadata(patch: Partial<TmClientMetadata> = {}): TmClientMetadata {
  return {
    formatVersion: 1,
    distribution: {
      slug: 'acme',
      displayName: 'Acme',
      copyright: null,
      version: '1.0.0+0123abc',
      platformVersion: '1.0.0',
    },
    features: [
      { key: 'core', version: '1.0.0' },
      { key: 'gl', version: '1.0.0' },
      { key: 'acme', version: '1.0.0' },
    ],
    languages: [
      { code: 'en', nativeName: 'English', dir: 'ltr', region: 'US', regions: ['US', 'GB'], nativeDigits: null },
      { code: 'ar', nativeName: 'العربية', dir: 'rtl', region: 'SA', regions: ['SA', 'AE'], nativeDigits: 'arab' },
    ],
    calendars: ['gc'],
    // A fresh copy each call, so a test may patch any depth of it.
    entities: structuredClone(byName([...PLATFORM_SUMMARIES, ...ENTITIES.map((entity) => entity.summary)])),
    securables: [
      {
        resource: 'acme.Customer',
        action: 'Read',
        filterRoot: 'acme.Customer',
        isSensitive: false,
        feature: 'acme',
        resourceLabelKey: 'acme_Customer_Plural',
        actionLabelKey: 'Action_Read',
        featureLabelKey: 'Feature_acme',
      },
    ],
    settingKeys: [
      {
        name: 'core.general.shortCompanyName',
        category: 'general',
        visibility: 'Client',
        valueType: 'String',
        isNullable: true,
        isArray: false,
        defaultJson: null,
        labelKey: 'Setting_ShortCompanyName',
      },
    ],
    settingCategories: [{ name: 'general', resource: 'core.Settings', order: 10, labelKey: 'Settings_General' }],
    notificationTypes: [
      { key: 'core.assignment', category: 'assignments', mutable: true, severity: 'Info', toast: true, inbox: true },
    ],
    notificationChannels: [{ key: 'core.inbox', defaultEnabled: true }],
    clientEvents: ['core.notification'],
    jobHandlers: [{ key: 'core.notification-delivery', schedulable: false, labelKey: 'Job_NotificationDelivery' }],
    widgetTypes: ['acme.cash-position'],
    codedReports: [
      {
        key: 'acme-credit-exposure',
        feature: 'acme',
        route: 'GET /{tenantId}/api/acme/reports/credit-exposure',
        securable: ['acme.Customer', 'Read'],
        kind: 'Summary',
        titleKey: 'Acme_Report_CreditExposure',
        export: 'Platform',
        parameters: [{ key: 'AsOf', type: 'Date', required: true }],
      },
    ],
    routing: {
      serverFirstSegments: ['api', 'bff', 'health', 'id', 'mcp', 'openapi', 'signin-oidc', 'signout-callback-oidc', '.well-known'],
      serverTenantSegments: ['api', 'hub', 'blobs', 'mcp'],
      spaFirstSegments: ['tenants', 'signed-out'],
      spaTenantSegments: ['settings', 'me', 'list'],
    },
    ...patch,
  };
}

/**
 * The test Entities chunk, agreeing with {@link tmTestMetadata}'s entity
 * summaries. Its `core.*` entities and enums are the platform's own, as the
 * server emits them. The top-level members `patch` sets replace the
 * fixture's whole.
 *
 * @param patch - The members to replace.
 * @returns The chunk.
 *
 * @example
 * ```ts
 * const entities = tmTestEntitiesChunk();
 * const customer = entities.entities.find((entity) => entity.name === 'acme.Customer');
 * ```
 */
export function tmTestEntitiesChunk(patch: Partial<TmEntitiesChunk> = {}): TmEntitiesChunk {
  return {
    // A fresh copy each call, so a test may patch any depth of it.
    entities: structuredClone(byName([...PLATFORM_ENTITIES, ...ENTITIES.map(metadata)])),
    enums: structuredClone(PLATFORM_ENUMS),
    ...patch,
  };
}

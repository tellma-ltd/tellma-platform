// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { TestBed } from '@angular/core/testing';
import { Router } from '@angular/router';
import { type TmFakeApi, tmWarmTenant } from '@tellma/core-testing';

import type { TmClientMetadata } from '../metadata/tm-client-metadata';
import type { TmLanguageDefinition, TmStringTree } from '../app/tm-app-config';
import type { TmEntitySummary } from '../entity/tm-entity-metadata';
import type { TmEntityType } from '../entity/tm-entity-types';
import type { TmDecimal } from '../json/tm-json';
import { tmEnglish } from '../l10n/tm-english';
import { tmAppRig, type TmAppRig, tmNavigate, tmRouteTestMetadata, tmSettle, TM_T17, TM_T17_DOCS } from '../routes/tm-app.testing.util';
import type { TmLaunchDocs } from '../state/launch/tm-launch-docs';
import type { TmNotificationTypeMetadata } from './tm-notification-metadata';

// The attention specs' app: tenant 17 entered in a whole test app, so the
// model, the arrivals and the text read the routed tenant's store, the
// client metadata and the strings as the app does. The tenant speaks
// English, Arabic and French, so each of its three content slots is a
// language a test can switch to.

/** The notification types the specs toast or not. */
export const TM_TEST_NOTIFICATION_TYPES: readonly TmNotificationTypeMetadata[] = [
  { key: 'core.export.ready', category: 'jobs', mutable: false, severity: 'Success', toast: true, inbox: true, targetResource: 'core.Export' },
  { key: 'core.job.failed', category: 'jobs', mutable: false, severity: 'Error', toast: true, inbox: true },
  { key: 'gl.center.shared', category: 'sharing', mutable: true, severity: 'Info', toast: false, inbox: true },
  { key: 'gl.center.flagged', category: 'centers', mutable: true, severity: 'Warning', toast: true, inbox: true, targetResource: 'gl.Center' },
];

/** The notifications' summary, so their search page has a route. */
const NOTIFICATION: TmEntitySummary = {
  name: 'core.Notification',
  segment: 'notifications',
  labelKey: 'core_Notification',
  pluralLabelKey: 'core_Notification_Plural',
  newLabelKey: 'core_Notification_New',
  labelFallback: 'core.Notification',
  operations: ['query', 'details'],
  capabilities: [],
  displayName: { source: 'Title', multilingual: false },
  kind: 'stack',
};

/** The server strings the specs render, by language. */
const STRINGS: Readonly<Record<string, Readonly<Record<string, string>>>> = {
  en: {
    Notification_core_export_ready: 'Export is ready',
    Notification_core_export_ready_Detail: '{fileName} · {rowCount, number} rows',
    Notification_core_job_failed: 'A job failed',
    Notification_gl_center_flagged: '{center} was flagged',
    Notification_gl_center_flagged_Detail: 'Urgent: {urgent, select, true {yes} other {no}}; tags {tags}',
    Excel_Export_Label: 'Exporting {entity}',
    Excel_Export_Progress: '{done, number} of {total, number} rows',
    JobHandler_core_import: 'Importing',
    Assignment_gl_Center_Detail: 'Managed by {manager}',
  },
  ar: {
    Notification_core_export_ready: 'التصدير جاهز',
    gl_Center: 'مركز',
    gl_Center_Plural: 'المراكز',
    Excel_Export_Label: 'جارٍ تصدير {entity}',
    Assignment_gl_Center_Detail: 'يديره {manager}',
  },
  fr: {},
};

/** A language the specs switch to, with the platform strings they read in it. */
function language(code: string, dir: 'ltr' | 'rtl', tm: TmStringTree = {}): TmLanguageDefinition {
  return { code, dir, strings: () => Promise.resolve({ tm, tmUi: {} }) };
}

/** Arabic's attention strings the specs read: the caption's title puts the code first. */
const ARABIC_TM: TmStringTree = { attention: { assignment: { title: '{code} · {entity}' } } };

/** What an attention spec drives. */
export interface TmAttentionRig extends TmAppRig {
  /** The fake API. */
  readonly api: TmFakeApi;
  /** Stores a copy of one of tenant 17's documents, saved at the given time (now by default). */
  put(name: 'inbox' | 'assignments' | 'tenant', data: unknown, savedAt?: number): Promise<void>;
}

/**
 * Starts the test app in tenant 17 and returns the rig.
 *
 * @param docs - Tenant 17's launch documents, merged over the defaults.
 * @param metadata - The Core chunk's members, merged over the route test metadata's.
 * @returns The rig.
 */
export async function tmAttentionRig(docs: Partial<TmLaunchDocs> = {}, metadata: Partial<TmClientMetadata> = {}): Promise<TmAttentionRig> {
  const { settings, me } = TM_T17_DOCS;
  const routed = tmRouteTestMetadata();
  const rig = await tmAppRig({
    docs: {
      ...TM_T17_DOCS,
      settings: {
        ...settings,
        names: ['Acme', 'أكمي', null],
        languages: [
          ...settings.languages,
          { code: 'ar', nativeName: 'العربية', symbol: 'ع', isRightToLeft: true },
          { code: 'fr', nativeName: 'Français', symbol: 'Fr', isRightToLeft: false },
        ],
      },
      me,
      ...docs,
    },
    metadata: {
      ...routed,
      // The notifications, whose search page the toasts' overflow pill opens.
      entities: [...routed.entities, NOTIFICATION],
      notificationTypes: TM_TEST_NOTIFICATION_TYPES,
      jobHandlers: [
        { key: 'core.export', schedulable: false, labelKey: 'JobHandler_core_export', labelFallback: 'core.export' },
        { key: 'core.import', schedulable: false, labelKey: 'JobHandler_core_import' },
      ],
      ...metadata,
    },
    languages: [tmEnglish(), language('ar', 'rtl', ARABIC_TM), language('fr', 'ltr')],
    strings: STRINGS,
  });
  await tmNavigate('/17/centers');
  await tmSettle();
  return {
    ...rig,
    put: async (name, data, savedAt = Date.now()) => {
      await rig.store.put(TM_T17, name, { tag: null, savedAt, data });
      TestBed.tick();
    },
  };
}

/** A notification row. */
export function tmTestNotification(id: number, members: Readonly<Record<string, unknown>> = {}): TmEntityType<'core.Notification'> {
  return {
    id,
    type: 'core.export.ready',
    severity: 'Success',
    argumentsJson: null,
    createdAt: '2026-11-23T08:00:00.000Z',
    ...members,
  } as TmEntityType<'core.Notification'>;
}

/** An assignment row of a center. */
export function tmTestAssignment(id: number, members: Readonly<Record<string, unknown>> = {}): TmEntityType<'core.Assignment'> {
  return {
    id,
    resource: 'gl.Center',
    targetId: '23',
    state: 'Open',
    assigneeId: 42,
    assignedById: 7,
    assignedAt: '2026-11-23T08:00:00.000Z',
    isReturn: false,
    ...members,
  } as TmEntityType<'core.Assignment'>;
}

/** An inbox version. */
export function tmVersion(value: number): TmDecimal {
  return String(value) as TmDecimal;
}

/**
 * Stores another tenant's launch documents, tenant 17's under its id, with
 * the attention documents given, so the app enters it warm.
 *
 * @param rig - The rig.
 * @param tenantId - The tenant.
 * @param docs - The tenant's own documents, over tenant 17's.
 * @param at - When the documents were saved; now by default.
 * @returns The tenant's partition.
 */
export async function tmWarmTestTenant(
  rig: TmAppRig,
  tenantId: number,
  docs: Partial<TmLaunchDocs> = {},
  at = Date.now(),
): Promise<typeof TM_T17> {
  const partition = { ...TM_T17, tenantId };
  const { settings, me, tenant } = TM_T17_DOCS;
  await tmWarmTenant(rig.store, partition, { settings: { ...settings, tenantId }, me, tenant: { ...tenant, id: tenantId }, ...docs }, { at });
  return partition;
}

/**
 * Enters a tenant's centers page and lets the entry settle.
 *
 * @param tenantId - The tenant.
 */
export async function tmEnterTestTenant(tenantId: number): Promise<void> {
  await TestBed.inject(Router).navigateByUrl(`/${String(tenantId)}/centers`);
  await tmSettle();
}

export { TM_T17, tmSettle };

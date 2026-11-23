// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { TestBed } from '@angular/core/testing';
import {
  provideTellmaStateTesting,
  provideTellmaTesting,
  tmFakeAssignmentsSummary,
  tmFakeInboxSummary,
  tmTestProfile,
  tmWarmTenant,
} from '@tellma/core-testing';

import type { TmDecimal } from '../json/tm-json';
import { TM_T17_DOCS } from '../routes/tm-app.testing.util';
import { TM_LEADER_DOCUMENTS } from '../state/leader/tm-leader-documents';
import { TmClientStore } from '../state/store/tm-client-store';
import type { TmTenantPartition } from '../state/store/tm-store-layout';
import { TM_ATTENTION_DOCUMENTS, type TmAssignmentsSummary, type TmInboxRunningJob } from './tm-attention-documents';

// The attention documents as leader documents: the inbox's route and events,
// its poll while a job runs, and its round trip through the store.

/** The inbox's registration. */
const inbox = TM_ATTENTION_DOCUMENTS.find((document) => document.name === 'inbox')!;

/** A running job of the given status. */
function job(id: number, status: TmInboxRunningJob['status']): TmInboxRunningJob {
  return { id, handlerKey: 'core.export', status, createdAt: '2026-11-23T08:00:00.000Z' };
}

describe('TM_ATTENTION_DOCUMENTS', () => {
  it('registers the inbox alone, with its route, events and format, until the server serves the assignments', () => {
    expect(TM_ATTENTION_DOCUMENTS.map(({ name, route, events, formatVersion }) => ({ name, route, events, formatVersion }))).toEqual([
      { name: 'inbox', route: 'inbox/summary', events: ['inbox.changed', 'job.changed'], formatVersion: 1 },
    ]);
  });

  it('polls the inbox while a job is pending or running, not while every job waits on a person', () => {
    expect(inbox.pollWhile?.(tmFakeInboxSummary())).toBe(false);
    expect(inbox.pollWhile?.(tmFakeInboxSummary({ running: [job(1, 'Held'), job(2, 'PendingConfirmation')] }))).toBe(false);
    expect(inbox.pollWhile?.(tmFakeInboxSummary({ running: [job(1, 'Held'), job(2, 'Pending')] }))).toBe(true);
    expect(inbox.pollWhile?.(tmFakeInboxSummary({ running: [job(3, 'Running')] }))).toBe(true);
  });

  it('round-trips through the client store under its format, beside a warm tenant', async () => {
    // Registered as provideTellma registers them, which gives the store their formats.
    const documents = TM_ATTENTION_DOCUMENTS.map((document) => ({ provide: TM_LEADER_DOCUMENTS, multi: true, useValue: document }));
    TestBed.configureTestingModule({ providers: [provideTellmaTesting(), provideTellmaStateTesting(), ...documents] });
    const store = TestBed.inject(TmClientStore);
    const partition: TmTenantPartition = { kind: 'tenant', sub: tmTestProfile().sub, tenantId: 17 };
    const summary = tmFakeInboxSummary({ unseen: 3, maxUnseenSeverity: 'Warning', version: '9007199254740993' as TmDecimal });
    await tmWarmTenant(store, partition, { ...TM_T17_DOCS, inbox: summary });
    expect(store.doc(partition, 'inbox')()?.data).toEqual(summary);
  });

  it('keeps the assignments as a plain store document, absent until written, with no leader registration', async () => {
    TestBed.configureTestingModule({ providers: [provideTellmaTesting(), provideTellmaStateTesting()] });
    const store = TestBed.inject(TmClientStore);
    const partition: TmTenantPartition = { kind: 'tenant', sub: tmTestProfile().sub, tenantId: 17 };
    expect(store.doc(partition, 'assignments')()).toBeUndefined();
    const summary = tmFakeAssignmentsSummary({ open: 2, new: 1 });
    await tmWarmTenant(store, partition, { ...TM_T17_DOCS, assignments: summary });
    expect(store.doc(partition, 'assignments')()?.data).toEqual(summary);
    await store.patch<TmAssignmentsSummary>(partition, 'assignments', (current) => current && { ...current, open: 3 });
    expect(store.doc(partition, 'assignments')()?.data).toMatchObject({ open: 3, new: 1 });
  });
});

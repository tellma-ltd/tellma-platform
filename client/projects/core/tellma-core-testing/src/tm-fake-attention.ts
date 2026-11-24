// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import type { TmAssignmentsSummary, TmDecimal, TmInboxSummary } from '@tellma/core';

// The attention documents a test stores or answers with: an empty inbox
// and an empty tray unless the test says otherwise, so each test names only
// what it is about.

/**
 * An inbox summary: nothing unseen, unread or running at version `0`,
 * with the members the test gives.
 *
 * @param members - The members that differ from the empty inbox.
 * @returns The summary.
 *
 * @example
 * ```ts
 * api.respond('inbox/summary', tmFakeInboxSummary({ unseen: 3, maxUnseenSeverity: 'Warning', version: '42' as TmDecimal }));
 * ```
 */
export function tmFakeInboxSummary(members: Partial<TmInboxSummary> = {}): TmInboxSummary {
  return { unseen: 0, unread: 0, latest: [], running: [], related: {}, version: '0' as TmDecimal, ...members };
}

/**
 * An assignments summary: nothing open or new, either way, with the members
 * the test gives.
 *
 * @param members - The members that differ from the empty tray.
 * @returns The summary.
 *
 * @example
 * ```ts
 * await tmWarmTenant(store, partition, { ...docs, assignments: tmFakeAssignmentsSummary({ open: 2, new: 1 }) });
 * ```
 */
export function tmFakeAssignmentsSummary(members: Partial<TmAssignmentsSummary> = {}): TmAssignmentsSummary {
  return { open: 0, new: 0, openByMe: 0, latest: [], latestByMe: [], related: {}, ...members };
}

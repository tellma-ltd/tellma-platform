// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { inject, untracked } from '@angular/core';
import {
  TM_NOW,
  TmApi,
  TmApiError,
  TmClientStore,
  type TmEntityType,
  type TmInboxSummary,
  type TmInboxVerbResult,
  type TmJson,
  TmLocalization,
  type TmPartition,
  TmStoreRefusedError,
  TmTelemetry,
  ɵTM_UNIQUE_LEADER_DOCUMENTS,
} from '@tellma/core';
import { TmToasts } from '@tellma/core-ui/toast';

import type { TmInboxOverlay, TmPanel, TmSeenRelease } from './tm-attention';

// The attention model's server calls (spec 0028 §5.2–§5.4): the verbs, the
// seen and the panel's read, with their telemetry and the failure toast,
// and how the verbs' overlays change a copy, which the model applies in the
// same frame as a verb once this chunk is in. The model loads this file
// with import() at its first call, which opening a panel makes: nothing on
// the first frame talks to the server about attention or overlays a copy,
// so it is a chunk of its own rather than part of the eager entry point.

/** A tenant's partition. */
type TenantPartition = Extract<TmPartition, { readonly kind: 'tenant' }>;

/** What a verb's telemetry names it. */
type TmVerbName = 'read' | 'read-all' | 'clear' | 'clear-read' | 'stop-job' | 'seen';

/** The verbs' failure toast's key, so a repeated failure replaces it. */
const FAILED_KEY = 'tm.attention.failed';

/** The document each panel shows. */
const DOCUMENTS = { bell: 'inbox', tray: 'assignments' } as const;

/**
 * The summaries' count cap, the model's own: a count at it reads "99+".
 * Repeated here, so the chunk imports nothing but types from the model.
 */
const CAP = 100;

/** A notification's member as a number, else `undefined`; a member the row's type names, emitted or open. */
function numberOf(row: TmEntityType<'core.Notification'>, member: keyof TmEntityType<'core.Notification'>): number | undefined {
  const value: unknown = row[member];
  return typeof value === 'number' ? value : undefined;
}

/** A notification's instant member in epoch milliseconds, else `NaN`; a member the row's type names, emitted or open. */
function timeOf(row: TmEntityType<'core.Notification'>, member: keyof TmEntityType<'core.Notification'>): number {
  const value: unknown = row[member];
  return typeof value === 'string' ? Date.parse(value) : Number.NaN;
}

/** Lowers a capped count, which stays capped: past the cap the true count is unknown. */
function lower(count: number, by: number): number {
  return count >= CAP ? count : Math.max(0, count - by);
}

/**
 * An inbox copy with the verbs' overlays that still hold, and the stops
 * asked, applied. Internal: the model applies it.
 *
 * @param copy - The stored copy.
 * @param overlays - The overlays that still hold over it, oldest first.
 * @param stopping - The jobs this tab asked to stop.
 * @param now - The time, in epoch milliseconds, that marks a row read or a stop asked.
 * @returns The copy as the user sees it.
 */
export function tmApplyOverlays(
  copy: TmInboxSummary,
  overlays: readonly TmInboxOverlay[],
  stopping: ReadonlySet<number>,
  now: number,
): TmInboxSummary {
  // A row is unseen when it arrived after the user last opened the bell.
  const seenAt = Date.parse(copy.seenAt ?? '');
  const unseen = (row: TmEntityType<'core.Notification'>): boolean => !(timeOf(row, 'createdAt') <= seenAt);
  const unread = (row: TmEntityType<'core.Notification'>): boolean => row['readAt'] === undefined || row['readAt'] === null;
  let { latest, unread: unreadCount, unseen: unseenCount } = copy;
  const at = new Date(now).toISOString();
  for (const overlay of overlays) {
    const named = (row: TmEntityType<'core.Notification'>): boolean => overlay.ids === null || overlay.ids.has(numberOf(row, 'id') ?? NaN);
    if (overlay.verb === 'read' || overlay.verb === 'read-all') {
      // Read rows stay, marked read; the count drops by those it marks.
      const marked = latest.filter((row) => named(row) && unread(row)).length;
      latest = latest.map((row) => (named(row) && unread(row) ? { ...row, readAt: at } : row));
      unreadCount = overlay.verb === 'read-all' ? 0 : lower(unreadCount, marked);
    } else {
      // Cleared rows leave, and their counts with them.
      const cleared = latest.filter((row) => (overlay.verb === 'clear' ? named(row) : !unread(row)));
      latest = latest.filter((row) => !cleared.includes(row));
      unreadCount = lower(unreadCount, cleared.filter(unread).length);
      unseenCount = lower(unseenCount, cleared.filter(unseen).length);
    }
  }
  const running = copy.running.map((job) => (stopping.has(job.id) && job.cancelRequestedAt === undefined ? { ...job, cancelRequestedAt: at } : job));
  return { ...copy, latest, unread: unreadCount, unseen: unseenCount, running };
}

/**
 * The attention model's server calls. Created in an injection context, once
 * the chunk loads.
 */
export class TmAttentionCalls {
  readonly #api = inject(TmApi);
  readonly #store = inject(TmClientStore);
  readonly #telemetry = inject(TmTelemetry);
  readonly #toasts = inject(TmToasts);
  readonly #l10n = inject(TmLocalization);
  readonly #now = inject(TM_NOW);
  readonly #documents = inject(ɵTM_UNIQUE_LEADER_DOCUMENTS);

  /**
   * Sends a verb and reports its outcome; a failure toasts until the user
   * tries again or dismisses it.
   *
   * @param verb - The verb, as its telemetry names it.
   * @param route - The verb's route.
   * @param body - The verb's body.
   * @param tenantId - The tenant.
   * @param retry - Runs the verb again, from the failure toast.
   * @returns The server's answer, or `null` when the verb failed.
   */
  async verb<T>(verb: Exclude<TmVerbName, 'seen'>, route: string, body: TmJson, tenantId: number, retry: () => void): Promise<T | null> {
    try {
      const result = await this.#api.write<T>(route, body, { tenantId });
      this.#done(verb, true);
      return result;
    } catch (error) {
      this.#failed(verb, error);
      this.#toasts.show({
        tone: 'error',
        title: this.#l10n.ɵtext('tm.attention.toasts.failed')(),
        duration: 'sticky',
        key: FAILED_KEY,
        action: { label: this.#l10n.ɵtext('tm.errors.retry')(), run: retry },
      });
      return null;
    }
  }

  /**
   * Marks a panel's document seen. A failure marks nothing and toasts
   * nothing: the badge comes back with the panel's own copy.
   *
   * @param panel - The panel.
   * @param tenantId - The tenant.
   * @returns What releases the panel's seen overlay, or `null` when the seen failed.
   */
  async seen(panel: TmPanel, tenantId: number): Promise<TmSeenRelease | null> {
    try {
      const result = await this.#api.write<TmInboxVerbResult | { readonly seenAt: string }>(
        panel === 'bell' ? 'inbox/seen' : 'assignments/seen',
        {},
        { tenantId },
      );
      this.#done('seen', true);
      return 'version' in result ? { kind: 'version', version: result.version } : { kind: 'seen-at', at: Date.parse(result.seenAt) };
    } catch (error) {
      this.#failed('seen', error);
      return null;
    }
  }

  /**
   * Reads a registered document and stores it stamped with the time the
   * read was sent. Nothing is read offline, or for a document the app does
   * not register; an aborted, failed or refused read leaves the stored copy.
   *
   * @param partition - The tenant's partition.
   * @param name - The document, or the panel whose document it is.
   * @param abort - Aborts the read.
   * @param sent - Told the send time once the read is sent.
   * @returns A promise that settles once the copy is stored, or the read failed.
   */
  async read(partition: TenantPartition, name: 'inbox' | 'assignments' | TmPanel, abort: AbortSignal, sent?: (at: number) => void): Promise<void> {
    const documentName = name === 'bell' || name === 'tray' ? DOCUMENTS[name] : name;
    const document = this.#documents.find((candidate) => candidate.name === documentName);
    if (document === undefined || !untracked(this.#api.online)) {
      return;
    }
    const sentAt = this.#now();
    sent?.(sentAt);
    try {
      const data = await this.#api.read<unknown>(document.route, {}, { signal: abort, background: true, tenantId: partition.tenantId });
      await this.#store.put(partition, documentName, { tag: null, savedAt: sentAt, data });
    } catch (error) {
      // The HTTP layer and the store report what needs reporting.
      if (!(error instanceof TmApiError) && !(error instanceof TmStoreRefusedError)) {
        this.#telemetry.error(error, { source: 'tm-attention' });
      }
    }
  }

  /** Reports a verb's outcome. */
  #done(verb: TmVerbName, ok: boolean): void {
    this.#telemetry.event('tm.attention.verb', { verb, outcome: ok ? 'ok' : 'failed' });
  }

  /** Reports a failed verb, and the error when the HTTP layer has not. */
  #failed(verb: TmVerbName, error: unknown): void {
    this.#done(verb, false);
    if (!(error instanceof TmApiError)) {
      this.#telemetry.error(error, { source: 'tm-attention' });
    }
  }
}

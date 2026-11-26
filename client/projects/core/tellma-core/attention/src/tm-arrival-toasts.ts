// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

/// <reference types="@tellma/core-tools/lucide-icons" />

import { DOCUMENT, inject } from '@angular/core';
import { Router } from '@angular/router';
import {
  TM_TOAST_ARRIVAL_MS,
  type TmAssignmentsSummary,
  TmDisplayNames,
  type TmEntityType,
  type TmId,
  type TmInboxSummary,
  TmLaunchCache,
  TmLocalization,
  type TmNotificationSeverity,
  type TmTenantFacts,
  type TmTenantUrls,
  TmTelemetry,
  TmUrls,
  ɵTmClientMetadataStore,
} from '@tellma/core';
import { TmIconRegistry } from '@tellma/core-ui/icon';
import { type TmToast, type TmToastRef, TmToasts } from '@tellma/core-ui/toast';
import Inbox from 'lucide/dist/esm/icons/inbox.mjs';

import { TmAttention } from './tm-attention';
import { TmAttentionText } from './tm-attention-text';
import { tmIcuArguments } from './tm-icu-arguments';

// The arrival toasts (spec 0028 §8.1): which rows of a copy arrived and the
// toast each arrival shows. The arrival engine loads this file with import()
// at the first copy it hands on: no first-frame call needs it, so it is a
// chunk of its own rather than part of the eager entry point.

/** A toast's kind, as its telemetry names it. */
type ArrivalKind = 'notification' | 'assignment' | 'grouped' | 'tenant-state';

/** Each severity's toast tone. */
const TONES: Readonly<Record<TmNotificationSeverity, TmToast['tone']>> = {
  Info: 'neutral',
  Success: 'success',
  Warning: 'warning',
  Error: 'error',
};

/** The tenant-state toast's key, so a second change replaces the first. */
const TENANT_KEY = 't:state';

/** The scope of the arrival toasts' strings under `tm`. */
const SCOPE = 'arrivals';

/** The arrival toasts' English strings, under `tm.arrivals`; they load with this chunk, not with the first frame. */
const STRINGS_EN = {
  assigned: '{title} assigned to you',
  returned: '{title} returned to you',
  comment: '{assigner}: {comment}',
  assignedBy: 'Assigned by {assigner}',
  many: '{count, plural, one {# document assigned to you} other {# documents assigned to you}}',
  readOnly: '{tenant} is now read-only. You can view data but not change it.',
  frozen: '{tenant} is now under maintenance. You can view data but not change it until it ends.',
  view: 'View',
  open: 'Open',
  download: 'Download',
} as const;

// The row helpers are generic over the row's type: a distribution's emitted
// declarations type a notification or an assignment as a closed interface,
// and without them the row is an open JSON object, so a member is one the
// row's own type names.

/** A row's member as non-empty text, else `undefined`. */
function textOf<R extends object>(row: R, member: keyof R): string | undefined {
  const value: unknown = row[member];
  return typeof value === 'string' && value !== '' ? value : undefined;
}

/** A row's instant member in epoch milliseconds, else `NaN`. */
function timeOf<R extends object>(row: R, member: keyof R): number {
  return Date.parse(textOf(row, member) ?? '');
}

/** The newest instant among rows, or `-Infinity` for none. */
function newest<R extends object>(rows: readonly R[], member: keyof R): number {
  return rows.reduce((latest, row) => Math.max(latest, timeOf(row, member) || -Infinity), -Infinity);
}

/**
 * Shows the toasts of what arrived in a copy of a tenant's attention
 * documents. Created in an injection context, once the chunk loads; it
 * registers the `inbox` icon the assignment toasts draw.
 */
export class TmArrivalToasts {
  readonly #toasts = inject(TmToasts);
  readonly #text = inject(TmAttentionText);
  readonly #attention = inject(TmAttention);
  readonly #urls = inject(TmUrls);
  readonly #router = inject(Router);
  readonly #telemetry = inject(TmTelemetry);
  readonly #l10n = inject(TmLocalization);
  readonly #metadata = inject(ɵTmClientMetadataStore);
  readonly #names = inject(TmDisplayNames);
  readonly #launch = inject(TmLaunchCache);
  readonly #document = inject(DOCUMENT);

  /** Creates the toasts, registering their strings and the icon they draw. */
  constructor() {
    this.#l10n.ɵregisterStrings(SCOPE, STRINGS_EN);
    inject(TmIconRegistry).register({ inbox: Inbox });
  }

  // --- Notifications ---

  /**
   * Toasts the notifications newer than the previous copy's newest, and
   * created at or after `since`, that are unread and of a toasting type,
   * oldest first.
   *
   * @param copy - The copy that arrived.
   * @param previous - The copy before it.
   * @param tenantId - The tenant both copies belong to.
   * @param since - The earliest creation time that counts, in epoch milliseconds.
   */
  notifications(copy: TmInboxSummary, previous: TmInboxSummary, tenantId: number, since: number): void {
    const after = newest(previous.latest, 'createdAt');
    const types = this.#metadata.current()?.notificationTypes ?? [];
    const arrivals = copy.latest.filter(
      (row) =>
        timeOf(row, 'createdAt') > after &&
        timeOf(row, 'createdAt') >= since &&
        (row['readAt'] === undefined || row['readAt'] === null) &&
        types.some((type) => type.key === row['type'] && type.toast),
    );
    // Shown oldest first, so the newest end up on top and the pill counts the oldest.
    for (const row of arrivals.reverse()) {
      this.#notification(row, tenantId);
    }
  }

  /** One notification's toast. */
  #notification(row: TmEntityType<'core.Notification'>, tenantId: number): void {
    const type = textOf(row, 'type') ?? '';
    const id = row['id'];
    const argumentsJson = textOf(row, 'argumentsJson') ?? null;
    const { title, detail } = this.#text.notification(type, argumentsJson)();
    const declared = this.#metadata.current()?.notificationTypes.find((candidate) => candidate.key === type)?.severity;
    const severity = (textOf(row, 'severity') as TmNotificationSeverity | undefined) ?? declared ?? 'Info';
    const urls = this.#urls.forTenant(tenantId);
    // The panel row's activation: an export downloads its file; any other notice with a target opens through its route.
    const fileId = type === 'core.export.ready' ? tmIcuArguments(argumentsJson)?.['fileId'] : undefined;
    const action =
      typeof fileId === 'number'
        ? { label: this.#string('download'), run: () => this.#download(urls.blob('export-file', fileId, { download: true })) }
        : textOf(row, 'targetResource') !== undefined && row['targetId'] !== undefined && row['targetId'] !== null
          ? { label: this.#string('view'), run: () => this.#go(urls.page(`notifications/${String(id)}/open`)) }
          : undefined;
    this.#show('notification', {
      tone: TONES[severity] ?? 'neutral',
      title,
      ...(detail === null ? {} : { detail }),
      duration: severity === 'Error' ? 'sticky' : TM_TOAST_ARRIVAL_MS,
      key: `n:${String(id)}`,
      ...(action === undefined ? {} : { action }),
    });
  }

  /** Downloads a file the way a download link does. */
  #download(url: string): void {
    const link = this.#document.createElement('a');
    link.href = url;
    link.download = '';
    link.click();
  }

  // --- Assignments ---

  /**
   * Toasts the assignments newer than the previous copy's newest, and
   * assigned at or after `since`, that the user did not assign: one with
   * its caption, or several grouped.
   *
   * @param copy - The copy that arrived.
   * @param previous - The copy before it.
   * @param tenantId - The tenant both copies belong to.
   * @param since - The earliest assignment time that counts, in epoch milliseconds.
   */
  assignments(copy: TmAssignmentsSummary, previous: TmAssignmentsSummary, tenantId: number, since: number): void {
    const after = newest(previous.latest, 'assignedAt');
    const me = this.#launch.docs.me()?.user.id;
    const arrivals = copy.latest.filter((row) => timeOf(row, 'assignedAt') > after && timeOf(row, 'assignedAt') >= since && row['assignedById'] !== me);
    const [row] = arrivals;
    if (arrivals.length === 1 && row !== undefined) {
      this.#names.rememberRelated(copy.related);
      this.#assignment(row, tenantId);
    } else if (arrivals.length > 1) {
      this.#show('grouped', {
        tone: 'accent',
        title: this.#string('many', { count: arrivals.length }),
        duration: TM_TOAST_ARRIVAL_MS,
        key: 'a:many',
        action: {
          label: this.#string('view'),
          run: () => {
            if (this.#attention.open() !== 'tray') {
              this.#attention.toggle('tray', 'toast');
            }
          },
        },
      });
    }
  }

  /** One assignment's toast: its caption, and the assigner's comment, else the caption's detail, else who assigned it. */
  #assignment(row: TmEntityType<'core.Assignment'>, tenantId: number): void {
    const caption = this.#text.caption(row)();
    const assignerId = row['assignedById'];
    const assigner = typeof assignerId === 'number' ? this.#names.label('core.User', assignerId)() : null;
    const comment = textOf(row, 'comment');
    const about = [caption.detail, caption.amount].filter((part) => part !== null).join(' · ');
    const detail =
      comment !== undefined
        ? assigner === null
          ? comment
          : this.#string('comment', { assigner, comment })
        : about !== ''
          ? about
          : assigner === null
            ? undefined
            : this.#string('assignedBy', { assigner });
    const urls: TmTenantUrls = this.#urls.forTenant(tenantId);
    const resource = textOf(row, 'resource');
    const targetId = row['targetId'];
    // A target's id is a long, which travels as decimal text.
    const target =
      resource !== undefined && (typeof targetId === 'number' || typeof targetId === 'string') ? urls.target(resource, targetId as TmId) : null;
    this.#show('assignment', {
      tone: 'accent',
      icon: 'inbox',
      title: this.#string(row['isReturn'] === true ? 'returned' : 'assigned', { title: caption.title }),
      ...(detail === undefined ? {} : { detail }),
      duration: TM_TOAST_ARRIVAL_MS,
      key: `a:${String(row['id'])}`,
      ...(target === null ? {} : { action: { label: this.#string('open'), run: () => this.#go(target) } }),
    });
  }

  // --- The tenant's state ---

  /**
   * Toasts a tenant that turned read-only or frozen.
   *
   * @param state - The tenant's new state.
   */
  tenant(state: Exclude<TmTenantFacts['state'], 'Active'>): void {
    const names = this.#launch.docs.settings()?.names ?? [];
    const tenant = names[this.#l10n.contentSlot() - 1] || names[0] || '';
    this.#show('tenant-state', {
      tone: 'warning',
      title: this.#string(state === 'Frozen' ? 'frozen' : 'readOnly', { tenant }),
      duration: TM_TOAST_ARRIVAL_MS,
      key: TENANT_KEY,
    });
  }

  // --- Helpers ---

  /** Shows a toast and reports how it closed. */
  #show(kind: ArrivalKind, toast: TmToast): void {
    const ref: TmToastRef = this.#toasts.show(toast);
    void ref.closed.then((outcome) => {
      // A toast the code closed is no user outcome.
      if (outcome !== 'api') {
        this.#telemetry.event('tm.attention.toast', { kind, outcome });
      }
    });
  }

  /** Follows an in-app link. */
  #go(url: string): void {
    void this.#router.navigateByUrl(url);
  }

  /** An arrival string. */
  #string(key: keyof typeof STRINGS_EN, params?: Readonly<Record<string, unknown>>): string {
    return this.#l10n.ɵtext(`tm.${SCOPE}.${key}`, params)();
  }
}

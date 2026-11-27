// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

/// <reference types="@tellma/core-tools/lucide-icons" />

import { LiveAnnouncer } from '@angular/cdk/a11y';
import { LocationStrategy } from '@angular/common';
import {
  afterNextRender,
  ChangeDetectionStrategy,
  Component,
  computed,
  DestroyRef,
  effect,
  ElementRef,
  inject,
  Injector,
  runInInjectionContext,
  type Signal,
  signal,
  untracked,
  viewChild,
} from '@angular/core';
import { NavigationStart, Router } from '@angular/router';
import {
  TM_NOW,
  TmApi,
  TmDisplayName,
  type TmDisplayNameParts,
  TmDisplayNames,
  type TmEntityType,
  type TmInboxRunningJob,
  type TmJson,
  type TmJsonObject,
  TmLaunchCache,
  TmLocalization,
  type TmNotificationSeverity,
  TmPageActions,
  TmServerText,
  TmTelemetry,
  TmUrls,
  ɵTmClientMetadataStore,
} from '@tellma/core';
import {
  TmAttentionText,
  type TmNotificationText,
  tmIcuArguments,
  ɵTmAttentionRowStyles,
  ɵTmAttentionSkeletonRows,
  ɵTmRovingRows,
} from '@tellma/core/attention-rows';
import { TmL10n } from '@tellma/core-ui';
import { TmBadge } from '@tellma/core-ui/badge';
import { ɵTM_BANNER_GLYPHS } from '@tellma/core-ui/banner';
import { TmButton } from '@tellma/core-ui/button';
import { TmConfirm } from '@tellma/core-ui/confirm';
import { TmEmptyState } from '@tellma/core-ui/empty-state';
import { TmIcon, type TmIconSource } from '@tellma/core-ui/icon';
import { type TmMenuEntry, TmMenu, TmMenuTrigger } from '@tellma/core-ui/menu';
import { TM_MODAL_DATA } from '@tellma/core-ui/modal';
import { TmProgressBar } from '@tellma/core-ui/progress';
import { TmRelativeTime } from '@tellma/core-ui/relative-time';
import CircleCheck from 'lucide/dist/esm/icons/circle-check.mjs';
import Ellipsis from 'lucide/dist/esm/icons/ellipsis.mjs';

import { TmAttention } from './tm-attention';
// The server calls' chunk loads with this one, at idle: the model's verbs then overlay the panel's
// first copy in the click's frame, and Review's lookup is in memory.
import { tmJobOwner } from './tm-attention-calls';
import { TM_ROW_PARAMS, type TmAttentionPanelData, tmItemPosition, tmKeepPanelFocus, tmPublishPanel } from './tm-attention-panel-data';

// The bell's panel (spec 0028 §6.2–§6.4): the header with Mark all as read
// and More options, the user's running jobs, the status line while live
// updates are paused, the notification rows and the footer link. It renders
// from the stored inbox with this tab's overlays, in the frame it opens; the
// model reads a fresh copy in the background, and rows update in place,
// keyed by id. Rows that arrive while the list is scrolled down wait under a
// pinned "{n} new" button, so nothing moves under the reader. Each list is
// one tab stop with roving rows. A frozen tenant shows no verb; offline,
// the verbs stay, disabled with "You're offline". The verbs that the page
// context offers too, Mark all as read and Stop, run through the page
// actions' dispatcher, which asks before any Stop (spec 0026 §9.5). An open
// panel publishes its overlay page context, `panel:bell`.

/** A notification row, as the panel renders it. */
interface NoticeView {
  /** The notification. */
  readonly row: TmEntityType<'core.Notification'>;
  /** Its id. */
  readonly id: number;
  /** Its id and creation time: a replaced row has a fresh one, so it arrives anew. */
  readonly key: string;
  /** Its title and detail, which follow the language. */
  readonly text: Signal<TmNotificationText>;
  /** Its severity. */
  readonly severity: TmNotificationSeverity;
  /** Its severity's icon. */
  readonly icon: TmIconSource;
  /** Its severity's colour, such as `var(--success)`. */
  readonly color: string;
  /** Whether it is unread. */
  readonly unread: boolean;
  /** Its open route's app URL, which marks it read and opens its target. */
  readonly url: string;
  /** The open route's address, as the router writes it. */
  readonly href: string;
  /** An export's file download address; `null` for any other type. */
  readonly download: string | null;
  /** The actor's display name, shown when the type has no detail; `null` without an actor. */
  readonly actor: { readonly parts: Signal<TmDisplayNameParts | null>; readonly label: Signal<string | null> } | null;
  /** When it was created, as ISO 8601. */
  readonly createdAt: string;
}

/** A running job, as the panel renders it. */
interface JobView {
  /** The job. */
  readonly job: TmInboxRunningJob;
  /** Its label, which follows the language. */
  readonly label: Signal<string>;
  /** Its progress message, which follows the language; `null` without one. */
  readonly message: Signal<string | null>;
  /** Its page's app URL. */
  readonly url: string;
  /** Its page's address. */
  readonly href: string;
}

/**
 * Each severity's icon. Those the banner draws come from it, so the panel
 * reuses the modules the first frame already holds.
 */
const SEVERITY_ICONS: Readonly<Record<TmNotificationSeverity, TmIconSource>> = {
  Success: CircleCheck,
  Warning: ɵTM_BANNER_GLYPHS.warning,
  Error: ɵTM_BANNER_GLYPHS.error,
  Info: ɵTM_BANNER_GLYPHS.info,
};

/** How many running jobs the panel lists before "+{n} more in My jobs". */
const JOBS_SHOWN = 3;

/** How often the rows' accessible names re-read their relative times. */
const MINUTE_MS = 60_000;

/** The users' entity, whose related rows name the actors. */
const USER = 'core.User';

/** A notification's member as non-empty text, else `undefined`. */
function textOf(row: TmEntityType<'core.Notification'>, member: keyof TmEntityType<'core.Notification'>): string | undefined {
  const value: unknown = row[member];
  return typeof value === 'string' && value !== '' ? value : undefined;
}

/** A time member in epoch milliseconds; `NaN` when absent. */
function timeOf(value: string | undefined): number {
  return Date.parse(value ?? '');
}

/**
 * The bell's panel: notifications and running jobs. Created by the panels'
 * host in a popover or an end drawer.
 */
@Component({
  selector: 'tm-bell-panel',
  imports: [
    TmBadge,
    TmButton,
    TmDisplayName,
    TmEmptyState,
    TmIcon,
    TmMenu,
    TmMenuTrigger,
    TmProgressBar,
    TmRelativeTime,
    ɵTmAttentionRowStyles,
    ɵTmAttentionSkeletonRows,
    ɵTmRovingRows,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { class: 'tm-attention-panel', id: 'tm-attention-bell-panel', tabindex: '-1' },
  template: `
    <tm-attention-row-styles />
    <!-- The header: the heading (the drawer's own title stands in for it), then the verbs. -->
    <div class="tm-attention-panel__header">
      @if (!drawer) {
        <h2 #heading class="tm-attention-panel__heading" tabindex="-1">{{ name() }}</h2>
      }
      <span class="tm-attention-panel__spacer"></span>
      @if (!frozen() && (inbox()?.unread ?? 0) > 0) {
        <button tmButton variant="ghost" size="sm" [disabledReason]="offlineReason()" (click)="readAll()">{{ text('bell.markAllRead') }}</button>
      }
      <button #moreButton tmButton variant="ghost" size="sm" [tmMenuTriggerFor]="more" [attr.aria-label]="text('bell.more')">
        <tm-icon [svg]="glyphs.more" />
      </button>
      <tm-menu #more [items]="menu()" [aria-label]="text('bell.more')" />
    </div>
    <div #body class="tm-attention-panel__body" (scroll)="scrolled()">
      <!-- In progress: the newest three running jobs, then the rest's link. -->
      @if (jobs().length > 0) {
        <h3 class="tm-attention-panel__section" id="tm-attention-bell-jobs">{{ text('jobs.heading') }}</h3>
        <ul class="tm-attention-rows" tmRovingRows aria-labelledby="tm-attention-bell-jobs" [fallback]="fallback()">
          @for (view of jobs(); track view.job.id) {
            <li class="tm-attention-row tm-attention-panel__job" data-tm-row [attr.data-tm-id]="view.job.id">
              <span class="tm-attention-row__main">
                <span class="tm-attention-panel__title-line">
                  <a class="tm-attention-row__title" data-tm-row-control [attr.href]="view.href" (click)="openJob($event, view, $index)">{{ view.label() }}</a>
                  @switch (view.job.status) {
                    @case ('Held') {
                      <tm-badge tone="warning">{{ text('jobs.held') }}</tm-badge>
                    }
                    @case ('PendingConfirmation') {
                      <tm-badge tone="warning">{{ text('jobs.awaiting') }}</tm-badge>
                    }
                    @default {
                      @if (percent(view.job); as shown) {
                        <span class="tm-attention-panel__percent">{{ shown }}</span>
                      }
                    }
                  }
                </span>
                @if (view.job.status === 'Pending' || view.job.status === 'Running') {
                  <tm-progress-bar [value]="view.job.status === 'Running' ? (view.job.progressPercent ?? null) : null" [label]="view.label()" />
                }
                <span class="tm-attention-row__line">{{ jobLine(view) }}</span>
              </span>
              <!-- The job's controls: Review while it waits for confirmation, Stop until a stop is asked. -->
              @if (view.job.status === 'PendingConfirmation') {
                <button tmButton data-tm-row-control variant="ghost" size="sm" (click)="review(view, $index)">{{ text('jobs.review') }}</button>
              }
              @if (!frozen() && view.job.cancelRequestedAt === undefined) {
                <button
                  tmButton
                  data-tm-row-control
                  class="tm-attention-panel__hover"
                  variant="ghost"
                  size="sm"
                  [disabledReason]="offlineReason()"
                  (click)="stop(view)"
                >
                  {{ text('jobs.stop') }}
                </button>
              }
            </li>
          }
        </ul>
        @if (moreJobs() > 0) {
          <a class="tm-attention-panel__more" [attr.href]="myJobs.href" (click)="follow($event, myJobs.url)">{{
            text('jobs.more', { count: moreJobs() })
          }}</a>
        }
      }
      <!-- The status line, while live updates are paused. -->
      @if (paused()) {
        <p class="tm-attention-panel__status" role="status">
          <span>{{ text('bell.paused', { time: updatedAt() }) }}</span>
          <button tmButton variant="ghost" size="sm" [disabledReason]="offlineReason()" (click)="refresh()">{{ text('bell.refresh') }}</button>
        </p>
      }
      <!-- The notifications: skeletons before any document, the empty state, or the rows. -->
      @if (inbox() === undefined) {
        <tm-attention-skeleton-rows [label]="name()" />
      } @else if (notices().length === 0) {
        <tm-empty-state size="panel" icon="check-check" [heading]="text('bell.empty')" />
      } @else {
        @if (waiting() > 0) {
          <button tmButton class="tm-attention-panel__fresh" variant="secondary" size="sm" (click)="showWaiting()">
            {{ text('bell.fresh', { count: waiting() }) }}
          </button>
        }
        <ul class="tm-attention-rows" tmRovingRows [attr.aria-label]="name()" [fallback]="fallback()" (rowDelete)="deleted($event)">
          @for (view of notices(); track view.key) {
            <li
              class="tm-attention-row tm-attention-panel__notice"
              data-tm-row
              [attr.data-tm-id]="view.id"
              [class.tm-attention-panel__notice--unread]="view.unread"
            >
              <tm-icon class="tm-attention-row__icon" [attr.data-severity]="view.severity" [style.color]="view.color" [svg]="view.icon" />
              <a
                class="tm-attention-row__main tm-attention-panel__link"
                data-tm-row-control
                [attr.href]="view.href"
                [attr.aria-label]="label(view)"
                (click)="open($event, view, $index)"
              >
                <span class="tm-attention-row__title">{{ view.text().title }}</span>
                <span class="tm-attention-row__line">
                  @if (view.text().detail; as detail) {
                    {{ detail }}
                  } @else if (view.actor; as actor) {
                    <tm-display-name [parts]="actor.parts()" />
                  }
                </span>
              </a>
              @if (view.download; as download) {
                <a tmButton data-tm-row-control variant="ghost" size="sm" download [attr.href]="download">{{ text('bell.download') }}</a>
              }
              <span class="tm-attention-row__end">
                <tm-relative-time [value]="view.createdAt" textStyle="narrow" />
                @if (view.unread) {
                  <span class="tm-attention-panel__dot" aria-hidden="true"></span>
                }
              </span>
              @if (!frozen()) {
                <button
                  tmButton
                  data-tm-row-control
                  class="tm-attention-panel__hover"
                  variant="ghost"
                  size="sm"
                  [attr.aria-label]="text('bell.clear')"
                  [disabledReason]="offlineReason()"
                  (click)="clear(view)"
                >
                  <tm-icon [svg]="glyphs.clear" />
                </button>
              }
            </li>
          }
        </ul>
      }
    </div>
    <!-- The footer: every notification, on its page. -->
    <div class="tm-attention-panel__footer">
      <a class="tm-attention-panel__see-all" [attr.href]="all.href" (click)="follow($event, all.url)">{{ text('bell.seeAll') }}</a>
    </div>
  `,
})
export class TmBellPanel {
  readonly #attention = inject(TmAttention);
  readonly #text = inject(TmAttentionText);
  readonly #server = inject(TmServerText);
  readonly #l10n = inject(TmLocalization);
  readonly #numbers = inject(TmL10n);
  readonly #names = inject(TmDisplayNames);
  readonly #urls = inject(TmUrls);
  readonly #router = inject(Router);
  readonly #location = inject(LocationStrategy);
  readonly #api = inject(TmApi);
  readonly #launch = inject(TmLaunchCache);
  readonly #metadata = inject(ɵTmClientMetadataStore);
  readonly #confirm = inject(TmConfirm);
  readonly #actions = inject(TmPageActions);
  readonly #announcer = inject(LiveAnnouncer);
  readonly #telemetry = inject(TmTelemetry);
  readonly #now = inject(TM_NOW);
  readonly #injector = inject(Injector);
  /** The heading, absent in the drawer. */
  private readonly headingRef = viewChild<ElementRef<HTMLElement>>('heading');
  /** The More options button, where focus moves when Mark all as read leaves. */
  private readonly moreRef = viewChild.required<unknown, ElementRef<HTMLElement>>('moreButton', { read: ElementRef });
  /** The scrolling body. */
  private readonly bodyRef = viewChild.required<ElementRef<HTMLElement>>('body');
  readonly #host = inject<ElementRef<HTMLElement>>(ElementRef).nativeElement;

  /** Each notification's view, kept while its row stays. */
  #notices = new Map<TmEntityType<'core.Notification'>, NoticeView>();
  /** Each job's view, kept while its row stays. */
  #jobs = new Map<TmInboxRunningJob, JobView>();
  /** The rows the reader has been shown, by key; a row outside it arrived while the list was scrolled down. */
  readonly #shown = signal<ReadonlySet<string>>(new Set());
  /** Whether the list is scrolled to its top, where arrivals insert at once. */
  readonly #atTop = signal(true);
  /** Ticks every minute, so the rows' accessible names keep their relative times current. */
  readonly #minute = signal(0);

  /** Whether the panel sits in the end drawer, whose title stands in for the heading. */
  protected readonly drawer = (inject(TM_MODAL_DATA, { optional: true }) as TmAttentionPanelData | null)?.drawer === true;

  /** The glyphs the panel draws itself. */
  protected readonly glyphs = { more: Ellipsis, clear: ɵTM_BANNER_GLYPHS.dismiss } as const;

  /** The panel's heading, which names its lists too: the bell button's own name. */
  protected readonly name = this.#l10n.ɵtext('tm.attention.bell.label');

  /** The stored inbox with this tab's overlays; `undefined` before any copy. */
  protected readonly inbox = this.#attention.inbox;

  /** Whether the tenant is frozen, so no verb shows. */
  protected readonly frozen = computed(() => this.#launch.docs.tenant()?.state === 'Frozen');

  /** Why the verbs are disabled: offline; `null` online. */
  protected readonly offlineReason = computed(() => (this.#api.online() ? null : this.#l10n.ɵtext('tm.errors.offline.title')()));

  /** Whether live updates are paused, so the status line shows. */
  protected readonly paused = computed(() => this.#attention.live() === 'paused');

  /** When the stored copy was read, as a time of day. */
  protected readonly updatedAt = computed(() => {
    const savedAt = this.#attention.ɵinboxSavedAt();
    return savedAt === null ? '' : this.#numbers.formatTime(new Date(savedAt).toISOString());
  });

  /** The running jobs shown, newest first. */
  protected readonly jobs: Signal<readonly JobView[]> = computed(() => {
    const running = [...(this.inbox()?.running ?? [])].sort((a, b) => timeOf(b.createdAt) - timeOf(a.createdAt));
    return untracked(() => {
      const views = new Map<TmInboxRunningJob, JobView>();
      for (const job of running.slice(0, JOBS_SHOWN)) {
        views.set(job, this.#jobs.get(job) ?? this.#jobView(job));
      }
      this.#jobs = views;
      return [...views.values()];
    });
  });

  /** How many running jobs the panel does not list. */
  protected readonly moreJobs = computed(() => Math.max(0, (this.inbox()?.running.length ?? 0) - JOBS_SHOWN));

  /** Every notification the inbox holds, newest first. */
  readonly #allNotices: Signal<readonly NoticeView[]> = computed(() => {
    const latest = this.inbox()?.latest ?? [];
    return untracked(() => {
      const views = new Map<TmEntityType<'core.Notification'>, NoticeView>();
      for (const row of latest) {
        views.set(row, this.#notices.get(row) ?? this.#noticeView(row));
      }
      this.#notices = views;
      return [...views.values()];
    });
  });

  /** The notification rows shown: those the reader has been shown, and every row while the list is at its top. */
  protected readonly notices = computed(() => {
    const shown = this.#shown();
    return this.#allNotices().filter((view) => shown.has(view.key));
  });

  /** How many rows wait under the pinned button. */
  protected readonly waiting = computed(() => this.#allNotices().length - this.notices().length);

  /** The More options menu: the settings, and Clear read unless the tenant is frozen. */
  protected readonly menu = computed<readonly TmMenuEntry[]>(() => {
    const settings = this.#urls.page('me/notifications');
    return [
      {
        id: 'settings',
        label: this.text('bell.settings'),
        href: this.#location.prepareExternalUrl(settings),
        action: () => this.#go(settings),
      },
      ...(this.frozen()
        ? []
        : [
            {
              id: 'clear-read',
              label: this.text('bell.clearRead'),
              ...(this.offlineReason() === null ? {} : { disabledReason: this.offlineReason() ?? '' }),
              action: () => void this.#clearRead(),
            },
          ]),
    ];
  });

  /** The footer's page of every notification. */
  protected readonly all = this.#link(this.#urls.search('core.Notification'));

  /** The user's jobs page, which "+{n} more in My jobs" opens. */
  protected readonly myJobs = this.#link(this.#urls.search('core.Job', { tab: 'mine' }));

  /** Where focus goes when the last row of a list leaves while focused: the heading, else the panel. */
  protected readonly fallback = computed(() => this.headingRef()?.nativeElement ?? this.#host);

  /** Creates the panel: its page context, its arrivals and its first focus. */
  constructor() {
    this.#publish();
    tmKeepPanelFocus(this.#host, () => untracked(this.fallback));

    // --- Arrivals: rows at the top insert at once; below it they wait, and either way they are announced ---
    let known: ReadonlySet<string> | null = null;
    effect(() => {
      const keys = this.#allNotices().map((view) => view.key);
      const atTop = this.#atTop();
      untracked(() => {
        // Skeletons: the first copy's rows are no arrivals.
        if (this.inbox() === undefined) {
          return;
        }
        const arrived = known === null ? 0 : keys.filter((key) => !known!.has(key)).length;
        known = new Set(keys);
        const shown = this.#shown();
        // The first copy, and every copy while at the top, is shown whole; below it, only rows already shown stay.
        this.#shown.set(atTop || shown.size === 0 ? new Set(keys) : new Set(keys.filter((key) => shown.has(key))));
        if (arrived > 0) {
          this.#announcer.announce(this.text('bell.arrived', { count: arrived }), 'polite');
        }
      });
    });

    const timer = setInterval(() => this.#minute.update((tick) => tick + 1), MINUTE_MS);
    inject(DestroyRef).onDestroy(() => clearInterval(timer));

    // --- Opening focuses the first row, else the first header control; after the popover's and the drawer's own first focus ---
    afterNextRender({
      read: () => {
        const first =
          this.bodyRef().nativeElement.querySelector<HTMLElement>('[data-tm-row] [data-tm-row-control]') ??
          this.#host.querySelector<HTMLElement>('.tm-attention-panel__header button');
        first?.focus();
      },
    });
  }

  /**
   * A string of the panels'.
   *
   * @param key - The key under `tm.attentionPanels`.
   * @param params - The message's parameters.
   * @returns The text.
   */
  protected text(key: string, params?: Readonly<Record<string, unknown>>): string {
    return this.#l10n.ɵtext(`tm.attentionPanels.${key}`, params)();
  }

  /**
   * A running job's percent, as the title's end shows it.
   *
   * @param job - The job.
   * @returns The percent, or `null` when it is not running with a known percent.
   */
  protected percent(job: TmInboxRunningJob): string | null {
    return job.status === 'Running' && job.progressPercent !== undefined && job.cancelRequestedAt === undefined
      ? this.#numbers.formatNumber(job.progressPercent / 100, { percent: true })
      : null;
  }

  /**
   * A job row's second line, by its status.
   *
   * @param view - The job.
   * @returns The line.
   */
  protected jobLine(view: JobView): string {
    const { job } = view;
    switch (job.status) {
      case 'Held':
        return this.#server.has('Jobs_HeldAfterGap') ? this.#server.message('Jobs_HeldAfterGap')() : this.text('jobs.heldLine');
      case 'PendingConfirmation':
        return this.text('jobs.awaitingLine');
      default:
        if (job.cancelRequestedAt !== undefined) {
          return this.text('jobs.stopping');
        }
        if (job.status === 'Pending') {
          return this.text('jobs.waiting');
        }
        return view.message() ?? (job.progressPercent === undefined ? this.text('jobs.working') : '');
    }
  }

  /**
   * A notification link's accessible name: its title, its detail or actor,
   * its time and "unread", joined with the language's separator.
   *
   * @param view - The row.
   * @returns The name.
   */
  protected label(view: NoticeView): string {
    this.#minute();
    const { title, detail } = view.text();
    const parts = [
      title,
      detail ?? view.actor?.label() ?? null,
      this.#numbers.formatRelativeTime(view.createdAt, { now: this.#now(), style: 'long' }),
      view.unread ? this.text('bell.unread') : null,
    ];
    return parts.filter((part) => part !== null && part !== '').join(this.#l10n.ɵtext('tm.attentionRows.separator')());
  }

  /** The list scrolled: at the top, the waiting rows insert. */
  protected scrolled(): void {
    this.#atTop.set(this.bodyRef().nativeElement.scrollTop <= 0);
  }

  /** The pinned button: scrolls to the top, where the waiting rows insert, and focuses the first row once they have. */
  protected showWaiting(): void {
    this.bodyRef().nativeElement.scrollTop = 0;
    this.#atTop.set(true);
    // The waiting rows insert in the next render, and the pinned button leaves with it: the first row takes focus then.
    afterNextRender(
      { write: () => this.bodyRef().nativeElement.querySelector<HTMLElement>('[data-tm-row] .tm-attention-panel__link')?.focus() },
      { injector: this.#injector },
    );
  }

  /** Mark all as read, which leaves once nothing is unread: focus moves on to More options first. */
  protected readAll(): void {
    this.moreRef().nativeElement.focus();
    void this.#actions.invoke('attention.read-all', {}, 'user');
  }

  /** Refresh, on the status line. */
  protected refresh(): void {
    void this.#attention.refresh('inbox');
  }

  /**
   * A row's Clear: the row leaves at once, and the clear is announced.
   *
   * @param view - The row.
   */
  protected clear(view: NoticeView): void {
    if (!untracked(this.#attention.writable)) {
      return;
    }
    void this.#attention.clear([view.id]);
    this.#announcer.announce(this.text('bell.cleared'), 'polite');
  }

  /**
   * A row's Delete key: clears it, as its Clear does.
   *
   * @param row - The row's element.
   */
  protected deleted(row: HTMLElement): void {
    const view = untracked(this.notices).find((candidate) => String(candidate.id) === row.dataset['tmId']);
    if (view !== undefined && !untracked(this.frozen)) {
      this.clear(view);
    }
  }

  /**
   * A notification link's click: a plain click closes the panel and opens
   * the row's route, which marks it read; a click that opens a new tab
   * stays the browser's.
   *
   * @param event - The click.
   * @param view - The row.
   * @param index - Its index in the list.
   */
  protected open(event: MouseEvent, view: NoticeView, index: number): void {
    if (this.#plain(event)) {
      this.#opened('notification', index);
      this.#go(view.url);
    }
  }

  /**
   * A job title's click: opens the job's page.
   *
   * @param event - The click.
   * @param view - The job.
   * @param index - Its index in the list.
   */
  protected openJob(event: MouseEvent, view: JobView, index: number): void {
    if (this.#plain(event)) {
      this.#opened('job', index);
      this.#go(view.url);
    }
  }

  /**
   * A link's click elsewhere in the panel: the footer, or the rest of the jobs.
   *
   * @param event - The click.
   * @param url - The app URL.
   */
  protected follow(event: MouseEvent, url: string): void {
    if (this.#plain(event)) {
      this.#go(url);
    }
  }

  /**
   * A job's Review: the panel closes, and the record that owns the job opens,
   * where the user reads its warnings and confirms; without one, or when it
   * cannot be found in time, the job's page. A user who moved on while the
   * owner was looked up stays where they went.
   *
   * @param view - The job.
   * @param index - Its index in the list.
   */
  protected review(view: JobView, index: number): void {
    this.#opened('job', index);
    this.#close();
    // The lookup ends with the first navigation that starts meanwhile, the user's own.
    const life = new AbortController();
    const moved = this.#router.events.subscribe((event) => event instanceof NavigationStart && life.abort());
    void runInInjectionContext(this.#injector, () => tmJobOwner(view.job, life.signal)).then((url) => {
      moved.unsubscribe();
      if (!life.signal.aborted) {
        void this.#router.navigateByUrl(url ?? view.url);
      }
    });
  }

  /**
   * A job's Stop, through the dispatcher, which asks first; the row's link
   * takes focus once Stop leaves.
   *
   * @param view - The job.
   */
  protected stop(view: JobView): void {
    void this.#actions.invoke('attention.stop-job', { id: view.job.id }, 'user');
  }

  // --- The rows ---

  /** Makes a notification row's view. */
  #noticeView(row: TmEntityType<'core.Notification'>): NoticeView {
    const id = Number(row['id']);
    const type = textOf(row, 'type') ?? '';
    const argumentsJson = textOf(row, 'argumentsJson') ?? null;
    const declared = this.#metadata.current()?.notificationTypes.find((candidate) => candidate.key === type)?.severity;
    const given = (textOf(row, 'severity') as TmNotificationSeverity | undefined) ?? declared;
    const severity = given !== undefined && given in SEVERITY_ICONS ? given : 'Info';
    const actorId = row['actorUserId'];
    const url = this.#urls.page(`notifications/${id}/open`);
    const fileId = type === 'core.export.ready' ? tmIcuArguments(argumentsJson)?.['fileId'] : undefined;
    return {
      row,
      id,
      key: `${id}@${textOf(row, 'createdAt') ?? ''}`,
      text: this.#text.notification(type, argumentsJson),
      severity,
      icon: SEVERITY_ICONS[severity],
      color: `var(--${severity.toLowerCase()})`,
      unread: row['readAt'] === undefined || row['readAt'] === null,
      url,
      href: this.#location.prepareExternalUrl(url),
      download: typeof fileId === 'number' ? this.#urls.blob('export-file', fileId, { download: true }) : null,
      actor:
        typeof actorId === 'number'
          ? { parts: this.#names.labelParts(USER, actorId), label: this.#names.label(USER, actorId) }
          : null,
      createdAt: textOf(row, 'createdAt') ?? '',
    };
  }

  /** Makes a running job's view. */
  #jobView(job: TmInboxRunningJob): JobView {
    const url = this.#urls.page(`jobs/${job.id}`);
    return {
      job,
      label: this.#text.job(job),
      message: this.#text.jobMessage(job.progressKey ?? null, job.progressArgumentsJson ?? null),
      url,
      href: this.#location.prepareExternalUrl(url),
    };
  }

  // --- Navigation ---

  /** Whether a click is a plain one, which the panel routes in place; a modified click opens a tab. */
  #plain(event: MouseEvent): boolean {
    if (event.button !== 0 || event.ctrlKey || event.metaKey || event.shiftKey || event.altKey) {
      return false;
    }
    event.preventDefault();
    return true;
  }

  /** Closes the panel and navigates in the app. */
  #go(url: string): void {
    this.#close();
    void this.#router.navigateByUrl(url);
  }

  /** Closes the panel. */
  #close(): void {
    if (untracked(this.#attention.open) === 'bell') {
      this.#attention.toggle('bell', 'click');
    }
  }

  /** Reports an opened row and its place. */
  #opened(kind: 'notification' | 'job', index: number): void {
    this.#telemetry.event('tm.attention.item-opened', { panel: 'bell', kind, position: tmItemPosition(index) });
  }

  /** A link's app URL and address. */
  #link(url: string): { readonly url: string; readonly href: string } {
    return { url, href: this.#location.prepareExternalUrl(url) };
  }

  /** Clear read, after the confirmation. */
  async #clearRead(): Promise<void> {
    const confirmed = await this.#confirm.confirm({
      title: this.text('bell.clearReadTitle'),
      message: this.text('bell.clearReadMessage'),
      confirmLabel: this.text('bell.clear'),
      destructive: true,
    });
    if (confirmed) {
      await this.#attention.clearRead();
    }
  }

  // --- The page context ---

  /** Publishes the panel's overlay page context while it is open. */
  #publish(): void {
    const enabled = computed(() => this.#attention.writable());
    const none = signal<string | null>(null).asReadonly();
    // The running job an invocation names, read from arguments as given: any JSON, null included.
    const runningJob = (args: TmJson): TmInboxRunningJob | undefined => {
      const id = typeof args === 'object' && args !== null && !Array.isArray(args) ? (args as TmJsonObject)['id'] : undefined;
      return untracked(this.inbox)?.running.find((job) => job.id === id);
    };
    tmPublishPanel(
      'bell',
      {
        title: this.name,
        status: computed(() => (this.inbox() === undefined ? 'loading' : 'ready')),
        state: computed(() => {
          const inbox = this.inbox();
          return {
            unseen: inbox?.unseen ?? null,
            unread: inbox?.unread ?? null,
            live: this.#attention.live(),
            jobs: (inbox?.running ?? []).map((job) => ({
              id: job.id,
              handlerKey: job.handlerKey,
              status: job.status,
              stopping: job.cancelRequestedAt !== undefined,
              ...(job.progressPercent === undefined ? {} : { percent: job.progressPercent }),
            })),
          };
        }),
        visible: computed(() => {
          const rows = this.notices();
          return {
            columns: [
              { path: 'Type', label: 'Type' },
              { path: 'Title', label: this.name() },
              { path: 'State', label: 'State' },
            ],
            rows: rows.slice(0, 200).map((view) => ({
              id: view.id,
              cells: { Type: textOf(view.row, 'type') ?? '', Title: view.text().title, State: view.unread ? 'unread' : 'read' },
            })),
            total: rows.length,
            truncated: rows.length > 200,
          };
        }),
      },
      (id) => {
        const index = untracked(this.#allNotices).findIndex((view) => view.id === id);
        const view = untracked(this.#allNotices)[index];
        if (view !== undefined) {
          this.#opened('notification', index);
          this.#go(view.url);
        }
        return view !== undefined;
      },
      [
        {
          id: 'attention.read-all',
          labelKey: 'tm.attentionPanels.bell.markAllRead',
          group: 'menu',
          enabled: computed(() => enabled() && (this.inbox()?.unread ?? 0) > 0),
          destructive: false,
          disabledReason: none,
          invoke: async () => {
            await this.#attention.readAll();
            return { outcome: 'done' };
          },
        },
        {
          id: 'attention.stop-job',
          labelKey: 'tm.attentionPanels.jobs.stop',
          group: 'menu',
          enabled,
          // Every Stop asks first, the panel's own too, in the job's kind's words.
          destructive: true,
          confirm: {
            titleKey: 'tm.attentionPanels.jobs.stopTitle',
            messageKey: 'tm.attentionPanels.jobs.stopMessage',
            cancelKey: 'tm.attentionPanels.jobs.keepRunning',
            args: (args) => ({ kind: runningJob(args)?.handlerKey === 'core.import' ? 'import' : 'job' }),
          },
          // Only a job the bell shows running is asked about; any other id is refused before the question.
          accepts: (args) => runningJob(args) !== undefined,
          disabledReason: none,
          params: TM_ROW_PARAMS,
          // The stop shows in this frame and the action ends; a failed stop toasts, so a second Stop never waits on the first.
          invoke: async (args) => {
            const job = runningJob(args);
            if (job === undefined) {
              return { outcome: 'refused', reason: 'not-found' };
            }
            void this.#attention.stopJob(job.id);
            return { outcome: 'done' };
          },
        },
      ],
    );
  }
}

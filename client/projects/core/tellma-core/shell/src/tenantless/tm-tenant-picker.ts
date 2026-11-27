// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import {
  afterNextRender,
  ChangeDetectionStrategy,
  Component,
  computed,
  DestroyRef,
  effect,
  inject,
  signal,
  untracked,
  viewChild,
  ViewContainerRef,
} from '@angular/core';
import { ActivatedRoute, type ParamMap, Router, RouterLink } from '@angular/router';
import {
  provideTmPageScope,
  type TmApiError,
  TmClientStore,
  type TmContextError,
  TmLaunchCache,
  TmLocalization,
  TmPageContexts,
  TmSession,
  TmShortcuts,
  TmTelemetry,
  TmUrls,
  type TmVisibleData,
} from '@tellma/core';
import { TmAlert } from '@tellma/core-ui/alert';
import { TmBadge, type TmBadgeTone } from '@tellma/core-ui/badge';
import { TmButton } from '@tellma/core-ui/button';
import { TmEmptyState } from '@tellma/core-ui/empty-state';
import { TmSearchField } from '@tellma/core-ui/search-field';
import { TmSkeleton } from '@tellma/core-ui/skeleton';
import type { TmErrorView } from '@tellma/core/errors';

import { TM_SHELL_ERROR_VIEWS } from '../tm-shell-errors';
import { tmTenantNames } from './tm-tenant-names';
import { type TmTenantBadge, tmTenantCountBucket, type TmTenantRow, tmTenantRows, tmTenantSearch } from './tm-tenant-rows';
import { TmTenantlessShell } from './tm-tenantless-shell';
import { tmRegisterTenantlessStrings } from './tm-tenantless-strings';

// The tenant picker (spec 0027 §6.2; spec 0025 §3.1): the user's
// memberships from the launch cache in the first frame, revalidated after it
// renders (spec 0024 §14: no request before the first render). It never
// redirects. Without a stored list it shows four skeleton rows until the
// read answers, the error state when the read fails, or the empty state for
// a user without memberships. From seven memberships a search filters the
// rows, keeps its text in `?q=`, follows the only enabled match on Enter,
// and takes Mod+K. A tenant the user chose and can no longer open arrives
// with `?notice=tenant-not-found&from=<id>`, which shows a dismissible notice.
// Once the first frame shows a Connect, a tenant home's chunks load at idle,
// as hover and focus on Connect load them, so the first entry into a tenant
// paints its skeleton in the click's frame.

/** The memberships from which the search shows. */
const SEARCH_FROM = 7;

/** The most rows the page context lists. */
const CONTEXT_ROWS = 200;

/** The tone of each badge. */
const TONES: Readonly<Record<TmTenantBadge, TmBadgeTone>> = {
  lastUsed: 'neutral',
  sandbox: 'accent',
  readOnly: 'warning',
  frozen: 'warning',
  suspended: 'error',
  provisioning: 'neutral',
  inactive: 'neutral',
};

/** One of a row's names, as rendered. */
interface TmNameView {
  /** The name. */
  readonly text: string;
  /** Its language, or `null` when the tenant lists none for it. */
  readonly lang: string | null;
}

/** A row as the template renders it. */
interface TmRowView {
  /** The tenant's id. */
  readonly id: number;
  /** The name in the active language. */
  readonly name: TmNameView;
  /** The other names, then the tenant's id, on the second line. */
  readonly others: readonly TmNameView[];
  /** "Tenant 17". */
  readonly tenant: string;
  /** Whether the row is a sandbox beneath its live tenant. */
  readonly nested: boolean;
  /** Whether the row shows only as a matching sandbox's live tenant. */
  readonly dimmed: boolean;
  /** The badges, with their tones. */
  readonly badges: readonly { readonly key: TmTenantBadge; readonly tone: TmBadgeTone; readonly label: string }[];
  /** Why Connect is unavailable, or `null`. */
  readonly reason: string | null;
  /** The tenant's home. */
  readonly href: string;
  /** Connect's accessible name: "Connect to Acme". */
  readonly connectTo: string;
}

/**
 * The tenant picker at `/tenants`: the user's memberships, each with its
 * names, badges and Connect, in the tenantless shell.
 */
@Component({
  selector: 'tm-tenant-picker',
  imports: [RouterLink, TmAlert, TmBadge, TmButton, TmEmptyState, TmSearchField, TmSkeleton, TmTenantlessShell],
  providers: [provideTmPageScope()],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <tm-tenantless-shell>
      <div class="tm-tenant-picker">
        <!-- The heading, and the search from seven memberships. -->
        <div class="tm-tenant-picker__head">
          <h1 tmPageTitle tabindex="-1">{{ title() }}</h1>
          @if (searchable()) {
            <tm-search-field
              class="tm-tenant-picker__search"
              [ariaLabel]="searchLabel()"
              [(value)]="query"
              (search)="keepQuery($event)"
              (keydown.enter)="follow()"
            />
          }
        </div>

        <!-- The notice of a chosen tenant that can no longer be opened. -->
        @if (notice(); as notice) {
          <tm-alert class="tm-tenant-picker__notice" kind="info" live="polite">
            <span class="tm-tenant-picker__notice-text">
              {{ notice }}
              <button tmButton variant="ghost" size="sm" (click)="dismissNotice()">{{ dismissLabel() }}</button>
            </span>
          </tm-alert>
        }

        @if (view(); as rows) {
          <!-- The list, or the empty state of a user without memberships. -->
          @if (count() === 0) {
            <tm-empty-state class="tm-tenant-picker__empty" [heading]="emptyTitle()">
              {{ emptyMessage() }}
              <button tmButton tmEmptyStateActions (click)="signOut()">{{ signOutLabel() }}</button>
            </tm-empty-state>
          } @else {
            <ul class="tm-tenant-picker__list">
              @for (row of rows; track row.id) {
                <li
                  class="tm-tenant-picker__row"
                  [class.tm-tenant-picker__row--nested]="row.nested"
                  [class.tm-tenant-picker__row--dimmed]="row.dimmed"
                >
                  <!-- The names: the active language's in bold, then the others and the id. -->
                  <span class="tm-tenant-picker__names">
                    <span class="tm-tenant-picker__name" dir="auto" [attr.lang]="row.name.lang">{{ row.name.text }}</span>
                    <span class="tm-tenant-picker__more">
                      @for (other of row.others; track $index) {
                        <span dir="auto" [attr.lang]="other.lang">{{ other.text }}</span> ·
                      }
                      {{ row.tenant }}
                    </span>
                  </span>
                  <span class="tm-tenant-picker__badges">
                    @for (badge of row.badges; track badge.key) {
                      <tm-badge [tone]="badge.tone">{{ badge.label }}</tm-badge>
                    }
                  </span>
                  <!-- Connect: an in-app link, or a button disabled with its reason. -->
                  @if (row.reason; as reason) {
                    <button tmButton [disabledReason]="reason" [attr.aria-label]="row.connectTo">{{ connectLabel() }}</button>
                  } @else {
                    <a
                      tmButton
                      variant="primary"
                      [routerLink]="row.href"
                      [attr.aria-label]="row.connectTo"
                      (mouseenter)="preload(row.href)"
                      (focus)="preload(row.href)"
                      >{{ connectLabel() }}</a
                    >
                  }
                </li>
              }
            </ul>
            @if (rows.length === 0) {
              <p class="tm-tenant-picker__no-match">{{ noMatch() }}</p>
            }
          }
        } @else if (failure()) {
          <!-- The read failed and nothing is stored: the error state, with Retry; a line of its own while the error views cannot load. -->
          <ng-container #errorSlot />
          @if (errorViewsMissing()) {
            <p class="tm-tenant-picker__failed">
              {{ failedLabel() }}
              <button tmButton size="sm" (click)="retry()">{{ retryLabel() }}</button>
            </p>
          }
        } @else {
          <!-- Four rows with the final geometry until the read answers. -->
          <ul class="tm-tenant-picker__list" aria-busy="true">
            @for (index of skeletonRows; track index) {
              <li class="tm-tenant-picker__row" aria-hidden="true">
                <span class="tm-tenant-picker__names">
                  <tm-skeleton width="40%" />
                  <tm-skeleton width="60%" />
                </span>
                <tm-skeleton shape="block" width="88px" height="32px" />
              </li>
            }
          </ul>
        }
        <!-- The one live region, present from the first frame and empty until it has something to say. -->
        <p class="tm-tenant-picker__hidden" role="status">{{ announcement() }}</p>
      </div>
    </tm-tenantless-shell>
  `,
  styles: `
    :host {
      display: contents;
    }

    .tm-tenant-picker {
      display: flex;
      flex-direction: column;
      gap: var(--space-4);
      max-inline-size: 48rem;
      margin-inline: auto;
      padding: var(--space-6) var(--space-4);
      color: var(--text-body);
    }

    .tm-tenant-picker__head {
      display: flex;
      flex-wrap: wrap;
      align-items: center;
      justify-content: space-between;
      gap: var(--space-3);
    }

    .tm-tenant-picker__head h1 {
      margin: 0;
      color: var(--text-strong);
      font-size: var(--text-lg);
    }

    .tm-tenant-picker__search {
      inline-size: min(100%, 20rem);
    }

    .tm-tenant-picker__notice-text {
      display: flex;
      flex-wrap: wrap;
      align-items: center;
      justify-content: space-between;
      gap: var(--space-2);
    }

    /* Every row, the skeleton's included, has the same box. */
    .tm-tenant-picker__list {
      display: flex;
      flex-direction: column;
      margin: 0;
      padding: 0;
      list-style: none;
    }

    .tm-tenant-picker__row {
      display: grid;
      grid-template-columns: minmax(0, 1fr) auto auto;
      align-items: center;
      gap: var(--space-3);
      min-block-size: 64px;
      padding: var(--space-2) var(--space-3);
      border-block-end: var(--border-width) solid var(--border-subtle);
    }

    .tm-tenant-picker__row--nested {
      padding-inline-start: var(--space-8);
    }

    .tm-tenant-picker__row--dimmed {
      opacity: 0.6;
    }

    .tm-tenant-picker__names {
      display: grid;
      gap: var(--space-1);
      min-inline-size: 0;
    }

    .tm-tenant-picker__name {
      overflow: hidden;
      color: var(--text-strong);
      font-weight: var(--weight-semibold);
      text-overflow: ellipsis;
      white-space: nowrap;
    }

    .tm-tenant-picker__more {
      overflow: hidden;
      color: var(--text-muted);
      font-size: var(--text-sm);
      text-overflow: ellipsis;
      white-space: nowrap;
    }

    .tm-tenant-picker__badges {
      display: flex;
      flex-wrap: wrap;
      justify-content: flex-end;
      gap: var(--space-1);
    }

    .tm-tenant-picker__no-match {
      margin: 0;
      color: var(--text-muted);
    }

    /* The failure's own line, while the error views cannot load. */
    .tm-tenant-picker__failed {
      display: flex;
      flex-wrap: wrap;
      align-items: center;
      gap: var(--space-3);
      margin: 0;
    }

    /* Text that is read but not drawn. */
    .tm-tenant-picker__hidden {
      position: absolute;
      clip-path: inset(50%);
      inline-size: 1px;
      block-size: 1px;
      overflow: hidden;
      white-space: nowrap;
    }

    /* Below md the badges move under the names. */
    @media (width < 768px) {
      .tm-tenant-picker__row {
        grid-template-columns: minmax(0, 1fr) auto;
      }

      .tm-tenant-picker__badges {
        grid-row: 2;
        justify-content: flex-start;
      }
    }
  `,
})
export class TmTenantPicker {
  readonly #l10n = inject(TmLocalization);
  readonly #cache = inject(TmLaunchCache);
  readonly #session = inject(TmSession);
  readonly #urls = inject(TmUrls);
  readonly #router = inject(Router);
  readonly #route = inject(ActivatedRoute);
  readonly #lastTenantId = inject(TmClientStore).boot.read().lastTenantId;
  /** The search field, while it shows. */
  private readonly field = viewChild(TmSearchField);
  /** Where the error state renders, while it shows. */
  private readonly errorSlot = viewChild('errorSlot', { read: ViewContainerRef });
  // The query parameters, which carry the search text and the notice.
  readonly #params = signal<ParamMap>(this.#route.snapshot.queryParamMap);
  // Whether a read of the memberships runs, and how the last one failed.
  readonly #reading = signal(false);
  readonly #failure = signal<TmApiError | null>(null);
  // The error state's view, once the error presenter has described the failure.
  readonly #errorView = signal<TmErrorView | null>(null);
  // Whether the error views failed to load, so the picker shows its own line.
  readonly #errorViewsMissing = signal(false);
  // Whether the first frame has rendered, after which the live region speaks.
  readonly #rendered = signal(false);

  /** The rows of the skeleton. */
  protected readonly skeletonRows = [0, 1, 2, 3] as const;

  /** The heading. */
  protected readonly title = this.#l10n.ɵtext('tm.tenantless.picker.title');

  /** The search field's accessible name. */
  protected readonly searchLabel = this.#l10n.ɵtext('tm.tenantless.picker.search');

  /** The line shown while neither the list nor the error views could load. */
  protected readonly failedLabel = this.#l10n.ɵtext('tm.tenantless.picker.failed');

  /** Try again's label. */
  protected readonly retryLabel = this.#l10n.ɵtext('tm.errors.retry');

  /** Connect's label. */
  protected readonly connectLabel = this.#l10n.ɵtext('tm.tenantless.picker.connect');

  /** The notice's dismiss label. */
  protected readonly dismissLabel = this.#l10n.ɵtext('tm.tenantless.picker.notice.dismiss');

  /** The empty state's heading. */
  protected readonly emptyTitle = this.#l10n.ɵtext('tm.tenantless.picker.empty.title');

  /** Sign out's label. */
  protected readonly signOutLabel = this.#l10n.ɵtext('tm.tenantless.bar.signOut');

  /** The search text, which `?q=` starts. */
  protected readonly query = signal(this.#params().get('q') ?? '');

  /** The rows, in display order, or `undefined` before the memberships are known. */
  readonly #rows = computed(() => {
    const tenants = this.#cache.tenants();
    return tenants && tmTenantRows(tenants, this.#l10n.language(), this.#lastTenantId);
  });

  /** How many memberships the picker lists. */
  protected readonly count = computed(() => this.#rows()?.length ?? 0);

  /** Whether the search shows. */
  protected readonly searchable = computed(() => this.count() >= SEARCH_FROM);

  /** What the search shows; every row while the search is hidden. */
  readonly #search = computed(() => tmTenantSearch(this.#rows() ?? [], this.searchable() ? this.query() : ''));

  /** The rows as the template renders them, or `undefined` before the memberships are known. */
  protected readonly view = computed((): readonly TmRowView[] | undefined => {
    if (this.#rows() === undefined) {
      return undefined;
    }
    const text = (key: string, params?: Readonly<Record<string, unknown>>): string =>
      this.#l10n.ɵtext(`tm.tenantless.picker.${key}`, params)();
    return this.#search().shown.map(({ row, dimmed }) => ({
      id: row.membership.id,
      name: row.name,
      others: row.others,
      tenant: text('tenant', { id: String(row.membership.id) }),
      nested: row.nested,
      dimmed,
      badges: row.badges.map((key) => ({ key, tone: TONES[key], label: text(`badges.${key}`) })),
      reason: row.unavailable === null ? null : text(`unavailable.${row.unavailable}`),
      href: this.#urls.forTenant(row.membership.id).home(),
      connectTo: text('connectTo', { name: row.name.text }),
    }));
  });

  /** The empty state's message, which names the signed-in user's email. */
  protected readonly emptyMessage = computed(() =>
    this.#l10n.ɵtext('tm.tenantless.picker.empty.message', { email: this.#session.profile()?.email ?? '' })(),
  );

  /** The search's no-match message. */
  protected readonly noMatch = computed(() => this.#l10n.ɵtext('tm.tenantless.picker.noMatch', { query: this.query().trim() })());

  /** The tenant-not-found notice, or `null` when the URL carries none. */
  protected readonly notice = computed(() => {
    const params = this.#params();
    const from = Number(params.get('from'));
    if (params.get('notice') !== 'tenant-not-found' || !Number.isSafeInteger(from) || from <= 0) {
      return null;
    }
    const membership = this.#cache.tenants()?.find((tenant) => tenant.id === from);
    return membership === undefined
      ? this.#l10n.ɵtext('tm.tenantless.picker.notice.unnamed', { id: String(from) })()
      : this.#l10n.ɵtext('tm.tenantless.picker.notice.named', { name: tmTenantNames(membership, this.#l10n.language()).name.text })();
  });

  /** The failed read's error, shown while no list is stored. */
  protected readonly failure = this.#failure.asReadonly();

  /** Whether the error views failed to load. */
  protected readonly errorViewsMissing = this.#errorViewsMissing.asReadonly();

  /** Whether the skeleton shows: no list is stored and no read has failed. */
  readonly #loading = computed(() => this.#rows() === undefined && this.#failure() === null);

  /**
   * What the live region says: the loading line under the skeleton, the
   * no-match line, or the failure while the error views cannot load; empty
   * in the first frame, so the region is in place before its text changes.
   */
  protected readonly announcement = computed(() => {
    if (!this.#rendered()) {
      return '';
    }
    if (this.#loading()) {
      return this.#l10n.ɵtext('tm.tenantless.picker.loading')();
    }
    if (this.#rows() === undefined) {
      return this.#errorViewsMissing() ? this.failedLabel() : '';
    }
    return this.count() > 0 && this.view()?.length === 0 ? this.noMatch() : '';
  });

  /** Creates the picker, which reads the memberships again and preloads a tenant home once it has rendered. */
  constructor() {
    tmRegisterTenantlessStrings(this.#l10n);
    const destroyRef = inject(DestroyRef);

    // --- The query parameters: the search text and the notice ---
    const subscription = this.#route.queryParamMap.subscribe((params) => this.#params.set(params));
    destroyRef.onDestroy(() => subscription.unsubscribe());

    // --- The revalidation, after the first render (spec 0024 §14) ---
    afterNextRender(() => {
      // The live region speaks from the next change detection, never in the
      // render that inserts it: a region that arrives with its text is
      // unreliably announced.
      queueMicrotask(() => this.#rendered.set(true));
      this.#read();
    });

    // --- A tenant home's chunks, at idle once the first frame shows a Connect (spec 0024 §4.3, §4.4) ---
    // A hover's preload waits for idle, which a click can beat, and a first
    // entry whose tenant shell is still loading misses the click's frame.
    // Every tenant's home needs the same chunks, so one preload serves every
    // Connect.
    let warmed = false;
    effect(() => {
      const row = this.#rendered() ? this.#rows()?.find((candidate) => candidate.unavailable === null) : undefined;
      if (row !== undefined && !warmed) {
        warmed = true;
        this.#urls.preload(this.#urls.forTenant(row.membership.id).home());
      }
    });

    // --- The error state, from the lazily loaded error views; a line of its own when they fail to load ---
    const telemetry = inject(TmTelemetry);
    const errorViews = inject(TM_SHELL_ERROR_VIEWS);
    effect((onCleanup) => {
      const slot = this.errorSlot();
      const failure = this.#failure();
      if (slot === undefined || failure === null) {
        return;
      }
      let live = true;
      onCleanup(() => {
        live = false;
        slot.clear();
        this.#errorView.set(null);
        this.#errorViewsMissing.set(false);
      });
      errorViews().then(
        ({ TmErrorPresenter, TmErrorState }) => {
          if (live) {
            const view = slot.injector.get(TmErrorPresenter).describe(failure, { kind: 'read' });
            const state = slot.createComponent(TmErrorState);
            state.setInput('view', view);
            state.instance.retry.subscribe(() => this.#read());
            this.#errorView.set(view);
          }
        },
        (error: unknown) => {
          // Offline, or after a deploy removed the chunk: say so, and offer Try again.
          telemetry.error(error, { source: 'tm-tenant-picker' });
          if (live) {
            this.#errorViewsMissing.set(true);
          }
        },
      );
    });

    // --- Mod+K focuses the search, which the picker owns outside a tenant ---
    inject(TmShortcuts).register({
      id: 'tm.launcher.open',
      keys: 'Mod+K',
      labelKey: 'tm.tenantless.picker.search',
      scope: 'global',
      run: () => {
        const field = this.field();
        if (field === undefined) {
          return false;
        }
        field.focus({ select: true });
        return undefined;
      },
    });

    // --- Telemetry: once, when the list is first known ---
    let shown = false;
    effect(() => {
      const rows = this.#rows();
      if (rows !== undefined && !shown) {
        shown = true;
        const notice = untracked(this.notice) !== null;
        telemetry.event('tm.shell.picker-shown', { count: tmTenantCountBucket(rows.length), notice });
      }
    });

    // --- The page context (spec 0027 §14) ---
    inject(TmPageContexts).register({
      kind: 'custom',
      routeTemplate: '/tenants',
      title: this.title,
      // Loading while a read runs or the skeleton shows, never ready before the list is known.
      status: computed(() =>
        this.#reading() || this.#loading() ? 'loading' : this.#failure() !== null && this.#rows() === undefined ? 'error' : 'ready',
      ),
      state: computed(() => ({ count: this.count(), query: this.searchable() ? this.query() : '' })),
      visible: computed((): TmVisibleData | undefined => {
        const shown = this.#search().shown;
        return this.#rows() === undefined
          ? undefined
          : {
              columns: [
                { path: 'Name', label: 'Name' },
                { path: 'Id', label: 'Id' },
              ],
              rows: shown.slice(0, CONTEXT_ROWS).map(({ row }) => ({ id: row.membership.id, cells: { Name: row.name.text, Id: row.membership.id } })),
              total: shown.length,
              truncated: shown.length > CONTEXT_ROWS,
            };
      }),
      errors: computed((): readonly TmContextError[] => {
        const failure = this.#failure();
        return failure === null || this.#rows() !== undefined
          ? []
          : [
              {
                source: 'request',
                code: failure.code || failure.kind,
                message: this.#errorView()?.title() ?? '',
                ...(failure.traceId ? { traceId: failure.traceId } : {}),
              },
            ];
      }),
      actions: signal([]),
    });
  }

  /**
   * Keeps the search text in `?q=`, in place of the current entry.
   *
   * @param text - The search text.
   */
  protected keepQuery(text: string): void {
    void this.#router.navigate([], {
      relativeTo: this.#route,
      queryParams: { q: text === '' ? null : text },
      queryParamsHandling: 'merge',
      replaceUrl: true,
    });
  }

  /** Follows the only enabled match while searching, on Enter. */
  protected follow(): void {
    const enabled = this.#search().matches.filter((row: TmTenantRow) => row.unavailable === null);
    if (this.query().trim() !== '' && enabled.length === 1) {
      void this.#router.navigateByUrl(this.#urls.forTenant(enabled[0].membership.id).home());
    }
  }

  /** Drops the notice and the tenant it names from the URL. */
  protected dismissNotice(): void {
    void this.#router.navigate([], {
      relativeTo: this.#route,
      queryParams: { notice: null, from: null },
      queryParamsHandling: 'merge',
      replaceUrl: true,
    });
  }

  /**
   * Loads, at idle, what the tenant's home needs, as hover and focus on Connect promise it.
   *
   * @param href - The tenant's home.
   */
  protected preload(href: string): void {
    this.#urls.preload(href);
  }

  /** Signs out; a failure has shown its own toast. */
  protected signOut(): void {
    this.#session.signOut().catch(() => undefined);
  }

  /** Reads the memberships again, from the line shown while the error views cannot load. */
  protected retry(): void {
    this.#read();
  }

  /** Reads the memberships again; a failure shows the error state while no list is stored. */
  #read(): void {
    this.#failure.set(null);
    this.#reading.set(true);
    void this.#cache.revalidateUser().then((failure) => {
      this.#reading.set(false);
      // The app's end cancels the read; that is no failure to show.
      this.#failure.set(failure?.kind === 'cancelled' ? null : failure);
    });
  }
}

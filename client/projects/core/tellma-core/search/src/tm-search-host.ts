// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { LiveAnnouncer } from '@angular/cdk/a11y';
import { Location, NgComponentOutlet } from '@angular/common';
import {
  ChangeDetectionStrategy,
  Component,
  computed,
  DestroyRef,
  DOCUMENT,
  effect,
  inject,
  Injector,
  input,
  linkedSignal,
  resource,
  runInInjectionContext,
  signal,
  untracked,
  type InputSignal,
  type OnInit,
  type Signal,
  type Type,
  type WritableSignal,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { ActivatedRoute, NavigationEnd, Router, RouterLink } from '@angular/router';
import { CircleAlert, Columns3, Ellipsis, Inbox, Plus, RefreshCw, SearchX } from 'lucide';
import {
  TmApi,
  TmApiError,
  TmDisplayNames,
  TmEntities,
  TmLocalization,
  TmPageActions,
  TmPageContexts,
  TmPermissions,
  TmPreferences,
  TmTelemetry,
  TmUrls,
  type TmActionResult,
  type TmColumnInput,
  type TmColumnPlan,
  type TmContextError,
  type TmEntityDeclarationInput,
  type TmEntityDescriptor,
  type TmId,
  type TmJson,
  type TmJsonObject,
  type TmJsonSchema,
  type TmLazyComponent,
  type TmPlanRow,
  type TmSearchContext,
  type TmSearchPageActionDeclaration,
  type TmSearchQuery,
  type TmSelectPlan,
  type TmVisibleData,
  type ɵTmRouteDataInternal,
  ɵtmRoutedTenant,
} from '@tellma/core';
import { TmFormats, TmValue, tmGridColumnFor, type TmGridColumnConfig } from '@tellma/core/entity';
import { tmPageStateKey } from '@tellma/core/memory';
import { TmBreakpoints } from '@tellma/core-ui';
import { TmButton } from '@tellma/core-ui/button';
import {
  TmGrid,
  TmGridColumn,
  TmGridDisplayDef,
  TmGridEmptyDef,
  TmGridHeaderAction,
  TmGridHeaderDef,
  TmGridLoadingDef,
  TmGridRowEndHeaderDef,
  type TmGridRowActivation,
  type TmGridSort,
} from '@tellma/core-ui/grid';
import { TmIcon, TmIconRegistry } from '@tellma/core-ui/icon';
import { TmMenu, TmMenuTrigger, type TmMenuItem } from '@tellma/core-ui/menu';
import { TmPageHeader } from '@tellma/core-ui/page-header';
import { TmPager } from '@tellma/core-ui/pager';
import { TmProgressBar } from '@tellma/core-ui/progress';
import { TmSkeleton } from '@tellma/core-ui/skeleton';

import { tmSearchCanonical, tmSearchQuiet, tmSearchUrl } from './tm-search-canonical';
import { tmSearchResize, tmSearchUpdate, type TmSearchTransition } from './tm-search-change';
import { ɵTM_SEARCH_CONTEXT } from './tm-search-context';
import { tmSearchDiagnose } from './tm-search-diagnostics';
import {
  TM_SEARCH_EXPRESSION_LABEL,
  tmSearchColumnKey,
  tmSearchEffective,
  tmSearchIsExpression,
  tmSearchIsTree,
  tmSearchMaxPage,
  tmSearchPageSizes,
  tmSearchTilePaths,
  tmSearchTreeKeys,
  type TmSearchDeclaration,
  type TmSearchMode,
  type TmSearchSetting,
  type TmSearchState,
  type TmSearchWidth,
} from './tm-search-model';
import { tmSearchColumnsKey, tmSearchPageSizeKey, tmSearchViewKey } from './tm-search-preferences';
import {
  tmSearchApiError,
  tmSearchArithmetic,
  tmSearchRereads,
  tmSearchTotal,
  type TmSearchFailure,
  type TmSearchOutcome,
  type TmSearchReadKey,
  type TmSearchReadSource,
  type TmSearchResult,
  type TmSearchWire,
} from './tm-search-read';
import { tmSearchPlan, tmSearchRequest, type TmSearchRequest } from './tm-search-request';
import { tmSearchColumnsOf, tmSearchResolve, type TmSearchOpening } from './tm-search-resolve';
import { TmSearchState as TmSearchStateView, type TmSearchRegionState } from './tm-search-state';
import { ɵTM_SEARCH_STRINGS_EN } from './tm-search-strings';

// The search host (spec 0030 §3, §5, §7, §13.1, §13.4, §14, §19): the page
// of an entity's search route. It resolves the query state from the URL,
// plans its columns, and reads its rows through one resource keyed by the
// canonical query and a refresh generation, so a new key aborts the read in
// flight and leaving the page aborts it too. The held rows stay on screen
// under the progress bar while a new read runs; a failed read replaces them
// with its state, but for an offline re-read of the same query. The grid
// shows the plan's columns, links the display name and the navigations,
// and orders by one column; the toolbar holds Create, the declaration's
// page actions, Refresh and the pager. The host publishes the page's
// context and its own page actions, and provides the context its parts
// read through `injectTmSearch()`.

/** What the host holds of the reads: the last result, the last failure, and whether rows were kept through an offline re-read. */
interface TmSearchHeld {
  /** The result on screen; `null` for none. */
  readonly result: TmSearchResult | null;
  /** The failure of the last read; `null` after a read that answered. */
  readonly failure: TmSearchFailure | null;
}

/** One column as the grid binds it. */
interface TmSearchGridColumn {
  /** The plan's column. */
  readonly plan: TmColumnPlan;
  /** The grid column's inputs, from the column's type. */
  readonly config: Partial<TmGridColumnConfig>;
  /** The header text: the slash label, or a user's expression, then the zone suffix. */
  readonly header: Signal<string>;
  /** Whether the column is a user's expression column, headed by its text. */
  readonly expression: boolean;
  /** The width in pixels: the declared width, else the type's. */
  readonly width: number | undefined;
  /** Whether a header click orders by the column. */
  readonly sortable: boolean;
  /**
   * How a cell renders: static text, the row's link (the display name's
   * column), a typed value (a navigation's link, a thumbnail, a file), or
   * the declared display component.
   */
  readonly cell: 'static' | 'link' | 'value' | 'display';
  /** The declared display component's loader, for a `display` cell. */
  readonly display: TmLazyComponent | null;
}

/** No result and no failure: what shows before the first read lands. */
const NOTHING_HELD: TmSearchHeld = { result: null, failure: null };

/** How many rows the first load's skeleton draws. */
const SKELETON_ROWS = 12;

/** How many of the selected ids the page context carries. */
const CONTEXT_SELECTED_IDS = 200;

/** How many rows and columns the page context's visible data carries. */
const VISIBLE_ROWS = 200;
const VISIBLE_COLUMNS = 50;

/** The icons the page and its states name. */
const ICONS = { 'circle-alert': CircleAlert, 'columns-3': Columns3, inbox: Inbox, plus: Plus, 'refresh-cw': RefreshCw, 'search-x': SearchX };

/** The arguments of `search.query`, as a JSON Schema. */
const QUERY_PARAMS: TmJsonSchema = {
  type: 'object',
  additionalProperties: false,
  properties: {
    q: { type: 'string' },
    tab: { type: 'string' },
    filter: { type: 'string' },
    columnFilters: { type: 'object', additionalProperties: { type: 'string' } },
    inactive: { type: 'boolean' },
    custom: { type: 'object', additionalProperties: { type: 'string' } },
    order: { type: 'string' },
    page: { type: 'integer', minimum: 1 },
  },
};

/** The arguments of `search.open`, as a JSON Schema. */
const OPEN_PARAMS: TmJsonSchema = {
  type: 'object',
  additionalProperties: false,
  required: ['id'],
  properties: { id: { type: ['integer', 'string'] } },
};

/**
 * The component a lazy component's module holds.
 *
 * @param load - The lazy component.
 * @returns The component.
 */
async function componentOf(load: TmLazyComponent): Promise<Type<unknown>> {
  const loaded = await load();
  return typeof loaded === 'function' ? loaded : loaded.default;
}

/**
 * An entity's search page: the title and the toolbar over the grid of its
 * rows, each state of the read in the grid's place, and the pager. The
 * route host renders it for an entity's search route and a drill-through
 * list, with the routed entity's name.
 *
 * @example
 * ```html
 * <tm-search-host entity="gl.Center" />
 * ```
 */
@Component({
  selector: 'tm-search-host',
  imports: [
    NgComponentOutlet,
    RouterLink,
    TmButton,
    TmGrid,
    TmGridColumn,
    TmGridDisplayDef,
    TmGridEmptyDef,
    TmGridHeaderAction,
    TmGridHeaderDef,
    TmGridLoadingDef,
    TmGridRowEndHeaderDef,
    TmIcon,
    TmMenu,
    TmMenuTrigger,
    TmPageHeader,
    TmPager,
    TmProgressBar,
    TmSearchStateView,
    TmSkeleton,
    TmValue,
  ],
  providers: [{ provide: ɵTM_SEARCH_CONTEXT, useFactory: (): TmSearchContext => inject(TmSearchHost).ɵcontext }],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <!-- The header: the title, then Create and the page actions at the toolbar's start, Refresh and the pager at its end. -->
    <tm-page-header>
      <h1 tmPageTitle tabindex="-1">{{ title() }}</h1>

      <!-- Create: a link while the user may create, else a button that says why not; icon-only in the narrowest form.
           Each block has one root element, which the header projects by its selector. -->
      @if (createLink(); as action) {
        <a tmButton tmPageToolbar variant="primary" data-tm-create [routerLink]="action.href" [attr.aria-label]="compact() ? action.label : null">
          @if (compact()) {
            <tm-icon [svg]="glyphs.create" />
          } @else {
            {{ action.label }}
          }
        </a>
      }
      @if (createRefused(); as action) {
        <button
          tmButton
          tmPageToolbar
          type="button"
          variant="primary"
          data-tm-create
          [disabledReason]="action.reason"
          [attr.aria-label]="compact() ? action.label : null"
        >
          @if (compact()) {
            <tm-icon [svg]="glyphs.create" />
          } @else {
            {{ action.label }}
          }
        </button>
      }

      <!-- The declaration's primary page actions, after Create. -->
      @for (action of primaryActions(); track action.id) {
        <button tmButton tmPageToolbar type="button" [attr.data-tm-action]="action.id" [pending]="running().has(action.id)" (click)="invoke(action.id)">
          @if (action.icon) {
            <tm-icon [name]="action.icon" />
          }
          {{ action.label }}
        </button>
      }

      <!-- More: the menu page actions; in the narrowest form, every declared one. The menu's code
           loads the first time there are any, so a page with none never downloads it. -->
      <span tmPageToolbar class="tm-search-host__more">
        @defer (when menuActions().length > 0) {
          @if (menuActions().length > 0) {
            <button tmButton type="button" variant="ghost" data-tm-more [tmMenuTriggerFor]="more" [attr.aria-label]="text.moreActions()">
              <tm-icon [svg]="glyphs.more" />
            </button>
          }
          <tm-menu #more [items]="menuActions()" [aria-label]="text.moreActions()" />
        }
      </span>

      <!-- Refresh re-reads the query; the pager ends the toolbar. Neither renders while the first load's
           skeleton shows: the pager's status takes its width only once the total is known, so the end slot
           would widen and move. The empty slot draws no box, and the pair arrives with the first answer in
           its final geometry. Each block has one root element, which the header projects by its selector. -->
      @if (!firstLoad()) {
        <button
          tmButton
          tmPageToolbarEnd
          type="button"
          variant="ghost"
          data-tm-refresh
          [attr.aria-label]="text.refresh()"
          [title]="text.refresh()"
          (click)="invoke('search.refresh')"
        >
          <tm-icon [svg]="glyphs.refresh" />
        </button>
      }
      @if (!firstLoad()) {
        <tm-pager
          tmPageToolbarEnd
          mode="range"
          [page]="paging().page"
          [pageSize]="paging().size"
          [pageSizes]="paging().sizes"
          [sizeLabel]="paging().sizeLabel"
          [maxPage]="paging().maxPage"
          [disabled]="paging().disabled"
          [total]="total()"
          [totalIsLowerBound]="capped()"
          (pageChange)="goTo($event)"
          (pageSizeChange)="setSize($event)"
        />
      }
    </tm-page-header>

    <!-- The results region: busy while a read runs, the progress bar over the rows it keeps. -->
    <div class="tm-search-host__region" [attr.aria-busy]="loading() ? 'true' : null" (pointerover)="hover($event)">
      <tm-progress-bar class="tm-search-host__progress" [active]="loading() || (hatched() && body() === null)" [label]="text.loading()" />

      @if (hatched()) {
        <!-- The declaration's own results component, which reads the page through its context. -->
        @if (body(); as component) {
          <ng-container *ngComponentOutlet="component" />
        }
      } @else {
        <tm-grid
          class="tm-search-host__grid"
          [gridId]="gridId()"
          [contentKey]="contentKey()"
          [data]="rows()"
          [rowId]="rowId"
          [loading]="firstLoad()"
          [rowHeaders]="false"
          [rowActivation]="activation()"
          selectable
          [selectedIds]="selection()"
          (selectedIdsChange)="selected($event)"
          [(activeRowId)]="highlight"
          [orderBy]="sort()"
          (orderByChange)="orderBy($event)"
          (rowActivate)="activate($event)"
        >
          @for (column of gridColumns(); track column.plan.key) {
            <tm-grid-column
              [key]="column.config.key"
              [type]="column.config.type ?? 'text'"
              [header]="column.header()"
              [format]="column.config.format"
              [formatParts]="column.config.formatParts"
              [options]="column.config.options"
              [optionLabel]="column.config.optionLabel"
              [optionValue]="column.config.optionValue"
              [readonly]="true"
              [width]="column.width"
              [align]="column.config.align"
              [minDecimals]="column.config.minDecimals"
              [maxDecimals]="column.config.maxDecimals"
              [sortable]="column.sortable"
            >
              <!-- A user's expression column is headed by its text, as code. -->
              @if (column.expression) {
                <span *tmGridHeader="let header" class="tm-search-host__expression" [title]="header">
                  <span aria-hidden="true">ƒ</span>
                  <bdi dir="ltr">{{ header }}</bdi>
                </span>
              }

              @switch (column.cell) {
                <!-- The display name: the row's link. -->
                @case ('link') {
                  <ng-template tmGridDisplay let-row="row">
                    @if (targetOf(row); as target) {
                      <a class="tm-search-host__link" tabindex="-1" [routerLink]="target" [state]="openState()">{{ cellText(column.plan, row) }}</a>
                    } @else {
                      {{ cellText(column.plan, row) }}
                    }
                  </ng-template>
                }
                <!-- A navigation's link, a thumbnail, a file: the typed value. -->
                @case ('value') {
                  <tm-value *tmGridDisplay="let value; row as row" surface="grid" [property]="column.plan" [value]="row" />
                }
                <!-- The declared cell component, once loaded; its text until then. -->
                @case ('display') {
                  <ng-template tmGridDisplay let-value let-row="row">
                    @if (displayOf(column); as cell) {
                      <ng-container *ngComponentOutlet="cell; inputs: { value, row, column: column.plan }" />
                    } @else {
                      {{ cellText(column.plan, row) }}
                    }
                  </ng-template>
                }
              }
            </tm-grid-column>
          }

          <!-- The Columns button heads the row-end column in every state. -->
          <ng-template tmGridRowEndHeader>
            <button tmGridHeaderAction type="button" data-tm-columns [attr.aria-label]="text.chooseColumns()" [title]="text.chooseColumns()">
              <tm-icon [svg]="glyphs.columns" />
            </button>
          </ng-template>

          <!-- The first load: skeleton rows in the grid's place, under its header. -->
          <div *tmGridLoading class="tm-search-host__skeleton" data-tm-skeleton>
            @for (row of skeletonRows; track row) {
              <div class="tm-search-host__skeleton-row">
                @for (column of gridColumns(); track column.plan.key) {
                  <span class="tm-search-host__skeleton-cell" [style.inline-size.px]="column.width ?? 160">
                    <tm-skeleton />
                  </span>
                }
              </div>
            }
          </div>

          <!-- No rows: the region's state in their place, under the header. -->
          <div *tmGridEmpty class="tm-search-host__state">
            @if (region(); as shown) {
              <tm-search-state [state]="shown" (clear)="clear($event)" (retry)="retry()" />
            }
          </div>
        </tm-grid>
      }
    </div>
  `,
  styleUrl: './tm-search-host.css',
})
export class TmSearchHost implements OnInit {
  readonly #injector = inject(Injector);
  readonly #destroyRef = inject(DestroyRef);
  readonly #api = inject(TmApi);
  readonly #entities = inject(TmEntities);
  readonly #permissions = inject(TmPermissions);
  readonly #preferences = inject(TmPreferences);
  readonly #l10n = inject(TmLocalization);
  readonly #telemetry = inject(TmTelemetry);
  readonly #names = inject(TmDisplayNames);
  readonly #urls = inject(TmUrls);
  readonly #formats = inject(TmFormats);
  readonly #router = inject(Router);
  readonly #location = inject(Location);
  readonly #route = inject(ActivatedRoute);

  /** The page's path, such as `/17/customers`. */
  readonly #path =
    '/' +
    this.#route.snapshot.pathFromRoot
      .flatMap((route) => route.url)
      .map((segment) => segment.path)
      .join('/');

  // The page's own tenant's URL builders: the app may navigate out of the
  // tenant while the page still shows, and its links keep their tenant. The
  // page routes under a tenant, the first segment of its path.
  readonly #tenantUrls = this.#urls.forTenant(ɵtmRoutedTenant(this.#path)!);

  readonly #announcer = inject(LiveAnnouncer);
  readonly #window = inject(DOCUMENT).defaultView;
  readonly #breakpoints = inject(TmBreakpoints);
  readonly #contexts = inject(TmPageContexts);

  /** The entity's name, such as `gl.Center`. */
  readonly entity: InputSignal<string> = input.required<string>();

  // ---------------------------------------------------------------------------
  // The setting: where the page runs, the entity, the width, the preferences
  // ---------------------------------------------------------------------------

  /** The route's data, read once: the page's mode and its context's template. */
  readonly #data = this.#route.snapshot.data as ɵTmRouteDataInternal;

  /** Where the page runs: a drill-through list on the list route, else the entity's search page. */
  readonly #mode: TmSearchMode = this.#data.ɵentity === 'list' ? 'list' : 'page';

  /** The routed segment, such as `centers` or `sales-invoice-lines`. */
  readonly #segment: string | undefined = this.#route.snapshot.params['segment'] as string | undefined;

  /** The entity's descriptor, rebuilt when the language, the permissions or the settings move. */
  readonly #descriptor = computed<TmEntityDescriptor>(() => this.#entities.get(this.entity()));

  /** The entity's merged declaration, as far as the page reads it. */
  readonly #declared = computed(() => (this.#entities.declaration(this.entity()) ?? {}) as Partial<TmEntityDeclarationInput<string>>);

  /** The declaration members the query model reads. */
  readonly #declaration = computed<TmSearchDeclaration>(() => {
    const { columns, orderBy, tile, filters, tabs } = this.#declared();
    return { columns, orderBy, tile, filters, tabs } as TmSearchDeclaration;
  });

  /** The width class: narrow below `md`. */
  readonly #width = computed<TmSearchWidth>(() => (this.#breakpoints.atLeast('md')() ? 'wide' : 'narrow'));

  /** The entity's three search preferences. */
  readonly #preferenceSignals = computed(() => {
    const entity = this.#descriptor().name;
    return {
      columns: this.#preferences.get(tmSearchColumnsKey(entity)),
      view: this.#preferences.get(tmSearchViewKey(entity)),
      pageSize: this.#preferences.get(tmSearchPageSizeKey(entity)),
    };
  });

  /** The setting the query model reads. */
  readonly #setting = computed<TmSearchSetting>(() => {
    const preferences = this.#preferenceSignals();
    return {
      mode: this.#mode,
      descriptor: this.#descriptor(),
      declaration: this.#declaration(),
      width: this.#width(),
      preferences: { columns: preferences.columns(), view: preferences.view(), pageSize: preferences.pageSize() },
      ...(this.#mode === 'list' && this.#segment !== undefined && { segment: this.#segment }),
      injector: this.#injector,
      urls: this.#tenantUrls,
    };
  });

  // ---------------------------------------------------------------------------
  // The query state, its canonical URL, its plan and its request
  // ---------------------------------------------------------------------------

  /** The query state, resolved from the address when the page opens and changed by the page's controls. */
  readonly #state = linkedSignal<string, TmSearchState>({
    source: this.entity,
    computation: () => untracked(() => tmSearchResolve(this.#setting(), this.#opening('open')).state),
  });

  /** The state's canonical URL: empty in picker mode and for a list's invalid payload. */
  readonly #canonical = computed(() => tmSearchUrl(this.#setting(), this.#state()));

  /**
   * What identifies the state's rows: the canonical URL's path and the
   * canonical query, less the shown columns and the view, which change what
   * shows of the rows but not which rows. Whether a changed column set needs
   * the rows again is the select comparison's to decide.
   */
  readonly #identity = computed(() => {
    const state = this.#state();
    // A member set to undefined drops out of the JSON.
    const rows = { ...state.query, cols: undefined, view: undefined };
    const path = this.#canonical().split(/[?#]/)[0] ?? '';
    return JSON.stringify({ path, query: rows, list: state.listFilter ?? null });
  });

  /** The shown columns, by identity: a change of anything else keeps them. */
  readonly #columns = computed(() => this.#state().columns);

  /**
   * The select plan, built once per change of the columns, and again when
   * the descriptor rebuilds; nothing else in the state or the setting
   * builds it. It is built quietly: the host reports its drops once each.
   */
  readonly #plan = computed<TmSelectPlan>(() => {
    const columns = this.#columns();
    const setting = { ...untracked(this.#setting), descriptor: this.#descriptor(), declaration: this.#declaration() };
    return tmSearchPlan(tmSearchQuiet(setting), { ...untracked(this.#state), columns });
  });

  /** The rows request; `null` for a list whose payload is not valid, which reads nothing. */
  readonly #request = computed<TmSearchRequest | null>(() => {
    const state = this.#state();
    return state.payloadInvalid ? null : tmSearchRequest(this.#setting(), state, this.#plan());
  });

  // ---------------------------------------------------------------------------
  // The read: its key, its resource and what the host holds of it
  // ---------------------------------------------------------------------------

  /** The refresh generation, which Refresh, Try again and a reconnect bump. */
  readonly #generation = signal(0);

  /** What started the next read, for its timing. */
  #source: TmSearchReadSource = 'load';

  /**
   * The read's key: a new one for a new identity (another page, or a query
   * that picks other rows) or a new generation, and for a rebuilt request
   * the held one cannot answer (a column whose terms the select gains, a
   * language switch under a text order, a moved permission); otherwise the
   * held key, so a column removal, a view toggle or a scalar order reads
   * nothing.
   */
  readonly #key = linkedSignal<
    { readonly identity: string; readonly cols: string; readonly request: TmSearchRequest | null; readonly generation: number },
    TmSearchReadKey | null
  >({
    source: () => ({
      identity: this.#identity(),
      cols: JSON.stringify(this.#state().query.cols ?? null),
      request: this.#request(),
      generation: this.#generation(),
    }),
    computation: (next, previous) => {
      if (next.request === null) {
        return null;
      }
      const held = previous?.value ?? null;
      const fresh =
        held === null || next.generation !== previous!.source.generation || next.identity !== held.canonical || tmSearchRereads(held.request, next.request);
      if (!fresh) {
        return held;
      }
      // A rebuilt request over the same query re-reads as a refresh does.
      const source = held !== null && next.identity === held.canonical && next.generation === previous!.source.generation ? 'refresh' : this.#source;
      return { canonical: next.identity, cols: next.cols, request: next.request, generation: next.generation, source };
    },
  });

  /** The read, from start to abort. */
  readonly #read = resource({
    params: () => this.#key() ?? undefined,
    loader: ({ params, abortSignal }) => this.#load(params, abortSignal),
  });

  /** The last result and the last failure, held while a new read runs. */
  readonly #held = linkedSignal<TmSearchOutcome | undefined, TmSearchHeld>({
    source: () => this.#read.value(),
    computation: (outcome, previous) => {
      const held = previous?.value ?? NOTHING_HELD;
      if (outcome === undefined || (!outcome.ok && outcome.error.kind === 'cancelled')) {
        return held;
      }
      if (outcome.ok) {
        return { result: outcome, failure: null };
      }
      // An offline re-read of the rows on screen keeps them, the banner may lag: the same rows,
      // and columns they can fill. A select that gains a term keeps them only for the same shown
      // columns (a re-granted column, spec 0030 §5.4); an added column would show empty cells
      // that look like real empty values. With the same rows, the request differs from the held
      // one in its select alone, so the re-read rule says whether the select gained a term.
      const result = held.result;
      if (
        outcome.error.kind === 'offline' &&
        result !== null &&
        result.key.canonical === outcome.key.canonical &&
        (result.key.cols === outcome.key.cols || !tmSearchRereads(result.key.request, outcome.key.request))
      ) {
        return { result, failure: outcome };
      }
      return { result: null, failure: outcome };
    },
  });

  /** Whether a read runs. */
  protected readonly loading = computed(() => this.#read.isLoading());

  /** The rows on screen: the held result read through the current plan. */
  readonly #rows = computed<readonly TmPlanRow[]>(() => {
    const result = this.#held().result;
    return result === null ? [] : this.#plan().rows(result.set);
  });

  /** The rows on screen, by id. */
  readonly #byId = computed(() => new Map(this.#rows().map((row) => [row.id, row])));

  /** The ancestors of a search tree's matches. */
  readonly #ancestors = computed<readonly TmPlanRow[]>(() => {
    const ancestors = this.#held().result?.ancestors;
    return ancestors ? this.#plan().rows(ancestors) : [];
  });

  /** The total, `null` while unknown. */
  readonly #total = computed(() => this.#held().result?.total ?? null);

  /** Whether the total is a lower bound. */
  readonly #capped = computed(() => this.#held().result?.capped ?? false);

  /** The read's state, as the context and the page context publish it. */
  readonly #status = computed<'loading' | 'ready' | 'error'>(() => {
    if (this.#state().payloadInvalid) {
      return 'error';
    }
    if (this.#read.isLoading()) {
      return 'loading';
    }
    return this.#held().failure === null ? 'ready' : 'error';
  });

  /** The error the last read failed with. */
  readonly #error = computed(() => this.#held().failure?.error ?? null);

  // ---------------------------------------------------------------------------
  // The selection, the highlight and the targets
  // ---------------------------------------------------------------------------

  /** The selected rows' ids, across pages. */
  protected readonly selection = signal<ReadonlySet<TmId>>(new Set());

  /** The highlighted row's id. */
  protected readonly highlight: WritableSignal<TmId | null> = signal<TmId | null>(null);

  /** The target of a row: its declared link, else its details page while the user may open it; `null` for none. */
  readonly #targetOf = computed(() => {
    const entity = this.#descriptor().name;
    const rowLink = this.#declared().rowLink;
    const details = this.#permissions.allows(entity, 'details')();
    const cache = new WeakMap<TmPlanRow, string | null>();
    return (row: TmPlanRow): string | null => {
      let target = cache.get(row);
      if (target === undefined) {
        // A declared link runs in the host's injection context, so it may inject the URLs.
        target = rowLink
          ? untracked(() => runInInjectionContext(this.#injector, () => rowLink(row)))
          : details
            ? this.#tenantUrls.details(entity, row.id)
            : null;
        cache.set(row, target);
      }
      return target;
    };
  });

  /** Whether rows open: a declared link, or the details page the user may open. */
  protected readonly activation = computed(() =>
    this.#mode !== 'picker' && (this.#declared().rowLink !== undefined || this.#permissions.allows(this.#descriptor().name, 'details')()) ? 'row' : 'none',
  );

  // ---------------------------------------------------------------------------
  // The view: the title, the toolbar, the grid's binding, the region's state
  // ---------------------------------------------------------------------------

  /** The page's title: the entity's plural label. */
  protected readonly title = computed(() => this.#entities.label(this.entity(), true)());

  /** The grid's identity, so the widths survive navigation and a picker never shares a page's. */
  protected readonly gridId = computed(() => `${this.#mode === 'list' ? 'list' : 'search'}.${this.#descriptor().name}`);

  /** The grid's content key: the identity of the rows on screen, which changes when another result lands. */
  protected readonly contentKey = computed(() => this.#held().result?.key.canonical ?? '');

  /** The rows the grid binds: none while a failure or a first load shows. */
  protected readonly rows = this.#rows;

  /** A row's id. */
  protected readonly rowId = (row: TmPlanRow): TmId => row.id;

  /** Whether the toolbar takes its narrowest form, below `sm`. */
  protected readonly compact = computed(() => !this.#breakpoints.atLeast('sm')());

  /** The skeleton rows of a first load. */
  protected readonly skeletonRows = Array.from({ length: SKELETON_ROWS }, (_, index) => index);

  /** The columns as the grid binds them. */
  protected readonly gridColumns = computed<readonly TmSearchGridColumn[]>(() => {
    const plan = this.#plan();
    const { displayName } = this.#descriptor();
    const names = new Set([displayName.source, ...(displayName.prefix === undefined ? [] : [displayName.prefix])].map((key) => key.toLowerCase()));
    let linked = false;
    return plan.columns.map((column) => {
      const config = tmGridColumnFor(column, { injector: this.#injector, rows: 'plan' });
      const input = typeof column.input === 'object' ? column.input : null;
      const display = input !== null && 'display' in input && input.display !== undefined ? input.display : null;
      const expression = tmSearchIsExpression(column.input) && column.input.labelKey === TM_SEARCH_EXPRESSION_LABEL;
      // The first shown column of the display name is the row's link.
      const link = !linked && names.has(column.key.toLowerCase()) && column.kind !== 'expression';
      linked ||= link;
      const zone = this.#formats.zoneSuffix(column);
      return {
        plan: column,
        config,
        header: computed(() => (expression ? column.key : column.label()) + zone()),
        expression,
        width: (input !== null && 'width' in input ? input.width : undefined) ?? config.width,
        sortable: column.kind !== 'expression' && config.sortable === true,
        cell: display !== null ? 'display' : link ? 'link' : column.kind === 'navigation' || column.kind === 'image' || column.kind === 'file' ? 'value' : 'static',
        display,
      };
    });
  });

  /** The loaded display components, by loader. */
  readonly #cells = signal<ReadonlyMap<TmLazyComponent, Type<unknown>>>(new Map());

  /** The display components being loaded. */
  readonly #loadingCells = new Set<TmLazyComponent>();

  /** Whether the declaration names a component that replaces the results region. */
  protected readonly hatched = computed(() => this.#declared().search !== undefined);

  /** The declaration's results component, once loaded. */
  protected readonly body = signal<Type<unknown> | null>(null);

  /** The grid's order: the order column's, while it shows. */
  protected readonly sort = computed<TmGridSort | null>(() => {
    const order = this.#state().query.order;
    if (order === undefined) {
      return null;
    }
    const desc = order.endsWith(':desc');
    const key = desc ? order.slice(0, -5) : order;
    return this.#plan().columns.some((column) => column.key === key) ? { key, direction: desc ? 'desc' : 'asc' } : null;
  });

  /** The paging: the page, the size, the sizes offered, the last page and whether the pager pages. */
  protected readonly paging = computed(() => {
    const setting = this.#setting();
    const effective = tmSearchEffective(setting, this.#state().query);
    const browse = this.#request()?.tree === 'browse';
    const total = this.#total();
    return {
      page: browse ? 1 : effective.page,
      size: browse ? Math.max(1, total ?? 1) : effective.size,
      // The page size shows from `md` up, its long label from `xl` up.
      sizes: browse || !this.#breakpoints.atLeast('md')() ? [] : tmSearchPageSizes(setting.descriptor),
      sizeLabel: this.#breakpoints.atLeast('xl')() ? ('long' as const) : ('short' as const),
      maxPage: tmSearchMaxPage(setting.descriptor, effective.size),
      disabled: browse,
    };
  });

  /** The total the pager shows. */
  protected readonly total = this.#total;

  /** Whether the pager's total is a lower bound. */
  protected readonly capped = this.#capped;

  /** The region's state in place of rows; `null` while rows show, or the first load's skeleton does. */
  protected readonly region = computed<TmSearchRegionState | null>(() => {
    const state = this.#state();
    if (state.payloadInvalid) {
      return { kind: 'invalid-link' };
    }
    const held = this.#held();
    if (held.result !== null) {
      // A page past the end shows nothing while the last page with rows loads.
      return held.result.set.rows.length > 0 || tmSearchLastPage(held.result) !== null ? null : this.#emptyState(state.query);
    }
    return held.failure === null ? null : this.#failureState(held.failure);
  });

  /** Whether the first load's skeleton shows: a read runs with no rows and no state held. */
  protected readonly firstLoad = computed(() => this.loading() && this.#held().result === null && this.region() === null);

  /** The Create control: its URL, label and disabled reason; `null` when the entity cannot create from this page. */
  protected readonly create = computed(() => {
    const entity = this.#descriptor().name;
    const summary = this.#entities.summary(entity);
    if (this.#mode !== 'page' || summary === undefined || !summary.operations.includes('save') || !this.#entities.ɵhasDetailsPage(summary)) {
      return null;
    }
    return {
      href: this.#tenantUrls.create(entity),
      label: this.#l10n.ɵtext(summary.newLabelKey)(),
      reason: this.#permissions.disabledReason(entity, 'save')(),
    };
  });

  /** The page-state key of this page, which a row it opens finds it by. */
  readonly #from = tmPageStateKey(this.#path);

  /** The router state a row opens with: this page's state key and the shown page, for the details page's stepping. */
  protected readonly openState = computed(() => ({ tmFrom: this.#from, tmPage: this.#state().query.page ?? 1 }));

  /** Create as a link, while the user may create. */
  protected readonly createLink = computed(() => {
    const action = this.create();
    return action !== null && action.reason === null ? action : null;
  });

  /** Create as a disabled button that says why, while the user may not create. */
  protected readonly createRefused = computed(() => {
    const action = this.create();
    return action !== null && action.reason !== null ? { label: action.label, reason: action.reason } : null;
  });

  /** The declared page actions the toolbar shows: not over the selection, and permitted. */
  readonly #declaredActions = computed(() =>
    (this.#declared().pageActions ?? []).filter(
      (action) => action.selection !== true && (action.requires === undefined || this.#permissions.can(action.requires[0], action.requires[1])()),
    ),
  );

  /** The declared `primary` page actions, as buttons after Create; in the narrowest form, none. */
  protected readonly primaryActions = computed(() =>
    this.compact()
      ? []
      : this.#declaredActions()
          .filter((action) => (action.group ?? 'primary') === 'primary')
          .map((action) => ({ id: action.id, icon: action.icon, label: this.#l10n.ɵtext(action.labelKey)() })),
  );

  /** The More menu's items: the `menu` page actions, and in the narrowest form every declared one. */
  protected readonly menuActions = computed<readonly TmMenuItem[]>(() => {
    const running = this.#actions.running();
    return this.#declaredActions()
      .filter((action) => this.compact() || action.group === 'menu')
      .map((action) => ({
        id: action.id,
        label: this.#l10n.ɵtext(action.labelKey)(),
        ...(action.icon !== undefined && { icon: action.icon }),
        disabled: running.has(action.id),
        action: () => void this.invoke(action.id),
      }));
  });

  /** The ids of the page actions running, which their buttons show busy. */
  protected readonly running: Signal<ReadonlySet<string>>;

  /** The glyphs of the toolbar and the grid. */
  protected readonly glyphs = { refresh: RefreshCw, create: Plus, columns: Columns3, more: Ellipsis };

  /** The toolbar's and the grid's labels. */
  protected readonly text = {
    refresh: this.#l10n.ɵtext('tm.search.refresh'),
    more: this.#l10n.ɵtext('tm.search.more'),
    moreActions: this.#l10n.ɵtext('tm.search.moreActions'),
    chooseColumns: this.#l10n.ɵtext('tm.search.chooseColumns'),
    loading: this.#l10n.ɵtext('tm.search.loading'),
  };

  /** The page actions, for the host's own controls. */
  readonly #actions = inject(TmPageActions);

  /** The columns whose drops were reported, and the drops reported for them. */
  #reported: { readonly columns: unknown; readonly drops: Set<string> } = { columns: null, drops: new Set() };

  /** Whether an offline failure has seen the API offline, so a reconnect re-reads it. */
  #awaitingOnline = false;

  // ---------------------------------------------------------------------------
  // The context
  // ---------------------------------------------------------------------------

  /** The page's context, which `injectTmSearch()` returns and the page actions receive. Internal. */
  readonly ɵcontext: TmSearchContext;

  /** Creates the host: its strings and icons, the read's side effects, and the address it follows. */
  constructor() {
    this.#l10n.ɵregisterStrings('search', ɵTM_SEARCH_STRINGS_EN);
    const icons = inject(TmIconRegistry);
    const missing = Object.entries(ICONS).filter(([name]) => icons.get(name) === undefined);
    if (missing.length > 0) {
      icons.register(Object.fromEntries(missing));
    }
    this.running = this.#actions.running;

    // --- The context its parts read; its descriptor is the current one, rebuilt on a language switch ---
    const descriptor = this.#descriptor;
    this.ɵcontext = {
      mode: this.#mode,
      get descriptor(): TmEntityDescriptor {
        return descriptor();
      },
      query: computed(() => this.#state().query),
      plan: this.#plan,
      rows: this.#rows,
      ancestors: this.#ancestors,
      total: this.#total,
      totalIsLowerBound: this.#capped,
      status: this.#status,
      error: this.#error,
      selection: this.selection.asReadonly(),
      highlight: this.highlight,
      select: (ids, mode) => this.#select(ids, mode),
      update: (patch) => this.#update(patch),
      refresh: () => this.#refresh(),
      open: (id, options) => void this.#open(id, options?.newTab === true),
      url: (id) => {
        const row = this.#byId().get(id);
        return row === undefined ? null : this.#targetOf()(row);
      },
    };

    // --- Each dropped column and extra path reported once per columns ---
    effect(() => {
      const plan = this.#plan();
      const columns = this.#columns();
      untracked(() => this.#reportDrops(plan, columns));
    });

    // --- A column the user may read again returns as the access moves; one no longer readable drops from the plan ---
    // The preference's and the code default's columns are planned again (a
    // URL's columns dropped with the URL), tracking what the user may read.
    effect(() => {
      const state = this.#state();
      if (untracked(() => this.#leaving())) {
        return;
      }
      const setting = tmSearchQuiet({ ...untracked(this.#setting), descriptor: this.#descriptor(), declaration: this.#declaration() });
      const { columns, from } = tmSearchColumnsOf(setting, state.columnsFrom === 'url' ? state.query.cols : undefined);
      const shown = new Set(state.columns.map(columnIdentity));
      if (columns.some((column) => !shown.has(columnIdentity(column)))) {
        untracked(() => {
          this.#state.set({ ...state, query: tmSearchCanonical(setting, { ...state.query, cols: undefined }, columns), columns, columnsFrom: from });
          this.#write(this.#canonical(), 'replace');
        });
      }
    });

    // --- The highlighted row's target preloads, and once a result shows, Create's page too ---
    effect(() => {
      const id = this.highlight();
      const row = id === null ? undefined : this.#byId().get(id);
      if (row !== undefined) {
        untracked(() => this.#preload(row));
      }
    });
    effect(() => {
      const href = this.#held().result === null ? null : (this.createLink()?.href ?? null);
      if (href !== null) {
        untracked(() => this.#urls.preload(href));
      }
    });

    // --- The display components of the shown columns, loaded beside the rows ---
    effect(() => {
      const loaders = this.gridColumns()
        .map((column) => column.display)
        .filter((display) => display !== null);
      untracked(() => loaders.forEach((loader) => this.#loadCell(loader)));
    });

    // --- A width change crossing `md` follows the new class's view ---
    let width = untracked(this.#width);
    effect(() => {
      const next = this.#width();
      if (next !== width) {
        const from = width;
        width = next;
        untracked(() => this.#resize(from));
      }
    });

    // --- A settled result is announced; rows kept through an offline re-read say so ---
    let announced: TmSearchHeld = NOTHING_HELD;
    effect(() => {
      const held = this.#held();
      if (held !== announced) {
        const previous = announced;
        announced = held;
        untracked(() => this.#announce(held, previous));
      }
    });

    // --- The offline view re-reads once the API is online again ---
    effect(() => {
      const online = this.#api.online();
      const offline = this.#held().failure?.error.kind === 'offline';
      if (!offline) {
        this.#awaitingOnline = false;
      } else if (!online) {
        this.#awaitingOnline = true;
      } else if (this.#awaitingOnline) {
        this.#awaitingOnline = false;
        untracked(() => this.#refresh());
      }
    });

    // --- A page past the server's window loads the first page instead ---
    effect(() => {
      const failure = this.#held().failure;
      if (failure?.error.kind === 'limit' && failure.key.request.tree !== 'browse' && (untracked(this.#state).query.page ?? 1) > 1) {
        untracked(() => this.#apply(tmSearchUpdate(this.#setting(), this.#state(), { page: undefined }), 'page', 'replace'));
      }
    });

    // --- A page past the end of the rows (rows deleted meanwhile, a stale link) loads the last page with rows ---
    effect(() => {
      const result = this.#held().result;
      const last = result === null ? null : tmSearchLastPage(result);
      if (last !== null) {
        untracked(() => this.#apply(tmSearchUpdate(this.#setting(), this.#state(), { page: last > 1 ? last : undefined }), 'page', 'replace'));
      }
    });

    // --- Back, Forward and a link to this page with another query re-resolve the state ---
    this.#router.events.pipe(takeUntilDestroyed()).subscribe((event) => {
      if (event instanceof NavigationEnd) {
        this.#follow();
      }
    });
  }

  /**
   * Loads the declaration's results component beside the first read, publishes the page's context and its page
   * actions, and replaces the address with the canonical URL when they differ.
   */
  ngOnInit(): void {
    // --- The declaration's results component, loaded beside the first read (the entity input is set by now) ---
    const search = this.#declared().search;
    if (search !== undefined) {
      componentOf(search).then(
        (component) => this.body.set(component),
        (error: unknown) => this.#telemetry.error(error, { area: 'tm.search' }),
      );
    }

    this.#publish();
    this.#register();
    this.#write(this.#canonical(), 'replace');
  }

  // ---------------------------------------------------------------------------
  // The template's handlers
  // ---------------------------------------------------------------------------

  /**
   * A grid column's display component, once loaded.
   *
   * @param column - The column.
   * @returns The component, or `null` while it loads or when it failed.
   */
  protected displayOf(column: TmSearchGridColumn): Type<unknown> | null {
    return column.display === null ? null : (this.#cells().get(column.display) ?? null);
  }

  /**
   * A row's target, for its link.
   *
   * @param row - The row, as the grid passes it.
   * @returns The URL, or `null`.
   */
  protected targetOf(row: unknown): string | null {
    return this.#targetOf()(row as TmPlanRow);
  }

  /**
   * Preloads the target of the row under the pointer, once per route. The
   * grid never reorders the rows it binds, so a row's index among the
   * rendered ones (its `aria-rowindex` past the header row) is its index in
   * the rows.
   *
   * @param event - The pointer event.
   */
  protected hover(event: PointerEvent): void {
    const element = event.target instanceof Element ? event.target.closest('[role="row"][aria-rowindex]') : null;
    const row = element === null ? undefined : this.rows()[Number(element.getAttribute('aria-rowindex')) - 2];
    if (row !== undefined) {
      this.#preload(row);
    }
  }

  /**
   * A cell's text, for a link cell.
   *
   * @param column - The column.
   * @param row - The row, as the grid passes it.
   * @returns The text.
   */
  protected cellText(column: TmColumnPlan, row: unknown): string {
    return this.#formats.cell(column, row as TmPlanRow);
  }

  /**
   * Opens the row the grid activated.
   *
   * @param activation - The activation.
   */
  protected activate(activation: TmGridRowActivation<TmPlanRow>): void {
    void this.#open(activation.row.id, activation.newTab);
  }

  /**
   * Orders by the header the user clicked: ascending, descending, then the default order.
   *
   * @param sort - The grid's new order.
   */
  protected orderBy(sort: TmGridSort | null): void {
    this.#update({ order: sort === null ? undefined : sort.direction === 'desc' ? `${sort.key}:desc` : sort.key });
  }

  /**
   * Goes to a page, which pushes a history entry.
   *
   * @param page - The 1-based page.
   */
  protected goTo(page: number): void {
    this.#update({ page });
  }

  /**
   * Changes the page size, keeping the first visible row.
   *
   * @param size - The size.
   */
  protected setSize(size: number): void {
    this.#update({ size });
  }

  /**
   * Changes the selection from the grid's checkboxes.
   *
   * @param ids - The checked ids.
   */
  protected selected(ids: ReadonlySet<string | number>): void {
    this.selection.set(ids as ReadonlySet<TmId>);
  }

  /**
   * Clears what a no-matches state offers to clear: the filters, else the search text.
   *
   * @param what - What to clear.
   */
  protected clear(what: 'filters' | 'search'): void {
    this.#update(what === 'search' ? { q: undefined } : { columnFilters: undefined, filter: undefined, custom: undefined, inactive: undefined });
  }

  /** Reads the query again: the error view's Try again. */
  protected retry(): void {
    this.#refresh();
  }

  /**
   * Invokes a page action, as the toolbar's controls do.
   *
   * @param id - The action's id.
   * @returns How it ended.
   */
  protected invoke(id: string): Promise<TmActionResult> {
    return this.#actions.invoke(id, {}, 'user');
  }

  // ---------------------------------------------------------------------------
  // The state's changes
  // ---------------------------------------------------------------------------

  /**
   * Changes the query under the history and paging rules.
   *
   * @param patch - The fields to change.
   */
  #update(patch: Partial<TmSearchQuery>): void {
    const source: TmSearchReadSource =
      'order' in patch ? 'order' : 'page' in patch || 'size' in patch ? 'page' : 'q' in patch && Object.keys(patch).length === 1 ? 'typing' : 'filter';
    this.#apply(tmSearchUpdate(this.#setting(), this.#state(), patch), source);
  }

  /**
   * Applies a transition: the state, the preferences it writes, and the URL.
   *
   * @param transition - The transition.
   * @param source - What the read it starts reports.
   * @param history - How the URL reaches history; the transition's own when omitted.
   */
  #apply(transition: TmSearchTransition, source: TmSearchReadSource, history = transition.history): void {
    this.#source = source;
    this.#state.set(transition.state);
    // --- The preferences the change writes ---
    const entity = this.#descriptor().name;
    const { columns, view, pageSize } = transition.preferences;
    if (columns !== undefined) {
      this.#preferences.set(tmSearchColumnsKey(entity), columns);
    }
    if (view !== undefined) {
      this.#preferences.set(tmSearchViewKey(entity), view);
    }
    if (pageSize !== undefined) {
      this.#preferences.set(tmSearchPageSizeKey(entity), pageSize);
    }
    if (history !== null) {
      this.#write(transition.canonical, history);
    }
  }

  /** Reads the current query again under a new generation. */
  #refresh(): void {
    this.#source = 'refresh';
    this.#generation.update((generation) => generation + 1);
  }

  /**
   * Follows a resize across `md`: the new class's view.
   *
   * @param from - The width class before.
   */
  #resize(from: 'narrow' | 'wide'): void {
    this.#apply(tmSearchResize(this.#setting(), this.#state(), from), 'load');
  }

  /**
   * Changes the selection.
   *
   * @param ids - The rows' ids.
   * @param mode - Whether they join it, leave it, or replace it.
   */
  #select(ids: Iterable<TmId>, mode: 'add' | 'remove' | 'replace'): void {
    const next = new Set(mode === 'replace' ? [] : this.selection());
    for (const id of ids) {
      if (mode === 'remove') {
        next.delete(id);
      } else {
        next.add(id);
      }
    }
    this.selection.set(next);
  }

  /**
   * Opens a row's target, in a new tab when asked.
   *
   * @param id - The row's id.
   * @param newTab - Whether it opens in a new tab.
   * @returns Whether the row had a target.
   */
  #open(id: TmId, newTab: boolean): boolean {
    const url = this.ɵcontext.url(id);
    if (url === null) {
      return false;
    }
    if (newTab) {
      this.#window?.open(url, '_blank', 'noopener');
    } else {
      void this.#router.navigateByUrl(url, { state: this.openState() });
    }
    return true;
  }

  /**
   * Preloads a row's target at idle; the URLs load each route's chunks once.
   *
   * @param row - The row.
   */
  #preload(row: TmPlanRow): void {
    const target = this.#targetOf()(row);
    if (target !== null) {
      this.#urls.preload(target);
    }
  }

  // ---------------------------------------------------------------------------
  // The address
  // ---------------------------------------------------------------------------

  /**
   * What the page opens over: the address's query and raw fragment.
   *
   * @param navigation - Whether the page came here by Back or Forward.
   * @returns The opening.
   */
  #opening(navigation: 'open' | 'history'): TmSearchOpening {
    // The location's own path keeps the raw fragment, which the router would decode.
    const address = new URL(this.#location.path(true), 'https://tellma.invalid');
    return { params: address.searchParams, hash: address.hash, navigation };
  }

  /** Re-resolves the state after a navigation that changed the address to another query. */
  #follow(): void {
    const navigation = this.#router.lastSuccessfulNavigation()?.trigger === 'popstate' ? 'history' : 'open';
    // Planned quietly first, so the page's own writes, which change nothing, report nothing.
    if (tmSearchResolve(tmSearchQuiet(this.#setting()), this.#opening(navigation)).canonical === this.#canonical()) {
      return;
    }
    const resolution = tmSearchResolve(this.#setting(), this.#opening(navigation));
    this.#source = 'load';
    this.#state.set(resolution.state);
    this.#write(resolution.canonical, 'replace');
  }

  /**
   * Whether the app navigates away from this page: a navigation runs to
   * another page's path. The page then holds its own address writes, which
   * would cancel that navigation; a page whose chunk landed after the user
   * left also starts so.
   *
   * @returns Whether it does.
   */
  #leaving(): boolean {
    const navigation = this.#router.currentNavigation();
    return navigation !== null && tmPageStateKey(this.#router.serializeUrl(navigation.finalUrl ?? navigation.extractedUrl)) !== this.#from;
  }

  /**
   * Writes a canonical URL to the address, when it differs.
   *
   * @param canonical - The URL; empty for none.
   * @param history - Whether it pushes a history entry or replaces the current one.
   */
  #write(canonical: string, history: 'push' | 'replace'): void {
    const normalize = (url: string): string => this.#router.serializeUrl(this.#router.parseUrl(url));
    // A write while the app navigates away would cancel that navigation.
    if (this.#leaving() || canonical === '' || normalize(canonical) === normalize(this.#location.path(true))) {
      return;
    }
    void this.#router.navigateByUrl(canonical, { replaceUrl: history === 'replace' });
  }

  // ---------------------------------------------------------------------------
  // The read
  // ---------------------------------------------------------------------------

  /**
   * Runs one read: the request through the API, then the display names
   * remembered and the timing reported. Every failure resolves as an
   * outcome, so the resource never holds an error of its own.
   *
   * @param key - The read's key.
   * @param signal - The read's abort signal.
   * @returns How it ended.
   */
  async #load(key: TmSearchReadKey, signal: AbortSignal): Promise<TmSearchOutcome> {
    const started = performance.now();
    const entity = untracked(this.#descriptor).name;
    try {
      const wire = await this.#api.read<TmSearchWire>(key.request.route, key.request.body, { signal });
      const { total, capped } = tmSearchTotal(key.request, wire);
      // A details page opened from these rows renders its title at once.
      this.#names.remember(entity, wire.rows);
      this.#time(key, started, wire.rows.rows.length === 0 ? 'empty' : 'ok');
      return { ok: true, key, set: wire.rows, ancestors: wire.ancestors ?? null, total, capped };
    } catch (caught) {
      const error = tmSearchApiError(caught);
      if (!(caught instanceof TmApiError)) {
        // Not the server's answer: a fault of the client's, reported as one.
        this.#telemetry.error(caught, { area: 'tm.search' });
      }
      if (error.kind !== 'cancelled') {
        this.#time(key, started, error.kind === 'query-invalid' || tmSearchArithmetic(error) !== null ? 'invalid' : 'error');
      }
      return { ok: false, key, error };
    }
  }

  /**
   * Reports a read's `tm.search.query` timing.
   *
   * @param key - The read's key.
   * @param started - When it started.
   * @param outcome - How it ended.
   */
  #time(key: TmSearchReadKey, started: number, outcome: 'ok' | 'empty' | 'invalid' | 'error'): void {
    const tree = key.request.tree ?? 'none';
    // The grid is the one view the page renders until the tiles land.
    this.#telemetry.timing('tm.search.query', performance.now() - started, { source: key.source, view: 'grid', tree, outcome }, { sampled: true });
  }

  /**
   * The state of a result with no rows: no data when nothing narrows the
   * query and inactive rows are excluded, else no matches with the way out
   * it offers.
   *
   * @param query - The query.
   * @returns The state.
   */
  #emptyState(query: TmSearchQuery): TmSearchRegionState {
    const filtered = query.columnFilters !== undefined || query.filter !== undefined || query.custom !== undefined;
    if (!filtered && query.q === undefined && query.inactive !== true) {
      const create = this.create();
      return { kind: 'no-data', create: create !== null && create.reason === null ? { href: create.href, label: create.label } : null };
    }
    return { kind: 'no-matches', clear: filtered || query.inactive === true ? 'filters' : query.q !== undefined ? 'search' : null };
  }

  /**
   * The state of a failed read: the invalid filter's, the calculation's,
   * the invalid link's, else the error table's view.
   *
   * @param failure - The failure.
   * @returns The state.
   */
  #failureState(failure: TmSearchFailure): TmSearchRegionState {
    const { error, key } = failure;
    const reason = tmSearchArithmetic(error);
    if (reason !== null) {
      return { kind: 'calculation', reason };
    }
    const diagnosis = tmSearchDiagnose(key.request, this.#mode, error);
    if (diagnosis !== null && diagnosis.state !== 'other') {
      if (diagnosis.state === 'invalid-link') {
        return { kind: 'invalid-link' };
      }
      const diagnostic = diagnosis.diagnostic;
      const filters = key.request.body['filters'];
      const clause = diagnostic?.origin?.kind === 'fixed' || diagnostic === null ? null : Array.isArray(filters) ? filters[indexOf(diagnostic.clause)] : null;
      const token = typeof clause === 'string' && diagnostic !== null && diagnostic.length > 0 ? clause.slice(diagnostic.start, diagnostic.start + diagnostic.length) : null;
      return { kind: diagnosis.state, message: diagnostic?.message ?? null, token };
    }
    return { kind: 'error', error, entity: this.#descriptor().name };
  }

  /**
   * Announces a settled result: its count, or that the rows on screen were
   * kept through an offline re-read. A state announces itself.
   *
   * @param held - What the host holds now.
   * @param previous - What it held before.
   */
  #announce(held: TmSearchHeld, previous: TmSearchHeld): void {
    if (held.failure?.error.kind === 'offline' && held.result !== null) {
      void this.#announcer.announce(this.#l10n.ɵtext('tm.search.offline')(), 'polite');
      return;
    }
    const result = held.result;
    if (result !== null && result !== previous.result && result.set.rows.length > 0) {
      const count = result.total ?? result.set.rows.length;
      void this.#announcer.announce(this.#l10n.ɵtext(result.capped ? 'tm.search.resultsAtLeast' : 'tm.search.results', { count })(), 'polite');
    }
    // A leaf or a named filter from code that the server refused is a distribution's bug.
    if (held.failure !== null && held.failure !== previous.failure && tmSearchDiagnose(held.failure.key.request, this.#mode, held.failure.error)?.state === 'invalid-code') {
      this.#telemetry.event('tm.search.custom-filter-invalid');
    }
  }

  /**
   * Reports each drop of a plan once per shown columns: a tile path or a
   * tree key under the declaration, a column under the columns' source.
   *
   * @param plan - The plan.
   * @param columns - The shown columns it was built for.
   */
  #reportDrops(plan: TmSelectPlan, columns: unknown): void {
    if (this.#reported.columns !== columns) {
      this.#reported = { columns, drops: new Set() };
    }
    const setting = this.#setting();
    const extra = new Set([...tmSearchTilePaths(setting), ...tmSearchTreeKeys(setting.descriptor)]);
    const from = this.#state().columnsFrom;
    for (const { key, reason } of plan.dropped) {
      const id = `${key}\n${reason}`;
      if (!this.#reported.drops.has(id)) {
        this.#reported.drops.add(id);
        this.#telemetry.event('tm.entity.column-dropped', { reason, source: extra.has(key) ? 'declaration' : from });
      }
    }
  }

  /**
   * Loads a display component once; a load that fails leaves the column's
   * cells as text and is reported.
   *
   * @param loader - The component's loader.
   */
  #loadCell(loader: TmLazyComponent): void {
    if (this.#loadingCells.has(loader)) {
      return;
    }
    this.#loadingCells.add(loader);
    componentOf(loader).then(
      (component) => this.#cells.update((cells) => new Map(cells).set(loader, component)),
      (error: unknown) => {
        this.#loadingCells.delete(loader);
        this.#telemetry.error(error, { area: 'tm.search' });
      },
    );
  }

  // ---------------------------------------------------------------------------
  // The page context and the page actions
  // ---------------------------------------------------------------------------

  /** Registers the page's context, which replaces the route host's default one. */
  #publish(): void {
    const { ɵtemplate } = this.#data;
    const template =
      ɵtemplate === undefined ? '/:t/:segment' : this.#mode === 'list' || this.#segment === undefined ? ɵtemplate : ɵtemplate.replace(':segment', this.#segment);
    this.#contexts.register(
      {
        kind: 'search',
        entity: this.#descriptor().name,
        routeTemplate: template,
        title: this.title,
        status: this.#status,
        state: computed(() => this.#contextState()),
        visible: computed(() => this.#visible()),
        errors: computed(() => this.#contextErrors()),
        actions: signal([]).asReadonly(),
      },
      this.#destroyRef,
    );
  }

  /** The page context's state: the query, the view, the list choice and the selection. */
  #contextState(): TmJson {
    const setting = this.#setting();
    const query = this.#state().query;
    const effective = tmSearchEffective(setting, query);
    const selection = [...this.selection()];
    return {
      query: query as unknown as TmJsonObject,
      view: 'grid',
      list: tmSearchIsTree(setting.descriptor) && !effective.tree,
      selectedIds: selection.slice(0, CONTEXT_SELECTED_IDS),
      selectedCount: selection.length,
    };
  }

  /** The page context's visible data: the shown columns and at most 200 rows, as displayed. */
  #visible(): TmVisibleData | undefined {
    const columns = this.#plan().columns.slice(0, VISIBLE_COLUMNS);
    const rows = this.#rows();
    const headers = this.gridColumns();
    return {
      columns: columns.map((column, index) => ({ path: column.key, label: headers[index]?.header() ?? column.label() })),
      rows: rows.slice(0, VISIBLE_ROWS).map((row) => ({ id: row.id, cells: Object.fromEntries(columns.flatMap((column) => this.#visibleCells(column, row))) })),
      total: this.#total(),
      truncated: rows.length > VISIBLE_ROWS,
    };
  }

  /**
   * A cell as the page context carries it: a navigation as its display name
   * with its id beside it, any other value as the wire sent it.
   *
   * @param column - The column.
   * @param row - The row.
   * @returns The cell's entries.
   */
  #visibleCells(column: TmColumnPlan, row: TmPlanRow): (readonly [string, TmJson])[] {
    const value = column.value(row);
    const json = (value === undefined ? null : value) as TmJson;
    if (column.kind === 'navigation') {
      return [
        [column.key, json === null ? null : this.#formats.cell(column, row)],
        [`${column.key}.Id`, json],
      ];
    }
    return [[column.key, json]];
  }

  /** The page context's errors: the invalid filter's diagnostics, else the read's error with its trace id. */
  #contextErrors(): readonly TmContextError[] {
    const failure = this.#held().failure;
    if (failure === null) {
      return [];
    }
    const diagnosis = tmSearchDiagnose(failure.key.request, this.#mode, failure.error);
    if (diagnosis !== null && diagnosis.diagnostics.length > 0) {
      return diagnosis.diagnostics.map((diagnostic) => ({
        source: 'request',
        code: diagnostic.code,
        message: diagnostic.message ?? diagnostic.code,
        path: diagnostic.clause,
        ...(failure.error.traceId !== '' && { traceId: failure.error.traceId }),
      }));
    }
    return [
      {
        source: 'request',
        code: failure.error.code,
        message: failure.error.message,
        ...(failure.error.traceId !== '' && { traceId: failure.error.traceId }),
      },
    ];
  }

  /** Registers the host's own page actions and the declaration's. */
  #register(): void {
    const actions = this.#actions;
    const destroyRef = this.#destroyRef;
    const done: TmActionResult = { outcome: 'done' };

    // --- Create, when the entity creates from this page ---
    const create = untracked(this.create);
    if (create !== null) {
      const entity = this.#descriptor().name;
      const reason = this.#permissions.disabledReason(entity, 'save');
      actions.register(
        {
          id: 'search.create',
          labelKey: this.#entities.summary(entity)!.newLabelKey,
          icon: 'plus',
          group: 'primary',
          order: 10,
          enabled: computed(() => reason() === null),
          disabledReason: reason,
          invoke: async () => {
            await this.#router.navigateByUrl(this.#tenantUrls.create(entity));
            return done;
          },
        },
        destroyRef,
      );
    }

    // --- Refresh ---
    actions.register(
      {
        id: 'search.refresh',
        labelKey: 'tm.search.refresh',
        icon: 'refresh-cw',
        group: 'primary',
        order: 20,
        invoke: () => {
          this.#refresh();
          return Promise.resolve(done);
        },
      },
      destroyRef,
    );

    // --- The query and the open, without controls of their own ---
    actions.register(
      {
        id: 'search.query',
        labelKey: 'tm.search.query',
        params: QUERY_PARAMS,
        invoke: (args) => {
          this.#update(queryPatch(args));
          return Promise.resolve(done);
        },
      },
      destroyRef,
    );
    actions.register(
      {
        id: 'search.open',
        labelKey: 'tm.search.open',
        params: OPEN_PARAMS,
        invoke: (args) => {
          const id = (args as TmJsonObject)['id'] as TmId;
          return Promise.resolve({ outcome: 'done', data: { opened: this.#open(id, false) } });
        },
      },
      destroyRef,
    );

    // --- The declaration's page actions, each keeping its own id and group ---
    for (const declared of untracked(this.#declared).pageActions ?? []) {
      this.#registerDeclared(declared);
    }
  }

  /**
   * Registers one of the declaration's page actions: shown while its
   * permission holds, over the selection when it says so, and run in the
   * host's injection context with the page's context.
   *
   * @param declared - The declared action.
   */
  #registerDeclared(declared: TmSearchPageActionDeclaration): void {
    const permitted = declared.requires === undefined ? signal(true).asReadonly() : this.#permissions.can(declared.requires[0], declared.requires[1]);
    const selection = declared.selection === true;
    this.#actions.register(
      {
        id: declared.id,
        labelKey: declared.labelKey,
        ...(declared.icon !== undefined && { icon: declared.icon }),
        // An action over the selection is a menu action whatever its group.
        group: selection ? 'menu' : (declared.group ?? 'primary'),
        ...(declared.order !== undefined && { order: declared.order }),
        destructive: declared.destructive === true,
        ...(declared.confirm !== undefined && { confirm: declared.confirm }),
        enabled: computed(() => permitted() && (!selection || this.selection().size > 0)),
        invoke: async () => {
          const ids = selection ? [...this.selection()] : [];
          await runInInjectionContext(this.#injector, () => declared.run(this.ɵcontext, ids));
          return { outcome: 'done' };
        },
      },
      this.#destroyRef,
    );
  }
}

/**
 * The last page that has rows, for a result that came back empty past the
 * end of them: a page after the first whose total says rows exist. `null`
 * for any other result, a browse tree's included.
 *
 * @param result - The result.
 * @returns The last page with rows, or `null`.
 */
function tmSearchLastPage(result: TmSearchResult): number | null {
  const { body, tree } = result.key.request;
  const { skip, take } = body;
  // An unknown or zero total, a first page or a browse tree's rows have no last page to go to.
  if (result.set.rows.length > 0 || tree === 'browse' || typeof skip !== 'number' || typeof take !== 'number' || !skip || !result.total) {
    return null;
  }
  const last = Math.ceil(result.total / take);
  return last * take > skip ? null : last;
}

/**
 * What tells a shown column from the others: an expression's text, else its path in any case.
 *
 * @param column - The column.
 * @returns Its identity.
 */
function columnIdentity(column: TmColumnInput<string>): string {
  return tmSearchIsExpression(column) ? `=${column.expression}` : tmSearchColumnKey(column).toLowerCase();
}

/**
 * The index a clause such as `filters[1]` names.
 *
 * @param clause - The clause.
 * @returns The index, or -1.
 */
function indexOf(clause: string): number {
  const match = /^filters\[(\d+)\]$/.exec(clause);
  return match === null ? -1 : Number(match[1]);
}

/**
 * The query patch `search.query`'s arguments name: each known member of
 * its type, the rest left out.
 *
 * @param args - The arguments.
 * @returns The patch.
 */
function queryPatch(args: TmJson): Partial<TmSearchQuery> {
  const given = (typeof args === 'object' && args !== null && !Array.isArray(args) ? args : {}) as TmJsonObject;
  const patch: { -readonly [K in keyof TmSearchQuery]?: TmSearchQuery[K] } = {};
  const text = (name: 'q' | 'tab' | 'filter' | 'order'): void => {
    if (typeof given[name] === 'string') {
      patch[name] = given[name];
    }
  };
  const record = (name: 'columnFilters' | 'custom'): void => {
    const value = given[name];
    if (typeof value === 'object' && value !== null && !Array.isArray(value)) {
      patch[name] = Object.fromEntries(Object.entries(value).filter((entry): entry is [string, string] => typeof entry[1] === 'string'));
    }
  };
  text('q');
  text('tab');
  text('filter');
  text('order');
  record('columnFilters');
  record('custom');
  if (typeof given['inactive'] === 'boolean') {
    patch.inactive = given['inactive'];
  }
  if (typeof given['page'] === 'number' && Number.isInteger(given['page']) && given['page'] >= 1) {
    patch.page = given['page'];
  }
  return patch;
}

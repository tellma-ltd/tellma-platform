// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { LocationStrategy, NgComponentOutlet } from '@angular/common';
import {
  ChangeDetectionStrategy,
  Component,
  computed,
  DestroyRef,
  effect,
  inject,
  InjectionToken,
  Injector,
  input,
  linkedSignal,
  signal,
  untracked,
  ViewEncapsulation,
  type Signal,
  type Type,
} from '@angular/core';
import { Router } from '@angular/router';
import {
  TM_NOW,
  TmLocalization,
  TmPermissions,
  TmTelemetry,
  type TmJson,
  type TmLazyComponent,
  type TmReportArgs,
  type TmSecurableRef,
  type TmWidgetSize,
} from '@tellma/core';
import { TmL10n } from '@tellma/core-ui';
import { TmButton } from '@tellma/core-ui/button';
import { TmCard } from '@tellma/core-ui/card';
import { TmEmptyState } from '@tellma/core-ui/empty-state';
import { TmIcon } from '@tellma/core-ui/icon';
import { TmMenu, TmMenuTrigger, type TmMenuEntry } from '@tellma/core-ui/menu';
import { TmSkeleton } from '@tellma/core-ui/skeleton';
import { TmTooltip } from '@tellma/core-ui/tooltip';

import type { TmTileList } from './tm-list-tiles';
import type { TmPlacement } from './tm-pack-dashboard';
import { TM_WIDGET_CONTEXT, type TmWidgetContext, type TmWidgetRun, type TmWidgetTick } from './tm-widget-context';

// One tile of the grid (spec 0035 §8.4–§8.6): a labelled section on a card
// whose flush body fills the tile and scrolls inside it, so a tile's height
// never depends on its data. The header never changes with state: its title,
// a list type's count and "View all", or the options menu, and the status
// line's reserved row for a type that shows one. The tile's own states are
// its body's absence: a type no declaration knows is not available, and a
// type whose permission the user lacks is denied and never loads its body.
// Otherwise the body loads from its type's declaration, behind five
// skeleton rows, reads its context through injectTmWidget(), and reports
// its runs back, which the status line shows.

/** What a tile shows in its body. */
export type TmTileView = 'loading' | 'ready' | 'error' | 'denied' | 'unavailable';

/** What a tile's grid resolved for it. */
export interface TmTileModel {
  /** The tile's key. */
  readonly key: string;
  /** The widget type's key. */
  readonly type: string;
  /** The tile's title, already resolved. */
  readonly title: string;
  /** The tile's description, already resolved; `null` when it has none. */
  readonly description: string | null;
  /** The body's loader; `undefined` when no declaration knows the type. */
  readonly component: TmLazyComponent | undefined;
  /** The permission the type requires, when the user lacks it; the tile is denied. */
  readonly denied: TmSecurableRef | null;
  /** Whether the tile shows the status line. */
  readonly status: boolean;
  /** A built-in list type's count and "View all"; `null` for any other type, which shows the options menu. */
  readonly list: TmTileList | null;
  /** The tile's size, in cells. */
  readonly size: TmWidgetSize;
  /** The type's settings, parsed; `null` when the widget has none. */
  readonly settings: TmJson | null;
}

/** Whether a tile's grid is the home page's, whose lists report the home's telemetry. Internal. */
export const TM_HOME_SURFACE = new InjectionToken<boolean>('TM_HOME_SURFACE', { factory: () => false });

/** The tick a tile starts with: no refresh asked for yet. */
const FIRST_TICK: TmWidgetTick = { generation: 0, clear: false, background: false };

/** The bodies already loaded, by loader, so a later tile of a type renders its body in its first frame. */
const loaded = new WeakMap<TmLazyComponent, Type<unknown>>();

/** The tiles created so far, which number the ids of their descriptions. */
let tiles = 0;

/**
 * One dashboard tile: its card, its header, and its body or its state. A
 * grid creates its tiles; a page that lays out its own tiles may too.
 *
 * @example
 * ```html
 * <tm-dashboard-tile [tile]="model" [placement]="placement" [parameters]="args" />
 * ```
 */
@Component({
  selector: 'tm-dashboard-tile',
  imports: [NgComponentOutlet, TmButton, TmCard, TmEmptyState, TmIcon, TmMenu, TmMenuTrigger, TmSkeleton, TmTooltip],
  changeDetection: ChangeDetectionStrategy.OnPush,
  // The card's own elements are filled to the tile's height, so the rules
  // reach into its markup, each one through child combinators so a card
  // nested in a body keeps its own layout.
  encapsulation: ViewEncapsulation.None,
  template: `
    <!-- The tile's section, named by its title and described by its description, busy while its body loads. -->
    <section
      class="tm-dashboard-tile__section"
      [attr.aria-label]="tile().title"
      [attr.aria-describedby]="tile().description === null ? null : descriptionId"
      [attr.aria-busy]="busy() ? 'true' : null"
    >
      <tm-card [heading]="tile().title" [count]="tile().list?.count() ?? null" flush>
        <!-- The header's end: a list's "View all", or the options menu; neither on a denied tile, nor in present mode. -->
        @if (tile().list; as list) {
          @if (list.viewAll; as url) {
            <a tmCardActions class="tm-dashboard-tile__view-all" [href]="href(url)" [attr.aria-label]="viewAllLabel()" (click)="viewAll($event, url)">{{ viewAllText() }}</a>
          }
        } @else if (menuShown()) {
          <button
            tmCardActions
            tmButton
            class="tm-dashboard-tile__options"
            variant="ghost"
            size="sm"
            type="button"
            [tmMenuTriggerFor]="options"
            [attr.aria-label]="optionsLabel()"
          >
            <tm-icon name="ellipsis" />
          </button>
          <tm-menu #options [items]="menu()" [aria-label]="optionsLabel()" />
        }
        @if (tile().status) {
          <!-- The status line: the description, then when the numbers were computed; its row is reserved before the first result. -->
          <span tmCardSubtitle class="tm-dashboard-tile__status" [tmTooltip]="statusTooltip()">
            @if (tile().description; as description) {
              <span class="tm-dashboard-tile__description">{{ description }}</span>
            }
            @if (updated(); as updated) {
              <span class="tm-dashboard-tile__updated">
                <time [attr.datetime]="updated.at">{{ updated.text }}</time>
                @if (updated.stale) {
                  <tm-icon class="tm-dashboard-tile__stale" name="circle-alert" [label]="updated.staleLabel" />
                }
                @if (updated.totals) {
                  <span>{{ updated.totals }}</span>
                }
              </span>
            } @else if (statusPending()) {
              <tm-skeleton class="tm-dashboard-tile__status-bar" shape="line" width="7rem" />
            }
          </span>
        }
        @switch (view()) {
          @case ('ready') {
            <!-- The type's body, which reads the tile's context. -->
            <ng-container *ngComponentOutlet="body(); injector: bodyInjector" />
          }
          @case ('denied') {
            <!-- No access: the permission it needs, and no run. -->
            <tm-empty-state size="panel" icon="lock" [heading]="forbiddenTitle()">{{ forbiddenMessage() }}</tm-empty-state>
          }
          @case ('unavailable') {
            <!-- A type no declaration knows. -->
            <tm-empty-state size="panel" icon="puzzle" [heading]="unavailable()" />
          }
          @case ('error') {
            <!-- The body's chunk did not load. -->
            <tm-empty-state size="panel" icon="wifi-off" [heading]="loadFailed()">
              <button tmButton tmEmptyStateActions type="button" (click)="retry()">{{ retryLabel() }}</button>
            </tm-empty-state>
          }
          @default {
            <!-- The body's chunk is loading: five list rows, as the home skeleton draws. -->
            <tm-skeleton class="tm-dashboard-frame__rows" shape="line" [lines]="5" />
          }
        }
      </tm-card>
      @if (tile().description; as description) {
        <!-- The full description, which describes the section. -->
        <span class="tm-dashboard-tile__described" [id]="descriptionId">{{ description }}</span>
      }
    </section>
  `,
  styles: `
    .tm-dashboard-tile {
      display: block;
    }
    .tm-dashboard-tile > .tm-dashboard-tile__section {
      block-size: 100%;
    }
    .tm-dashboard-tile > .tm-dashboard-tile__section > .tm-card {
      display: flex;
      flex-direction: column;
      block-size: 100%;
    }
    .tm-dashboard-tile > .tm-dashboard-tile__section > .tm-card > .tm-card__region {
      display: flex;
      flex: 1 1 auto;
      flex-direction: column;
      min-block-size: 0;
    }
    .tm-dashboard-tile > .tm-dashboard-tile__section > .tm-card > .tm-card__region > .tm-card__body {
      flex: 1 1 auto;
      min-block-size: 0;
      overflow: auto;
    }

    /* The header's end keeps the heading row's height: the link is one text line, and the
       options button overhangs the row's line box rather than grow it. */
    .tm-dashboard-tile__view-all {
      color: var(--text-link);
      font-size: var(--text-xs);
      line-height: var(--card-line-height);
      text-decoration: none;
      white-space: nowrap;
    }
    .tm-dashboard-tile__view-all:hover {
      text-decoration: underline;
    }
    .tm-dashboard-tile > .tm-dashboard-tile__section > .tm-card > .tm-card__header > .tm-card__heading-row > .tm-card__actions {
      block-size: var(--card-line-height);
    }

    /* The status line: one line, the description ellipsized first, then the time. */
    .tm-dashboard-tile__status {
      display: flex;
      align-items: center;
      gap: var(--space-1);
      min-inline-size: 0;
      overflow: hidden;
      white-space: nowrap;
    }
    .tm-dashboard-tile__description {
      flex: 0 1 auto;
      min-inline-size: 0;
      overflow: hidden;
      text-overflow: ellipsis;
    }
    .tm-dashboard-tile__description + .tm-dashboard-tile__updated::before {
      content: '·';
      margin-inline-end: var(--space-1);
    }
    .tm-dashboard-tile__updated {
      display: inline-flex;
      flex: none;
      align-items: center;
      gap: var(--space-1);
    }
    .tm-dashboard-tile__updated > span::before {
      content: '·';
      margin-inline-end: var(--space-1);
    }
    .tm-dashboard-tile__stale {
      --icon-size: 16px;

      color: var(--warning);
    }
    .tm-dashboard-tile__status-bar {
      flex: none;
    }

    /* The description that names the section to assistive technology alone. */
    .tm-dashboard-tile__described {
      position: absolute;
      inline-size: 1px;
      block-size: 1px;
      overflow: hidden;
      clip-path: inset(50%);
      white-space: nowrap;
    }
  `,
  host: {
    class: 'tm-dashboard-tile tm-dashboard-grid__cell',
    '[attr.data-tile]': 'tile().key',
    '[style.--tm-col]': 'placement().col',
    '[style.--tm-span]': 'placement().span',
    '[style.--tm-row]': 'placement().row',
    '[style.--tm-rows]': 'placement().rows',
    '[style.--tm-row-narrow]': 'placement().rowNarrow',
  },
})
export class TmDashboardTile {
  readonly #l10n = inject(TmLocalization);
  readonly #formats = inject(TmL10n);
  readonly #permissions = inject(TmPermissions);
  readonly #router = inject(Router);
  readonly #location = inject(LocationStrategy);
  readonly #telemetry = inject(TmTelemetry);
  readonly #home = inject(TM_HOME_SURFACE);
  readonly #now = inject(TM_NOW);

  /** What the grid resolved for the tile. */
  readonly tile = input.required<TmTileModel>();

  /** The tile's place on the grid. */
  readonly placement = input.required<TmPlacement>();

  /** The dashboard's supplied arguments, which the body reads; default none. */
  readonly parameters = input<TmReportArgs>({});

  /** Whether the dashboard is presented, which shows no menu; default `false`. */
  readonly presenting = input(false);

  /** The grid's refresh requests; each new value ticks the body. */
  readonly tick = input<TmWidgetTick>(FIRST_TICK);

  /** The body's tick: the grid's requests, and the tile's own Refresh, each a new generation. */
  readonly #tick = linkedSignal<TmWidgetTick, TmWidgetTick>({
    source: this.tick,
    computation: (grid, previous) => (previous === undefined ? grid : { ...grid, generation: previous.value.generation + 1 }),
  });

  /** The body's last reported run; `null` before it reports one. */
  readonly #run = signal<TmWidgetRun | null>(null);

  /** When the shown numbers were computed, kept across a failed run over them. */
  readonly #executedAt = signal<string | null>(null);

  /** The body loaded for this tile, with the loader it came from. */
  readonly #body = signal<{ readonly load: TmLazyComponent; readonly type: Type<unknown> } | null>(null);

  /** The loader whose chunk failed to load, until the user tries again. */
  readonly #failed = signal<TmLazyComponent | null>(null);

  /** The loader whose chunk is loading. */
  #loading: TmLazyComponent | null = null;

  /** Whether the tile has been destroyed, so a late load renders nothing. */
  #destroyed = false;

  /** The id of the element that holds the description. */
  protected readonly descriptionId = `tm-dashboard-tile-${++tiles}-description`;

  /** The context the body reads. */
  readonly #context: TmWidgetContext = {
    key: '',
    type: '',
    settings: computed(() => this.tile().settings),
    parameters: computed(() => this.parameters()),
    mode: signal<'view' | 'edit'>('view').asReadonly(),
    size: computed(() => this.tile().size, { equal: (a, b) => a.w === b.w && a.h === b.h }),
    tick: this.#tick.asReadonly(),
    report: (state) => this.#report(state),
  };

  /** The injector the body is created in, which provides its context. */
  protected readonly bodyInjector = Injector.create({
    providers: [{ provide: TM_WIDGET_CONTEXT, useFactory: () => ({ ...this.#context, key: this.tile().key, type: this.tile().type }) }],
    parent: inject(Injector),
  });

  /** What the tile shows. */
  readonly view: Signal<TmTileView> = computed(() => {
    const tile = this.tile();
    if (tile.component === undefined) {
      return 'unavailable';
    }
    if (tile.denied !== null) {
      return 'denied';
    }
    if (this.#failed() === tile.component) {
      return 'error';
    }
    return this.body() === null ? 'loading' : 'ready';
  });

  /** The body's run, as it last reported it; `null` before it reports one. */
  readonly run: Signal<TmWidgetRun | null> = computed(() => (this.view() === 'ready' ? this.#run() : null));

  /** When the numbers the tile shows were computed; `null` before the first result. */
  readonly executedAt: Signal<string | null> = this.#executedAt.asReadonly();

  /** Whether the tile is busy: its body's chunk, or its body's first run, is loading. */
  protected readonly busy = computed(() => this.view() === 'loading' || (this.run()?.status === 'loading' && this.#executedAt() === null));

  /** The body to render: the one loaded for this tile's type, by any tile. */
  protected readonly body: Signal<Type<unknown> | null> = computed(() => {
    const component = this.tile().component;
    if (component === undefined) {
      return null;
    }
    const held = this.#body();
    return loaded.get(component) ?? (held?.load === component ? held.type : null);
  });

  /** Whether the options menu shows: a type that is not a list, in view mode, not denied, not presented. */
  protected readonly menuShown = computed(() => this.tile().list === null && this.tile().denied === null && !this.presenting());

  /** The options menu's accessible name: "Widget options for {title}". */
  protected readonly optionsLabel: Signal<string> = computed(() => this.#l10n.ɵtext('tm.dashboard.options', { title: this.tile().title })());

  /** The options menu's commands in view mode. */
  protected readonly menu: Signal<readonly TmMenuEntry[]> = computed(() => [
    { id: 'refresh', label: this.#l10n.ɵtext('tm.dashboard.refresh')(), icon: 'refresh-cw', action: () => this.refresh() },
  ]);

  /** "View all". */
  protected readonly viewAllText = this.#l10n.ɵtext('tm.dashboard.viewAll');

  /** "View all"'s accessible name, which names its list. */
  protected readonly viewAllLabel: Signal<string> = computed(() => this.#l10n.ɵtext('tm.dashboard.viewAllOf', { title: this.tile().title })());

  /** Whether the status line still waits for the body's first result: a failed first run leaves it empty. */
  protected readonly statusPending = computed(() => this.run()?.status !== 'error');

  /** The status line's time, its stale marker and its omitted-totals note, once a result exists. */
  protected readonly updated: Signal<{
    readonly at: string;
    readonly text: string;
    readonly stale: boolean;
    readonly staleLabel: string;
    readonly totals: string | null;
  } | null> = computed(() => {
    const at = this.#executedAt();
    if (at === null || this.view() !== 'ready') {
      return null;
    }
    const time = this.#time(at);
    const run = this.#run();
    return {
      at,
      text: this.#l10n.ɵtext('tm.dashboard.updated', { time })(),
      stale: run?.status === 'error',
      staleLabel: this.#l10n.ɵtext('tm.dashboard.stale', { time })(),
      totals: run?.totalsOmitted ? this.#l10n.ɵtext('tm.dashboard.totalsOmitted')() : null,
    };
  });

  /** The status line's tooltip: the full description, then the omitted-totals sentence. */
  protected readonly statusTooltip: Signal<string> = computed(() =>
    [this.tile().description, this.updated()?.totals ? this.#l10n.ɵtext('tm.dashboard.totalsOmittedHint')() : null]
      .filter((part): part is string => part !== null && part !== '')
      .join(' '),
  );

  /** The no-access state's heading. */
  protected readonly forbiddenTitle = this.#l10n.ɵtext('tm.errors.forbidden.title');

  /** The no-access state's message, naming the permission the type needs. */
  protected readonly forbiddenMessage: Signal<string> = computed(() => {
    const denied = this.tile().denied;
    const permission = denied === null ? '' : this.#permissions.label(denied[0], denied[1])();
    return this.#l10n.ɵtext('tm.errors.forbidden.message', { permission })();
  });

  /** The not-available state's heading. */
  protected readonly unavailable = this.#l10n.ɵtext('tm.dashboard.unavailable');

  /** The failed load's heading. */
  protected readonly loadFailed = this.#l10n.ɵtext('tm.dashboard.loadFailed');

  /** The failed load's action. */
  protected readonly retryLabel = this.#l10n.ɵtext('tm.errors.retry');

  /** Loads the body whenever the tile shows one that has not loaded. */
  constructor() {
    inject(DestroyRef).onDestroy(() => (this.#destroyed = true));
    effect(() => {
      const component = this.tile().component;
      if (component !== undefined && this.view() === 'loading') {
        untracked(() => void this.#load(component));
      }
    });
  }

  /** Refreshes the body, clearing what it shows first: a user's refresh always clears. */
  refresh(): void {
    this.#tick.update((tick) => ({ generation: tick.generation + 1, clear: true, background: false }));
  }

  /** Tries the failed body's load again. */
  protected retry(): void {
    this.#failed.set(null);
  }

  /**
   * A link's `href`, under the app's base.
   *
   * @param url - The app URL.
   * @returns The `href`.
   */
  protected href(url: string): string {
    return this.#location.prepareExternalUrl(url);
  }

  /**
   * Follows "View all" in the app, and reports it on the home page; a click
   * that opens a new tab or window stays the browser's.
   *
   * @param event - The click.
   * @param url - The list's page.
   */
  protected viewAll(event: MouseEvent, url: string): void {
    const list = this.tile().list?.list;
    if (this.#home && (list === 'to-me' || list === 'by-me')) {
      this.#telemetry.event('tm.home.view-all', { list });
    }
    if (event.button !== 0 || event.ctrlKey || event.metaKey || event.shiftKey || event.altKey) {
      return;
    }
    event.preventDefault();
    void this.#router.navigateByUrl(url);
  }

  /**
   * Records a run the body reported: a result's time is kept until the next
   * result, so a failure over held numbers marks them stale.
   *
   * @param state - The run.
   */
  #report(state: TmWidgetRun): void {
    if (this.#destroyed) {
      return;
    }
    this.#run.set(state);
    if (state.executedAt) {
      this.#executedAt.set(state.executedAt);
    } else if (state.status === 'loading' && untracked(this.#tick).clear) {
      // A cleared refresh drops the held numbers, and the line waits again.
      this.#executedAt.set(null);
    }
  }

  /**
   * A run's time as the status line writes it: the time alone for a run
   * today in the display zone, else the date and the time.
   *
   * @param at - The instant, as ISO 8601.
   * @returns The text.
   */
  #time(at: string): string {
    const today = (instant: string): string => this.#formats.formatDateTime(instant, { timeStyle: 'none', zoneLabel: 'none' });
    return today(at) === today(new Date(this.#now()).toISOString())
      ? this.#formats.formatTime(at)
      : this.#formats.formatDateTime(at, { dateStyle: 'medium' });
  }

  /**
   * Loads a type's body, once at a time.
   *
   * @param component - The type's loader.
   * @returns A promise that settles once the body or the failure shows.
   */
  async #load(component: TmLazyComponent): Promise<void> {
    if (this.#loading === component) {
      return;
    }
    this.#loading = component;
    try {
      const module = await component();
      const body = typeof module === 'function' ? module : module.default;
      loaded.set(component, body);
      if (!this.#destroyed) {
        this.#body.set({ load: component, type: body });
      }
    } catch {
      if (!this.#destroyed) {
        this.#failed.set(component);
      }
    } finally {
      this.#loading = null;
    }
  }
}

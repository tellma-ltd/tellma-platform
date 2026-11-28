// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { ChangeDetectionStrategy, Component, computed, inject, input, signal, viewChildren, type Signal } from '@angular/core';
import {
  TM_APP_DECLARATIONS,
  TmEntities,
  TmLocalization,
  TmPermissions,
  TmUrls,
  type TmDashboardWidgetModel,
  type TmReportArgs,
  type TmTextSlots,
  type TmWidgetDefinition,
  type TmWidgetTypeDeclaration,
} from '@tellma/core';
import { TmAttention } from '@tellma/core/attention';

import { tmPrepareDashboards } from './tm-dashboard-setup';
import { TmDashboardTile, type TmTileModel, type TmTileView } from './tm-dashboard-tile';
import { tmTileList } from './tm-list-tiles';
import { tmPackDashboard, type TmPlacement } from './tm-pack-dashboard';
import type { TmWidgetTick } from './tm-widget-context';

// The dashboard grid (spec 0035 §8): a 12-column grid of fixed 30 px rows
// that keeps the stored positions from 768 px of its own width up and
// stacks the tiles in one column below it. Each widget's type resolves
// against the app's declarations: a type no declaration knows renders "not
// available", and one whose permission the user lacks is denied, hidden by
// default so the rows close over it, or shown with its lock. The tiles
// render in reading order, which is the DOM order, so Tab, screen readers
// and the narrow layout share one order. The geometry is the global
// stylesheet's, which the home skeleton shares. Each tile's state, with the
// time its body last reported, is the grid's `tiles`, which a page's
// context reads; `refreshAll` asks every tile's body to read again, clearing
// what it shows first.

/** One tile's state, for the page context: in reading order. */
export interface TmTileState {
  /** The tile's key. */
  readonly key: string;
  /** The tile's title. */
  readonly title: string;
  /** When the server computed the tile's numbers; `null` before a result, and for a body that never reports a run. */
  readonly executedAt: string | null;
  /** What the tile shows: its body's reported run, else the tile's own state. */
  readonly status: 'idle' | 'loading' | 'ready' | 'waiting' | 'denied' | 'unavailable' | 'error';
}

/** A tile with its place, in reading order. */
interface TmGridTile {
  /** What the tile shows. */
  readonly tile: TmTileModel;
  /** Where it sits. */
  readonly placement: TmPlacement;
}

/** A tile's body state as the page context names it, before its body reports a run. */
const STATUS: Readonly<Record<TmTileView, TmTileState['status']>> = {
  loading: 'loading',
  ready: 'ready',
  error: 'error',
  denied: 'denied',
  unavailable: 'unavailable',
};

/**
 * A grid of dashboard widgets. Each widget's type renders its body inside
 * its tile; the grid lays the tiles out in both layouts once per widget set,
 * so a resize runs no script.
 *
 * @example
 * ```html
 * <tm-dashboard-grid [widgets]="widgets" label="Your work" />
 * ```
 */
@Component({
  selector: 'tm-dashboard-grid',
  imports: [TmDashboardTile],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <!-- The tiles in reading order, each placed by its own custom properties. -->
    @for (entry of layout(); track entry.tile.key) {
      <tm-dashboard-tile
        [tile]="entry.tile"
        [placement]="entry.placement"
        [parameters]="parameters()"
        [presenting]="presenting()"
        [tick]="tick()"
      />
    }
  `,
  host: {
    class: 'tm-dashboard-grid',
    role: 'region',
    '[attr.aria-label]': 'label()',
  },
})
export class TmDashboardGrid {
  readonly #l10n = inject(TmLocalization);
  readonly #permissions = inject(TmPermissions);
  readonly #attention = inject(TmAttention);
  readonly #urls = inject(TmUrls);
  readonly #entities = inject(TmEntities);

  /** The app's widget type declarations, by key. */
  readonly #types: ReadonlyMap<string, TmWidgetTypeDeclaration> = new Map(
    inject(TM_APP_DECLARATIONS).widgets.map((declaration) => [declaration.key, declaration]),
  );

  /** The widgets to lay out, in any order. */
  readonly widgets = input.required<readonly TmDashboardWidgetModel[]>();

  /** The region's accessible name. */
  readonly label = input.required<string>();

  /** Whether a tile whose permission the user lacks shows its lock, or leaves the grid; default `'hide'`. */
  readonly denied = input<'show' | 'hide'>('hide');

  /** The dashboard's supplied arguments, which every tile's body reads; default none. */
  readonly parameters = input<TmReportArgs>({});

  /** Whether the dashboard is presented, which shows no tile menu; default `false`. */
  readonly presenting = input(false);

  /** The refresh every tile's body last received from the grid. */
  protected readonly tick = signal<TmWidgetTick>({ generation: 0, clear: false, background: false });

  /** The tiles, in reading order, with their places. */
  protected readonly layout: Signal<readonly TmGridTile[]> = computed(() => {
    // --- Each widget's type: its body, its header and its access ---
    const tiles = new Map<string, TmTileModel>();
    const shown: TmDashboardWidgetModel[] = [];
    for (const widget of this.widgets()) {
      const declaration = this.#types.get(widget.type) as (TmWidgetTypeDeclaration & Partial<TmWidgetDefinition>) | undefined;
      const requires = declaration?.requires;
      const denied = requires !== undefined && !this.#permissions.can(requires[0], requires[1])() ? requires : null;
      if (denied !== null && this.denied() === 'hide') {
        continue;
      }
      // A list type shows its description only in edit mode, which the grid does not have yet.
      const list = tmTileList(widget.type, this.#attention, this.#urls, this.#entities);
      tiles.set(widget.key, {
        key: widget.key,
        type: widget.type,
        title: this.#title(widget, declaration),
        description: list === null ? this.#text(widget.description, widget.descriptionKey) : null,
        component: declaration?.component,
        denied,
        status: declaration?.status === true,
        list,
        size: { w: widget.w, h: widget.h },
        settings: widget.settings ?? null,
      });
      shown.push(widget);
    }

    // --- Both layouts at once, in reading order ---
    return tmPackDashboard(shown).map((placement) => ({ tile: tiles.get(placement.key)!, placement }));
  });

  /** The tiles' rendered components, in reading order. */
  private readonly tileViews = viewChildren(TmDashboardTile);

  /** Each laid-out tile's state, in reading order: a tile not yet rendered is loading; a hidden one is absent. */
  readonly tiles: Signal<readonly TmTileState[]> = computed(() => {
    // Every tile the grid laid out reports, so a reader waiting on them all
    // waits on the visible tiles only, and a tile still rendering reads as
    // loading rather than missing.
    const views = new Map(this.tileViews().map((view) => [view.tile().key, view]));
    return this.layout().map(({ tile }) => {
      const view = views.get(tile.key);
      return {
        key: tile.key,
        title: tile.title,
        executedAt: view?.executedAt() ?? null,
        status: view === undefined ? 'loading' : (view.run()?.status ?? STATUS[view.view()]),
      };
    });
  });

  /** Registers the entry point's strings and icons. */
  constructor() {
    tmPrepareDashboards();
  }

  /** Asks every tile's body to read again, each clearing what it shows first. */
  refreshAll(): void {
    this.tick.update((tick) => ({ generation: tick.generation + 1, clear: true, background: false }));
  }

  /**
   * A tile's title: the first that exists of the widget's own title, in the
   * active content slot, else slot 1; its title's string key; and its type's
   * label. A type no declaration knows is named by its key.
   *
   * @param widget - The widget.
   * @param declaration - Its type's declaration, if one knows it.
   * @returns The title.
   */
  #title(widget: TmDashboardWidgetModel, declaration: Partial<TmWidgetDefinition> | undefined): string {
    return this.#text(widget.title, widget.titleKey ?? declaration?.labelKey) ?? widget.type;
  }

  /**
   * A text a widget carries: its own content slots, the active slot else
   * slot 1, else its string key's text.
   *
   * @param slots - The widget's own text, if any.
   * @param key - The string key to fall back on, if any.
   * @returns The text, or `null` when neither gives one.
   */
  #text(slots: TmTextSlots | undefined, key: string | undefined): string | null {
    const own = slots && ((slots[this.#l10n.contentSlot() - 1] ?? '').trim() || (slots[0] ?? '').trim());
    if (own) {
      return own;
    }
    return key === undefined ? null : this.#l10n.ɵtext(key)();
  }
}

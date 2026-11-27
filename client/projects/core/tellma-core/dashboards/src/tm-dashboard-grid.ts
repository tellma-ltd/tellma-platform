// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { ChangeDetectionStrategy, Component, computed, inject, input, viewChildren, type Signal } from '@angular/core';
import {
  TM_APP_DECLARATIONS,
  TmLocalization,
  TmPermissions,
  type TmDashboardWidgetModel,
  type TmWidgetDefinition,
  type TmWidgetTypeDeclaration,
} from '@tellma/core';

import { tmPrepareDashboards } from './tm-dashboard-setup';
import { TmDashboardTile, type TmTileModel, type TmTileView } from './tm-dashboard-tile';
import { tmPackDashboard, type TmPlacement } from './tm-pack-dashboard';

// The dashboard grid (spec 0035 §8): a 12-column grid of fixed 30 px rows
// that keeps the stored positions from 768 px of its own width up and
// stacks the tiles in one column below it. Each widget's type resolves
// against the app's declarations: a type no declaration knows renders "not
// available", and one whose permission the user lacks is denied, hidden by
// default so the rows close over it, or shown with its lock. The tiles
// render in reading order, which is the DOM order, so Tab, screen readers
// and the narrow layout share one order. The geometry is the global
// stylesheet's, which the home skeleton shares.

/** One tile's state, for the page context: in reading order. */
export interface TmTileState {
  /** The tile's key. */
  readonly key: string;
  /** The tile's title. */
  readonly title: string;
  /** When the server computed the tile's numbers; `null` for a body that never reports a run. */
  readonly executedAt: string | null;
  /** What the tile shows. */
  readonly status: 'idle' | 'loading' | 'ready' | 'waiting' | 'denied' | 'unavailable' | 'error';
}

/** A tile with its place, in reading order. */
interface TmGridTile {
  /** What the tile shows. */
  readonly tile: TmTileModel;
  /** Where it sits. */
  readonly placement: TmPlacement;
}

/** A tile's body state as the page context names it. */
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
      <tm-dashboard-tile [tile]="entry.tile" [placement]="entry.placement" />
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

  /** The tiles, in reading order, with their places. */
  protected readonly layout: Signal<readonly TmGridTile[]> = computed(() => {
    // --- Each widget's type: its body, its title and its access ---
    const tiles = new Map<string, TmTileModel>();
    const shown: TmDashboardWidgetModel[] = [];
    for (const widget of this.widgets()) {
      const declaration = this.#types.get(widget.type) as (TmWidgetTypeDeclaration & Partial<TmWidgetDefinition>) | undefined;
      const requires = declaration?.requires;
      const denied = requires !== undefined && !this.#permissions.can(requires[0], requires[1])() ? requires : null;
      if (denied !== null && this.denied() === 'hide') {
        continue;
      }
      tiles.set(widget.key, { key: widget.key, title: this.#title(widget, declaration), component: declaration?.component, denied });
      shown.push(widget);
    }

    // --- Both layouts at once, in reading order ---
    return tmPackDashboard(shown).map((placement) => ({ tile: tiles.get(placement.key)!, placement }));
  });

  /** The tiles' rendered components, in reading order. */
  private readonly tileViews = viewChildren(TmDashboardTile);

  /** Each tile's state, in reading order. */
  readonly tiles: Signal<readonly TmTileState[]> = computed(() =>
    this.tileViews().map((tile) => ({ key: tile.tile().key, title: tile.tile().title, executedAt: null, status: STATUS[tile.view()] })),
  );

  /** Registers the entry point's strings and icons. */
  constructor() {
    tmPrepareDashboards();
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
    const slots = widget.title;
    const own = slots && ((slots[this.#l10n.contentSlot() - 1] ?? '').trim() || (slots[0] ?? '').trim());
    if (own) {
      return own;
    }
    const key = widget.titleKey ?? declaration?.labelKey;
    return key === undefined ? widget.type : this.#l10n.ɵtext(key)();
  }
}

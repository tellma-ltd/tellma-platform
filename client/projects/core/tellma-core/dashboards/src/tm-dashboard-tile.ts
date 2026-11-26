// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { NgComponentOutlet } from '@angular/common';
import {
  ChangeDetectionStrategy,
  Component,
  computed,
  DestroyRef,
  effect,
  inject,
  input,
  signal,
  untracked,
  ViewEncapsulation,
  type Signal,
  type Type,
} from '@angular/core';
import { TmLocalization, TmPermissions, type TmLazyComponent, type TmSecurableRef } from '@tellma/core';
import { TmButton } from '@tellma/core-ui/button';
import { TmCard } from '@tellma/core-ui/card';
import { TmEmptyState } from '@tellma/core-ui/empty-state';
import { TmSkeleton } from '@tellma/core-ui/skeleton';

import type { TmPlacement } from './tm-pack-dashboard';

// One tile of the grid (spec 0035 §8.4, §8.6): a labelled section on a
// card whose flush body fills the tile and scrolls inside it, so a tile's
// height never depends on its data. The header never changes with state.
// The tile's own states are its body's absence: a type no declaration knows
// is not available, and a type whose permission the user lacks is denied
// and never loads its body. Otherwise the body loads from its type's
// declaration, behind five skeleton rows, and a body that fails to load
// offers to try again.

/** What a tile shows in its body. */
export type TmTileView = 'loading' | 'ready' | 'error' | 'denied' | 'unavailable';

/** What a tile's grid resolved for it. */
export interface TmTileModel {
  /** The tile's key. */
  readonly key: string;
  /** The tile's title, already resolved. */
  readonly title: string;
  /** The body's loader; `undefined` when no declaration knows the type. */
  readonly component: TmLazyComponent | undefined;
  /** The permission the type requires, when the user lacks it; the tile is denied. */
  readonly denied: TmSecurableRef | null;
}

/** The bodies already loaded, by loader, so a later tile of a type renders its body in its first frame. */
const loaded = new WeakMap<TmLazyComponent, Type<unknown>>();

/** One dashboard tile: its card, its title, and its body or its state. */
@Component({
  selector: 'tm-dashboard-tile',
  imports: [NgComponentOutlet, TmButton, TmCard, TmEmptyState, TmSkeleton],
  changeDetection: ChangeDetectionStrategy.OnPush,
  // The card's own elements are filled to the tile's height, so the rules
  // reach into its markup, each one through child combinators so a card
  // nested in a body keeps its own layout.
  encapsulation: ViewEncapsulation.None,
  template: `
    <section class="tm-dashboard-tile__section" [attr.aria-label]="tile().title">
      <tm-card [heading]="tile().title" flush>
        @switch (view()) {
          @case ('ready') {
            <!-- The type's body. -->
            <ng-container *ngComponentOutlet="body()" />
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
  readonly #permissions = inject(TmPermissions);

  /** What the grid resolved for the tile. */
  readonly tile = input.required<TmTileModel>();

  /** The tile's place on the grid. */
  readonly placement = input.required<TmPlacement>();

  /** The body loaded for this tile, with the loader it came from. */
  readonly #body = signal<{ readonly load: TmLazyComponent; readonly type: Type<unknown> } | null>(null);

  /** The loader whose chunk failed to load, until the user tries again. */
  readonly #failed = signal<TmLazyComponent | null>(null);

  /** The loader whose chunk is loading. */
  #loading: TmLazyComponent | null = null;

  /** Whether the tile has been destroyed, so a late load renders nothing. */
  #destroyed = false;

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

  /** The body to render: the one loaded for this tile's type, by any tile. */
  protected readonly body: Signal<Type<unknown> | null> = computed(() => {
    const component = this.tile().component;
    if (component === undefined) {
      return null;
    }
    const held = this.#body();
    return loaded.get(component) ?? (held?.load === component ? held.type : null);
  });

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

  /** Tries the failed body's load again. */
  protected retry(): void {
    this.#failed.set(null);
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

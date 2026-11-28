// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { NgComponentOutlet } from '@angular/common';
import { afterNextRender, ChangeDetectionStrategy, Component, computed, DestroyRef, inject, Injector, signal, type Type } from '@angular/core';
import {
  TM_APP_DECLARATIONS,
  TmLocalization,
  TmPageContexts,
  ɵTmPageFocus,
  ɵTmPageSkeleton,
  type TmEntityType,
  type TmLazyComponent,
  type TmVisibleData,
  type TmVisibleRow,
} from '@tellma/core';
import { TmAttention } from '@tellma/core/attention';
import { TmAttentionText } from '@tellma/core/attention-rows';
import { TmRecents } from '@tellma/core/recents';

import { TM_HOME_SURFACE } from './tm-dashboard-tile';
import { TmHomePage } from './tm-home-page';
import { TmHomeRendered } from './tm-home-telemetry';

// The tenant home's host (spec 0035 §15.1, §16): the route renders it, and
// it renders the winning `home` hatch, the last feature's that sets one,
// else the platform's home page. A hatch loads behind the home skeleton, in
// the geometry the route host drew, and is kept once loaded, so a later
// visit renders it in its first frame. While it loads, the host holds the
// arrival focus, which the route would otherwise give the skeleton, until
// the hatch has rendered (spec 0024 §4.4). A hatch whose chunk does not
// load leaves the platform's home page in its place, so every entry still
// lands on a working home. The host owns what stays the same whichever
// page renders: the `home` page context, from the documents the lists
// show, and the `tm.home.rendered` timing.

/** The hatches already loaded, by loader. */
const loaded = new WeakMap<TmLazyComponent, Type<unknown>>();

/** The rows of each list the page context carries: three lists of ten. */
const LIST_ROWS = 10;

/** The tenant home: the app's `home` hatch, else the platform's home page. */
@Component({
  selector: 'tm-home-host',
  imports: [NgComponentOutlet, ɵTmPageSkeleton],
  changeDetection: ChangeDetectionStrategy.OnPush,
  // Every page under the host is the home's: its lists report the home's telemetry, and it is timed once.
  providers: [{ provide: TM_HOME_SURFACE, useValue: true }, TmHomeRendered],
  template: `
    @if (page(); as page) {
      <!-- The hatch, or the platform's home page. -->
      <ng-container *ngComponentOutlet="page" />
    } @else {
      <!-- The hatch's chunk is loading: the home skeleton the route drew. -->
      <tm-page-skeleton kind="home" />
    }
  `,
  styles: `
    /* The host fills the route's box, so the page can fill main. */
    :host {
      display: block;
      block-size: 100%;
    }
  `,
})
export class TmHomeHost {
  readonly #attention = inject(TmAttention);
  readonly #recents = inject(TmRecents);
  readonly #text = inject(TmAttentionText);
  readonly #rendered = inject(TmHomeRendered, { self: true });

  /** The app's `home` hatch, or `null` for the platform's home page. */
  readonly #hatch = inject(TM_APP_DECLARATIONS).home;

  /** The component to render, once known. */
  protected readonly page = signal<Type<unknown> | null>(this.#hatch === null ? TmHomePage : (loaded.get(this.#hatch) ?? null));

  /** Publishes the home's context, and loads the hatch, unless it is the platform's page or already loaded. */
  constructor() {
    this.#publish();
    const hatch = this.#hatch;
    const injector = inject(Injector);
    if (hatch !== null && this.page() !== null) {
      // A hatch loaded on an earlier visit renders in this first frame, and is timed after it.
      afterNextRender({ read: () => this.#rendered.emit(true) }, { injector });
    } else if (hatch !== null) {
      // --- The focus waits for the hatch: taken before the route's hold ends, given back once it renders ---
      const release = inject(ɵTmPageFocus).ɵhold();
      let destroyed = false;
      inject(DestroyRef).onDestroy(() => {
        destroyed = true;
        release();
      });
      const show = (component: Type<unknown>, isHatch: boolean): void => {
        if (destroyed) {
          return;
        }
        this.page.set(component);
        afterNextRender(
          {
            read: () => {
              release();
              // The platform's page, in a failed hatch's place, times itself once its tiles settle.
              if (isHatch) {
                this.#rendered.emit(true);
              }
            },
          },
          { injector },
        );
      };
      hatch().then(
        (module) => {
          const component = typeof module === 'function' ? module : module.default;
          loaded.set(hatch, component);
          show(component, true);
        },
        () => show(TmHomePage, false),
      );
    }
  }

  /** Registers the `home` page context, which replaces the route host's default one. */
  #publish(): void {
    inject(TmPageContexts).register({
      kind: 'home',
      routeTemplate: '/:t',
      title: inject(TmLocalization).ɵtext('tm.home.title'),
      status: signal<'ready'>('ready').asReadonly(),
      state: computed(() => ({ hatch: this.#hatch !== null && this.page() !== TmHomePage })),
      visible: computed(() => this.#visible()),
      errors: signal([]).asReadonly(),
      actions: signal([]).asReadonly(),
    });
  }

  /**
   * The home's visible data: the three lists' rows, at most ten each, with
   * the list, the title, the detail and when; a row's id is its
   * assignment's, and a recent page has none.
   *
   * @returns The rows.
   */
  #visible(): TmVisibleData {
    const summary = this.#attention.assignments();
    const assignment = (list: 'to-me' | 'by-me', row: TmEntityType<'core.Assignment'>): TmVisibleRow => {
      const caption = this.#text.caption(row)();
      const id = row['id'];
      return {
        id: typeof id === 'number' ? id : null,
        cells: { list, title: caption.title, detail: caption.detail, when: typeof row['assignedAt'] === 'string' ? row['assignedAt'] : null },
      };
    };
    const rows: TmVisibleRow[] = [
      ...(summary?.latest ?? []).slice(0, LIST_ROWS).map((row) => assignment('to-me', row)),
      ...(summary?.latestByMe ?? []).slice(0, LIST_ROWS).map((row) => assignment('by-me', row)),
      ...this.#recents
        .pages()
        .slice(0, LIST_ROWS)
        .map((page): TmVisibleRow => ({ id: null, cells: { list: 'recents', title: page.title(), detail: page.url, when: new Date(page.visitedAt).toISOString() } })),
    ];
    return {
      columns: [
        { path: 'list', label: 'list' },
        { path: 'title', label: 'title' },
        { path: 'detail', label: 'detail' },
        { path: 'when', label: 'when' },
      ],
      rows,
      total: rows.length,
      truncated: false,
    };
  }
}

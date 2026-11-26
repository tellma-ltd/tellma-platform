// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { NgComponentOutlet } from '@angular/common';
import { ChangeDetectionStrategy, Component, DestroyRef, inject, signal, type Type } from '@angular/core';
import { TM_APP_DECLARATIONS, ɵTmPageSkeleton, type TmLazyComponent } from '@tellma/core';

import { TmHomePage } from './tm-home-page';

// The tenant home's host (spec 0035 §15.1): the route renders it, and it
// renders the winning `home` hatch, the last feature's that sets one, else
// the platform's home page. A hatch loads behind the home skeleton, in the
// geometry the route host drew, and is kept once loaded, so a later visit
// renders it in its first frame. A hatch whose chunk does not load leaves
// the platform's home page in its place, so every entry still lands on a
// working home.

/** The hatches already loaded, by loader. */
const loaded = new WeakMap<TmLazyComponent, Type<unknown>>();

/** The tenant home: the app's `home` hatch, else the platform's home page. */
@Component({
  selector: 'tm-home-host',
  imports: [NgComponentOutlet, ɵTmPageSkeleton],
  changeDetection: ChangeDetectionStrategy.OnPush,
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
  /** The app's `home` hatch, or `null` for the platform's home page. */
  readonly #hatch = inject(TM_APP_DECLARATIONS).home;

  /** The component to render, once known. */
  protected readonly page = signal<Type<unknown> | null>(this.#hatch === null ? TmHomePage : (loaded.get(this.#hatch) ?? null));

  /** Loads the hatch, unless it is the platform's page or already loaded. */
  constructor() {
    const hatch = this.#hatch;
    if (hatch !== null && this.page() === null) {
      let destroyed = false;
      inject(DestroyRef).onDestroy(() => (destroyed = true));
      hatch().then(
        (module) => {
          const component = typeof module === 'function' ? module : module.default;
          loaded.set(hatch, component);
          if (!destroyed) {
            this.page.set(component);
          }
        },
        () => {
          if (!destroyed) {
            this.page.set(TmHomePage);
          }
        },
      );
    }
  }
}

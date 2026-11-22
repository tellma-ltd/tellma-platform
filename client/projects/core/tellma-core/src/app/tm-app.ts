// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { ChangeDetectionStrategy, Component, inject, Injectable, signal } from '@angular/core';
import { NavigationEnd, type NavigationError, Router, RouterOutlet } from '@angular/router';

import { TM_API_UPDATES } from '../api/tm-api-seams';
import { TmRouteError, type TmRouteErrorKind } from '../routes/tm-route-error';

// The app's root component and the navigation failures it shows (spec 0024
// §4.4, §9.2). `tm-app` renders the router outlet; the shells are route
// components. A navigation that fails because a chunk did not load, such as
// a shell's after a deployment, asks the service worker for an update and
// shows `client-outdated` or `offline` until the next navigation lands.

/** What a module that failed to load throws, in each engine and bundler. */
const CHUNK_ERROR = /dynamically imported module|Importing a module script failed|ChunkLoadError|Loading chunk/i;

/** The failure of the last navigation, when a chunk did not load. */
@Injectable({ providedIn: 'root' })
export class TmNavigationFailure {
  readonly #updates = inject(TM_API_UPDATES);
  readonly #router = inject(Router);

  /** The address of the navigation that failed. */
  #url = '/';

  /** The view to show, or `null` while navigations succeed. */
  readonly #view = signal<TmRouteErrorKind | null>(null);

  /** The view to show, or `null` while navigations succeed. */
  readonly view = this.#view.asReadonly();

  /** Starts following the router, so a landed navigation clears the view. */
  constructor() {
    this.#router.events.subscribe((event) => event instanceof NavigationEnd && this.#view.set(null));
  }

  /**
   * Handles a failed navigation: a chunk that did not load asks the worker
   * for an update. Other errors are the global error handler's.
   *
   * @param failure - The router's error event.
   */
  ɵfailed(failure: NavigationError): void {
    if (CHUNK_ERROR.test(String((failure.error as { message?: unknown } | null)?.message ?? failure.error))) {
      this.#url = failure.url;
      void this.#updates.check().then((update) => this.#view.set(update === 'ready' || update === 'failed' ? 'client-outdated' : 'offline'));
    }
  }

  /** Runs the failed navigation again. */
  retry(): void {
    void this.#router.navigateByUrl(this.#url);
  }
}

/**
 * The root component of every Tellma app, in the `tm-app` element of the
 * distribution's `index.html`: the router outlet, and a failed navigation's
 * error view.
 */
@Component({
  selector: 'tm-app',
  imports: [RouterOutlet, TmRouteError],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <!-- A navigation that could not load its chunk; the routed page stays below. -->
    @if (failure(); as kind) {
      <tm-route-error [kind]="kind" (retry)="retry()" />
    }
    <router-outlet />
  `,
})
export class TmApp {
  readonly #failures = inject(TmNavigationFailure);

  /** A failed navigation's view, or `null`. */
  protected readonly failure = this.#failures.view;

  /** Runs the failed navigation again. */
  protected retry(): void {
    this.#failures.retry();
  }
}

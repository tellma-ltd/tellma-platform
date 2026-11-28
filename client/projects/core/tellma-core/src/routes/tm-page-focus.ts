// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { afterNextRender, DOCUMENT, inject, Injectable, Injector } from '@angular/core';
import { type ActivatedRouteSnapshot, NavigationEnd, Router } from '@angular/router';

import type { TmRouteDataInternal } from './tm-routes';

// Focus after a navigation (spec 0024 §4.4). On a path change, never a
// query-only one and never the first navigation, focus moves to the page's
// `[tmPageTitle]` heading once the page renders, else to `main`. A path
// change the page absorbs in place, such as a settings tab, moves no focus:
// focus stays on the tab list, as spec 0032 §7.1 says. A route host holds
// the move until its page has rendered, and a family's host that loads its
// own page, such as the home's hatch, holds it on until that page renders;
// any other route settles it after the navigation's render. An app's own
// navigation whose state carries `tmPageFocus: false` moves no focus: its
// page places focus itself, as a saved record's page focuses Edit (spec 0031
// §8.3). The router restores that state on Back and Forward, which are no
// longer that navigation, so those move focus as any path change does.

/** The path of a router URL, without its query and fragment. */
function pathOf(url: string): string {
  return url.split(/[?#]/, 1)[0]!;
}

/** A router state's deepest route down the primary outlet. */
function deepest(root: ActivatedRouteSnapshot): ActivatedRouteSnapshot {
  let route = root;
  while (route.firstChild) {
    route = route.firstChild;
  }
  return route;
}

/**
 * Whether a navigation stays on the same route and changes only parameters
 * the page absorbs in place.
 */
function inPlace(from: ActivatedRouteSnapshot, to: ActivatedRouteSnapshot): boolean {
  const absorbed = new Set((to.data as TmRouteDataInternal).ɵinPlace ?? []);
  return (
    from.routeConfig === to.routeConfig &&
    [...new Set([...Object.keys(from.params), ...Object.keys(to.params)])].every((name) => absorbed.has(name) || from.params[name] === to.params[name])
  );
}

/** Moves focus to the routed page's title after each path change. */
@Injectable({ providedIn: 'root' })
export class TmPageFocus {
  readonly #document = inject(DOCUMENT);
  readonly #injector = inject(Injector);
  readonly #router = inject(Router);

  /** The path of the last navigation, or `null` before the first. */
  #path: string | null = null;

  /** The deepest route of the last navigation, or `null` before the first. */
  #route: ActivatedRouteSnapshot | null = null;

  /** Whether a path change waits for its page. */
  #pending = false;

  /** The route hosts still loading their page. */
  #loading = 0;

  /** Starts following the router's navigations. Internal: the boot sequence starts it. */
  ɵstart(): void {
    this.#router.events.subscribe((event) => {
      if (!(event instanceof NavigationEnd)) {
        return;
      }
      const path = pathOf(event.urlAfterRedirects);
      const route = deepest(this.#router.routerState.snapshot.root);
      const navigation = this.#router.lastSuccessfulNavigation();
      const own = navigation?.trigger === 'imperative' && navigation.extras.state?.['tmPageFocus'] === false;
      this.#pending = !own && (this.#pending || (this.#path !== null && path !== this.#path && !inPlace(this.#route!, route)));
      this.#path = path;
      this.#route = route;
      // A route without a host of its own has rendered by the next frame.
      afterNextRender({ read: () => this.#loading === 0 && this.#settle() }, { injector: this.#injector });
    });
  }

  /**
   * Holds the focus move while a route host, or a family's host inside it, loads its page.
   *
   * @returns Releases the hold once the page has rendered, moving focus when a path change waits.
   */
  ɵhold(): () => void {
    this.#loading++;
    let released = false;
    return () => {
      if (!released) {
        released = true;
        this.#loading--;
        this.#settle();
      }
    };
  }

  /** Moves focus to the page's title, else to `main`, when a path change waits. */
  #settle(): void {
    if (!this.#pending || this.#loading > 0) {
      return;
    }
    this.#pending = false;
    const target =
      this.#document.querySelector<HTMLElement>('main [tmPageTitle]') ??
      this.#document.querySelector<HTMLElement>('[tmPageTitle]') ??
      this.#document.querySelector<HTMLElement>('main');
    if (target) {
      if (!target.hasAttribute('tabindex')) {
        target.setAttribute('tabindex', '-1');
      }
      target.focus({ preventScroll: true });
    }
  }
}

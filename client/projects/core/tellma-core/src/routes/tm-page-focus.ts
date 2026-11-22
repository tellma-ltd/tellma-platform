// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { afterNextRender, DOCUMENT, inject, Injectable, Injector } from '@angular/core';
import { NavigationEnd, Router } from '@angular/router';

// Focus after a navigation (spec 0024 §4.4). On a path change, never a
// query-only one and never the first navigation, focus moves to the page's
// `[tmPageTitle]` heading once the page renders, else to `main`. A route host
// holds the move until its page has rendered; any other route settles it
// after the navigation's render.

/** The path of a router URL, without its query and fragment. */
function pathOf(url: string): string {
  return url.split(/[?#]/, 1)[0]!;
}

/** Moves focus to the routed page's title after each path change. */
@Injectable({ providedIn: 'root' })
export class TmPageFocus {
  readonly #document = inject(DOCUMENT);
  readonly #injector = inject(Injector);
  readonly #router = inject(Router);

  /** The path of the last navigation, or `null` before the first. */
  #path: string | null = null;

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
      this.#pending ||= this.#path !== null && path !== this.#path;
      this.#path = path;
      // A route without a host of its own has rendered by the next frame.
      afterNextRender({ read: () => this.#loading === 0 && this.#settle() }, { injector: this.#injector });
    });
  }

  /**
   * Holds the focus move while a route host loads its page.
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

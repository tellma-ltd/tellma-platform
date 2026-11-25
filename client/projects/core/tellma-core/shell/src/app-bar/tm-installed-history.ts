// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { Location } from '@angular/common';
import { DestroyRef, DOCUMENT, inject, Injectable, InjectionToken, type Signal, signal } from '@angular/core';
import { NavigationEnd, NavigationStart, Router } from '@angular/router';

// Back and forward in installed mode (spec 0027 §7.3). An installed app runs
// in its own window without the browser's buttons, so the app bar draws its
// own from `md` up. Installed mode is a display-mode media query (or iOS's
// `navigator.standalone`), followed live; `minimal-ui` keeps the browser's
// buttons, so it does not count.
//
// Whether each way is open comes from the Navigation API where the browser
// has it. Without it (Safari before 26.2) the router's page id stands in for
// the spec's depth counter: the router writes it into every history entry
// (`canceledNavigationResolution: 'computed'`), adding 1 on a push and
// keeping it on a replace, so the entry's id is how far Back can go, and the
// highest id since the last push is how far Forward can. A reload forgets
// the highest id, as the spec's counter forgets its count.

/** The display modes in which the browser draws no history buttons. */
const INSTALLED = '(display-mode: standalone), (display-mode: fullscreen), (display-mode: window-controls-overlay)';

/** What the history buttons read from the browser. Internal: a test replaces it. */
export interface TmHistoryHost {
  /** The installed-mode media query. */
  readonly installed: MediaQueryList | null;
  /** Whether iOS runs the app from the home screen (`navigator.standalone`). */
  readonly standalone: boolean;
  /** The Navigation API, where the browser has it. */
  readonly navigation: Navigation | null;
}

/** What the history buttons read from the browser: the document's window. Internal. */
export const TM_HISTORY_HOST = new InjectionToken<TmHistoryHost>('TM_HISTORY_HOST', {
  providedIn: 'root',
  factory: (): TmHistoryHost => {
    const window = inject(DOCUMENT).defaultView;
    return {
      installed: window?.matchMedia?.(INSTALLED) ?? null,
      standalone: (window?.navigator as { readonly standalone?: boolean } | undefined)?.standalone === true,
      navigation: (window as { readonly navigation?: Navigation } | null)?.navigation ?? null,
    };
  },
});

/** The router's page id in a history entry's state. */
function pageId(state: unknown): number {
  const id = (state as { readonly ɵrouterPageId?: unknown } | null)?.ɵrouterPageId;
  return typeof id === 'number' ? id : 0;
}

/**
 * Leaves a history move to the router, which follows it on `popstate`. A
 * move with no entry that way, or one a newer navigation interrupts,
 * rejects both of its promises; neither is an error here.
 */
function settle(result: NavigationResult): void {
  result.committed?.catch(() => undefined);
  result.finished?.catch(() => undefined);
}

/**
 * Whether the app runs installed, and whether history can move back or
 * forward from the current entry: the app bar's Back and Forward buttons.
 * Provided by the tenant shell, for the tenant's life in the tab.
 */
@Injectable()
export class TmInstalledHistory {
  readonly #host = inject(TM_HISTORY_HOST);
  readonly #location = inject(Location);
  readonly #installed = signal(this.#host.standalone || (this.#host.installed?.matches ?? false));
  readonly #canGoBack = signal(false);
  readonly #canGoForward = signal(false);

  /** Whether the app runs in its own window, without the browser's buttons. */
  readonly installed: Signal<boolean> = this.#installed.asReadonly();

  /** Whether Back has an entry to go to. */
  readonly canGoBack: Signal<boolean> = this.#canGoBack.asReadonly();

  /** Whether Forward has an entry to go to. */
  readonly canGoForward: Signal<boolean> = this.#canGoForward.asReadonly();

  /** Creates the service, which follows the display mode and the history entries until the shell ends. */
  constructor() {
    const destroyRef = inject(DestroyRef);

    // --- Installed mode, followed live ---
    const installed = this.#host.installed;
    if (installed !== null && !this.#host.standalone) {
      const change = (): void => this.#installed.set(installed.matches);
      installed.addEventListener('change', change);
      destroyRef.onDestroy(() => installed.removeEventListener('change', change));
    }

    // --- With the Navigation API: the browser's own answers, re-read on every entry change ---
    const navigation = this.#host.navigation;
    if (navigation !== null) {
      const read = (): void => {
        this.#canGoBack.set(navigation.canGoBack);
        this.#canGoForward.set(navigation.canGoForward);
      };
      read();
      navigation.addEventListener('currententrychange', read);
      destroyRef.onDestroy(() => navigation.removeEventListener('currententrychange', read));
      return;
    }

    // --- Without it: the router's page ids ---
    // A push raises the highest id to the new entry's; a history move keeps it.
    let highest = pageId(this.#location.getState());
    let popped = false;
    const subscription = inject(Router).events.subscribe((event) => {
      if (event instanceof NavigationStart) {
        popped = event.navigationTrigger === 'popstate';
      } else if (event instanceof NavigationEnd) {
        const current = pageId(this.#location.getState());
        highest = popped ? Math.max(highest, current) : current;
        this.#canGoBack.set(current > 0);
        this.#canGoForward.set(current < highest);
      }
    });
    this.#canGoBack.set(highest > 0);
    destroyRef.onDestroy(() => subscription.unsubscribe());
  }

  /** Moves one entry back. */
  back(): void {
    const navigation = this.#host.navigation;
    if (navigation === null) {
      this.#location.back();
    } else {
      settle(navigation.back());
    }
  }

  /** Moves one entry forward. */
  forward(): void {
    const navigation = this.#host.navigation;
    if (navigation === null) {
      this.#location.forward();
    } else {
      settle(navigation.forward());
    }
  }
}

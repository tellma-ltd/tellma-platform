// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { DestroyRef, inject, Injectable, signal, type Signal, type Type } from '@angular/core';
import { NavigationEnd, Router } from '@angular/router';

// What the tenant shell's `main` shows (spec 0027 §7.5). Normally the route
// host; while a tenant-wide state replaces it (the tenant-state page that the
// error presenter shows for an unavailable tenant), the component it names.
// The replacement belongs to the path it was shown on: the next navigation
// that lands on another path gives `main` back to the route host, while a
// change of query or fragment alone keeps it.

/** A component that `main` shows in place of the route host, with its inputs. */
export interface TmShellMainOverride {
  /** The component. */
  readonly component: Type<unknown>;
  /** Its inputs, by name. */
  readonly inputs?: Readonly<Record<string, unknown>>;
}

/** A URL's path: what is left without its query and fragment. */
function pathOf(url: string): string {
  return url.split(/[?#]/, 1)[0];
}

/**
 * What the tenant shell's `main` shows: the route host, or a component that
 * replaces it, such as the page that says the tenant is suspended. A root
 * service; the tenant shell renders {@link TmShellMain.override}.
 *
 * A replacement lasts until {@link TmShellMain.restore}, or until a
 * navigation lands on another path.
 *
 * @example
 * ```ts
 * inject(TmShellMain).replace(TenantStatePage, { state: 'suspended' });
 * ```
 */
@Injectable({ providedIn: 'root' })
export class TmShellMain {
  readonly #router = inject(Router);
  readonly #override = signal<TmShellMainOverride | null>(null);
  // The path the replacement was shown for, or `null` while the route host shows.
  #path: string | null = null;

  /** What `main` shows instead of the route host, or `null` for the route host. */
  readonly override: Signal<TmShellMainOverride | null> = this.#override.asReadonly();

  /** Creates the service; a navigation to another path restores the route host. */
  constructor() {
    const subscription = this.#router.events.subscribe((event) => {
      if (event instanceof NavigationEnd && this.#path !== null && pathOf(event.urlAfterRedirects) !== this.#path) {
        this.restore();
      }
    });
    inject(DestroyRef).onDestroy(() => subscription.unsubscribe());
  }

  /**
   * Shows `component` in `main` instead of the route host, until
   * {@link TmShellMain.restore} or the next navigation to another path.
   * During a navigation, the replacement belongs to the path it is going to.
   *
   * @param component - The component.
   * @param inputs - Its inputs, by name.
   */
  replace(component: Type<unknown>, inputs?: Readonly<Record<string, unknown>>): void {
    // The page a replacement is asked for during a navigation is the one
    // being navigated to, so that navigation's own end keeps it.
    const navigation = this.#router.currentNavigation();
    const target = navigation?.finalUrl ?? navigation?.extractedUrl;
    this.#path = pathOf(target === undefined ? this.#router.url : this.#router.serializeUrl(target));
    this.#override.set(inputs === undefined ? { component } : { component, inputs });
  }

  /** Gives `main` back to the route host. */
  restore(): void {
    this.#path = null;
    this.#override.set(null);
  }
}

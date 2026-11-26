// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { DOCUMENT, inject, Injectable, InjectionToken, Injector } from '@angular/core';
import { ɵTM_UPDATES_LOAD, type ɵTmApiUpdateCheck } from '@tellma/core';

// Reload of the error views (spec 0024 §11 step 2): with a service worker
// controlling the page, Reload asks the update service, in its own lazy
// entry point, to download the new version first. With no worker in the
// way, a plain reload already gets the new version, so it runs at once
// without loading anything; an app with no update service, such as a test
// app, reloads the same way.

/** The page behind the error views' Reload. Internal: a test replaces it. */
export interface TmReloadHost {
  /** Whether a service worker controls the page. */
  controlled(): boolean;
  /** Loads the page again, as the browser would. */
  reload(): void;
}

/** The page behind the error views' Reload: the document's worker container and location. Internal. */
export const TM_RELOAD_HOST = new InjectionToken<TmReloadHost>('TM_RELOAD_HOST', {
  providedIn: 'root',
  factory: (): TmReloadHost => {
    const document = inject(DOCUMENT);
    return {
      controlled: () => (document.defaultView?.navigator.serviceWorker?.controller ?? null) !== null,
      reload: () => document.location.reload(),
    };
  },
});

/**
 * The error views' Reload and Reload anyway, which the About dialog's
 * Reload shares. Internal.
 */
@Injectable({ providedIn: 'root' })
export class TmErrorUpdates {
  readonly #injector = inject(Injector);
  readonly #load = inject(ɵTM_UPDATES_LOAD);
  readonly #host = inject(TM_RELOAD_HOST);

  /**
   * Gets the new version and reloads into it: at once when no worker is in
   * the way, and otherwise once the worker has downloaded it.
   *
   * @returns What the worker's check found; the page is reloading when it is
   *   `ready` or `disabled`.
   */
  async update(): Promise<ɵTmApiUpdateCheck> {
    if (!this.#host.controlled()) {
      this.#host.reload();
      return 'disabled';
    }
    let loaded;
    try {
      loaded = await this.#load();
    } catch {
      // The update service did not load, so the worker cannot be asked.
      return 'unreachable';
    }
    if (loaded === null) {
      this.#host.reload();
      return 'disabled';
    }
    return this.#injector.get(loaded.TmUpdates).update();
  }

  /** Reloads as the browser would: a degraded worker sends the load to the network. */
  reload(): void {
    this.#host.reload();
  }
}

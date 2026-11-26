// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { DestroyRef, DOCUMENT, inject, Injectable, InjectionToken, type Signal, signal, type Type } from '@angular/core';

// Installed mode (spec 0027 §7.3): an installed app runs in its own window
// without the browser's buttons, so the app bar draws its own Back and
// Forward from `md` up. Installed mode is a display-mode media query (or
// iOS's `navigator.standalone`), followed live; `minimal-ui` keeps the
// browser's buttons, so it does not count.
//
// Only this probe is in the first frame. The buttons, with the history
// tracking behind them, load in a chunk of their own once the app runs
// installed (tm-shell-history.ts), so a browser tab, the common case,
// carries none of them. Until the chunk lands, the bar reserves the buttons'
// boxes. The chunk imports nothing from here at run time: the bar hands it
// the Navigation API as an input, so this file stays in the first frame's
// own chunk rather than one the two would share.

/** The display modes in which the browser draws no history buttons. */
const INSTALLED = '(display-mode: standalone), (display-mode: fullscreen), (display-mode: window-controls-overlay)';

/** What installed mode and the history buttons read from the browser. Internal: a test replaces it. */
export interface TmHistoryHost {
  /** The installed-mode media query. */
  readonly installed: MediaQueryList | null;
  /** Whether iOS runs the app from the home screen (`navigator.standalone`). */
  readonly standalone: boolean;
  /** The Navigation API, where the browser has it. */
  readonly navigation: Navigation | null;
}

/** What installed mode and the history buttons read from the browser: the document's window. Internal. */
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

/** What the history buttons' chunk exports. */
export interface TmShellHistoryChunk {
  /** The Back and Forward buttons, which fill the box the app bar reserves for them. */
  readonly TmShellHistory: Type<unknown>;
}

/** Loads the history buttons' chunk. Internal: a test delays it or makes it fail. */
export const TM_SHELL_HISTORY_LOAD = new InjectionToken<() => Promise<TmShellHistoryChunk>>('TM_SHELL_HISTORY_LOAD', {
  providedIn: 'root',
  factory: () => () => import('./tm-shell-history'),
});

/**
 * Whether the app runs installed, in its own window without the browser's
 * history buttons. Provided by the tenant shell, for the tenant's life in
 * the tab.
 */
@Injectable()
export class TmInstalledMode {
  readonly #host = inject(TM_HISTORY_HOST);
  readonly #installed = signal(this.#host.standalone || (this.#host.installed?.matches ?? false));

  /** Whether the app runs in its own window, without the browser's buttons. */
  readonly installed: Signal<boolean> = this.#installed.asReadonly();

  /** The Navigation API, where the browser has it, which the history buttons read. */
  readonly navigation: Navigation | null = this.#host.navigation;

  /** Creates the probe, which follows the display mode until the shell ends. */
  constructor() {
    // iOS's home-screen mode cannot change while the app runs; a display mode can.
    const installed = this.#host.installed;
    if (installed !== null && !this.#host.standalone) {
      const change = (): void => this.#installed.set(installed.matches);
      installed.addEventListener('change', change);
      inject(DestroyRef).onDestroy(() => installed.removeEventListener('change', change));
    }
  }
}

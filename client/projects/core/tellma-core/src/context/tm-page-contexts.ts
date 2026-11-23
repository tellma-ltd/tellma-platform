// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { DestroyRef, inject, Injectable, type Signal } from '@angular/core';

import { TM_DEV_MODE } from '../app/tm-dev-mode';
import type { TmPageContext } from './tm-page-context';
import { TmPageRegistry, TmPageScope } from './tm-page-registry';

// The page-context registry's public face (spec 0026 §9.2). Injected inside
// a page scope, it registers into that scope; outside any, into none. Every
// instance reads the one root registry.

declare const ngDevMode: unknown;

/** A help key: lowercase kebab-case parts separated by dots, such as `gl.center.fields.code`. */
const HELP_KEY = /^[a-z][a-z0-9-]*(\.[a-z0-9][a-z0-9-]*)*$/;

/**
 * The page contexts of the app: the routed page's, and those of the open
 * context-bearing dialogs above it. Toolbars, menus, shortcuts, the support
 * dump and a future assistant read them.
 *
 * Inside a page scope, a second routed context replaces the first and keeps
 * its actions after its own, so a page that publishes richer context than
 * its host keeps the host's actions. Dialog contexts (`overlay: true`) never
 * replace each other and stack in registration order. A context ends with
 * its `DestroyRef` or its scope, whichever ends first.
 *
 * @example
 * ```ts
 * inject(TmPageContexts).register({
 *   kind: 'custom', routeTemplate: '/:t/credit-review', title: this.title, status: this.status,
 *   state: computed(() => ({ threshold: this.threshold(), overLimit: this.overLimit() })),
 *   errors: signal([]), actions: signal([]),
 * });
 * ```
 */
@Injectable({ providedIn: 'root' })
export class TmPageContexts {
  readonly #registry = inject(TmPageRegistry);
  readonly #scope = inject(TmPageScope, { optional: true });
  readonly #devMode = inject(TM_DEV_MODE);

  /** The routed page's context: the newest live context that is not a dialog's, its actions merged. */
  readonly current: Signal<TmPageContext | null> = this.#registry.current;

  /** The open context-bearing dialogs' contexts, bottom to top, their actions merged. */
  readonly overlays: Signal<readonly TmPageContext[]> = this.#registry.overlays;

  /** The topmost dialog's context, else the routed page's. */
  readonly top: Signal<TmPageContext | null> = this.#registry.top;

  /**
   * Registers a context in the page scope this instance was injected in.
   * Nothing is read until a reader asks, so registering costs nothing; a
   * host registers in its constructor.
   *
   * @param context - The context.
   * @param destroyRef - What ends the registration; by default, the caller's injection context's.
   * @throws Error in development, when the context's `helpKey` is malformed.
   */
  register(context: TmPageContext, destroyRef: DestroyRef = inject(DestroyRef)): void {
    if (
      typeof ngDevMode !== 'undefined' &&
      ngDevMode &&
      this.#devMode() &&
      context.helpKey !== undefined &&
      !HELP_KEY.test(context.helpKey)
    ) {
      throw new Error(`The help key "${context.helpKey}" must be lowercase kebab-case parts separated by dots, such as "gl.center".`);
    }
    this.#registry.ɵregister(context, this.#scope, destroyRef);
  }
}

// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { DestroyRef, inject, Injectable, signal, type Signal } from '@angular/core';

import type { TmJson } from '../json/tm-json';
import type { TmActionOrigin, TmActionResult, TmPageActionInput } from './tm-page-action';
import { TmPageRegistry, TmPageScope } from './tm-page-registry';

// The page actions' public face (spec 0026 §9.4, §9.5). Injected inside a
// page scope, it adds actions to that scope's context, even before the
// context registers; outside any scope, to the routed page's context.
// Invocations from every instance go through the one dispatcher.

/**
 * Registers page actions and invokes them. Toolbars, menus, shortcuts and a
 * future assistant are views of the same actions, and every one of them
 * invokes through {@link TmPageActions.invoke}, so the permission,
 * double-submit and confirmation rules hold in one place.
 *
 * A context's actions are its publisher's own, then its scope's, ordered by
 * group (toolbar first), then `order`, then registration. While a context is
 * on top, each of its actions' shortcuts is live.
 *
 * @example
 * ```ts
 * inject(TmPageActions).register({
 *   id: 'acme.credit-review.suggest-limits', labelKey: 'Acme_CreditReview_SuggestLimits', icon: 'wand',
 *   group: 'primary', shortcut: 'Mod+Shift+A',
 *   invoke: async () => { await this.suggestLimits(); return { outcome: 'done' }; },
 * });
 * ```
 */
@Injectable({ providedIn: 'root' })
export class TmPageActions {
  readonly #registry = inject(TmPageRegistry);
  readonly #scope = inject(TmPageScope, { optional: true });

  /** The ids being invoked, which views render busy. */
  readonly running: Signal<ReadonlySet<string>> = this.#registry.running;

  /**
   * Adds an action to the context of the page scope this instance was
   * injected in, or to the routed page's context outside any scope, until
   * `destroyRef` ends.
   *
   * @param action - The action; `group` defaults to `menu`, `enabled` to always, `destructive` to `false`, `disabledReason` to `null`.
   * @param destroyRef - What ends the registration; by default, the caller's injection context's.
   * @throws Error in development, when the id is malformed, a destructive action lacks `confirm`, or an action whose arguments require members has a shortcut.
   */
  register(action: TmPageActionInput, destroyRef: DestroyRef = inject(DestroyRef)): void {
    this.#registry.ɵadd(
      {
        ...action,
        group: action.group ?? 'menu',
        enabled: action.enabled ?? signal(true).asReadonly(),
        destructive: action.destructive ?? false,
        disabledReason: action.disabledReason ?? signal(null).asReadonly(),
        // Bound to the input, so a class's method keeps its `this`.
        invoke: (args, origin) => action.invoke(args, origin),
      },
      this.#scope,
      destroyRef,
    );
  }

  /**
   * Invokes an action of the top context, the lower dialogs' or the routed
   * page's, in that order of search: a disabled one is refused with its
   * reason, which a user's or a shortcut's invocation also announces; a
   * running one is refused as busy; a destructive one asks the user to
   * confirm, whoever invokes it; the rest run, busy meanwhile. A throw
   * becomes `failed`. Views call `invoke(action.id, {}, 'user')`, never the
   * action's own `invoke`.
   *
   * @param id - The action's id.
   * @param args - Its arguments, as its `params` describe them; `{}` from a view or a shortcut.
   * @param origin - Who invokes it.
   * @returns How the invocation ended; it never rejects in production.
   */
  invoke(id: string, args: TmJson, origin: TmActionOrigin): Promise<TmActionResult> {
    return this.#registry.invoke(id, args, origin);
  }
}

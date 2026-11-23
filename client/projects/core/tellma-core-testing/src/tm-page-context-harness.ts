// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { inject, Injectable, Injector } from '@angular/core';
import {
  type TmActionOrigin,
  type TmActionResult,
  type TmJson,
  type TmPageAction,
  TmPageActions,
  type TmPageContext,
  type TmPageContextSnapshot,
  TmPageContexts,
  tmSerializePageContext,
} from '@tellma/core';

/**
 * Reads the page-context registry and invokes page actions in a test, as
 * the support dump, the toolbars and a future assistant do: snapshots of the
 * routed page's and the dialogs' contexts, the top context's actions, and
 * invocations through the dispatcher.
 *
 * @example
 * ```ts
 * const harness = TestBed.inject(TmPageContextHarness);
 * expect(harness.current()?.kind).toBe('custom');
 * expect(await harness.invoke('acme.credit-review.suggest-limits')).toEqual({ outcome: 'done' });
 * ```
 */
@Injectable({ providedIn: 'root' })
export class TmPageContextHarness {
  readonly #injector = inject(Injector);
  readonly #contexts = inject(TmPageContexts);
  readonly #actions = inject(TmPageActions);

  /**
   * The routed page's context as a snapshot.
   *
   * @param includeData - Whether to carry page data; default `true`.
   * @returns The snapshot, or `null` when no routed context is live.
   */
  current(includeData = true): TmPageContextSnapshot | null {
    return this.#snapshot(this.#contexts.current(), includeData);
  }

  /**
   * The open dialogs' contexts as snapshots, bottom to top.
   *
   * @param includeData - Whether to carry page data; default `true`.
   * @returns The snapshots.
   */
  overlays(includeData = true): TmPageContextSnapshot[] {
    return this.#contexts.overlays().map((context) => this.#snapshot(context, includeData)!);
  }

  /**
   * The top context as a snapshot: the topmost dialog's, else the routed page's.
   *
   * @param includeData - Whether to carry page data; default `true`.
   * @returns The snapshot, or `null` when no context is live.
   */
  top(includeData = true): TmPageContextSnapshot | null {
    return this.#snapshot(this.#contexts.top(), includeData);
  }

  /**
   * The top context's actions, merged and in place order, as views read them.
   *
   * @returns The actions; none when no context is live.
   */
  actions(): readonly TmPageAction[] {
    return this.#contexts.top()?.actions() ?? [];
  }

  /**
   * The top context's action with an id.
   *
   * @param id - The action's id.
   * @returns The action, or `undefined`.
   */
  action(id: string): TmPageAction | undefined {
    return this.actions().find((action) => action.id === id);
  }

  /**
   * Invokes an action through the dispatcher, as a view, a shortcut or the
   * assistant would.
   *
   * @param id - The action's id.
   * @param args - Its arguments; default `{}`.
   * @param origin - Who invokes it; default `user`.
   * @returns How the invocation ended.
   */
  invoke(id: string, args: TmJson = {}, origin: TmActionOrigin = 'user'): Promise<TmActionResult> {
    return this.#actions.invoke(id, args, origin);
  }

  /**
   * The ids being invoked.
   *
   * @returns The ids.
   */
  running(): ReadonlySet<string> {
    return this.#actions.running();
  }

  /** A context's snapshot, or `null` for none. */
  #snapshot(context: TmPageContext | null, includeData: boolean): TmPageContextSnapshot | null {
    return context === null ? null : tmSerializePageContext(context, { includeData, injector: this.#injector });
  }
}

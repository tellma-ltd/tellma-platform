// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { computed, type DestroyRef, InjectionToken, type Signal, signal } from '@angular/core';

// What holds a layout's fields (spec 0031 §6.6): a card or a tab. A field
// reports whether it shows an error to the nearest one through DI, which
// reports to its own enclosing one, so a card holding a tab holding a field
// shows the field's error dot; and a field's focus() reveals every one
// around it (selects the tab, expands the card) before it focuses its
// control.

/** A card or a tab: what holds a field, and what a field reveals when it is focused. */
export class TmDetailsScope {
  /** The scope around this one, or `null` at the layout's top. */
  readonly #parent: TmDetailsScope | null;

  /** Shows this scope's own content: selects the tab, expands the card. */
  readonly #show: () => void;

  /** The error states held beneath, each a field's or an inner scope's. */
  readonly #held = signal<ReadonlySet<Signal<boolean>>>(new Set());

  /** Whether anything beneath shows an error. */
  readonly invalid: Signal<boolean> = computed(() => [...this.#held()].some((holds) => holds()));

  /**
   * A scope inside another.
   *
   * @param parent - The enclosing scope, or `null`.
   * @param show - Shows the scope's own content.
   * @param destroyRef - The scope's lifetime, which ends its report to the enclosing scope.
   */
  constructor(parent: TmDetailsScope | null, show: () => void, destroyRef: DestroyRef) {
    this.#parent = parent;
    this.#show = show;
    parent?.hold(this.invalid, destroyRef);
  }

  /**
   * Holds an error state beneath this scope, until `destroyRef` ends.
   *
   * @param shows - Whether the field or the inner scope shows an error.
   * @param destroyRef - What ends the hold.
   */
  hold(shows: Signal<boolean>, destroyRef: DestroyRef): void {
    this.#held.update((held) => new Set(held).add(shows));
    destroyRef.onDestroy(() =>
      this.#held.update((held) => {
        const next = new Set(held);
        next.delete(shows);
        return next;
      }),
    );
  }

  /** Shows this scope and every one around it, outermost last: the tab is selected and the card expanded. */
  reveal(): void {
    this.#show();
    this.#parent?.reveal();
  }
}

/** The nearest card or tab. Internal: the kit's cards and tabs provide it; fields and inner scopes inject it. */
export const TM_DETAILS_SCOPE = new InjectionToken<TmDetailsScope>('TM_DETAILS_SCOPE');

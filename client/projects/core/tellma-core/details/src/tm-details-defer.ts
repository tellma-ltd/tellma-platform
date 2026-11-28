// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { afterNextRender, DestroyRef, ElementRef, inject, signal, type Signal } from '@angular/core';

// The triggers of the child grid's and the child files' deferred chunks
// (spec 0031 §6.4): load once the host enters the viewport, prefetch once
// the browser is idle. They are the semantics of `@defer (on viewport;
// prefetch on idle)`, written as a signal the block reads with `when` and
// an idle callback that imports the block's module ahead of it: Angular
// keeps its viewport and idle triggers in @angular/core, which an app's
// first frame carries, so the built-in triggers would add their code to
// every page's first load once any block used them, where `when` and a
// plain import() cost nothing the app does not already ship.

/** When a deferred block of the details kit loads. */
export interface ɵTmDeferTriggers {
  /** Whether the host has entered the viewport: the block loads and renders. */
  readonly visible: Signal<boolean>;
}

/**
 * The triggers of a deferred block inside the calling component's host:
 * `visible` once the host intersects the viewport, and the block's module
 * imported ahead of it once the browser is idle after the host's first
 * render, so the block finds it loaded. A browser without an
 * IntersectionObserver or idle callbacks sets `visible` at once and
 * prefetches after a timeout. Internal to the details kit; call it in an
 * injection context.
 *
 * @param prefetch - Imports the block's module, as the block itself does.
 * @returns The triggers.
 */
export function ɵtmDeferTriggers(prefetch: () => Promise<unknown>): ɵTmDeferTriggers {
  const host = inject<ElementRef<HTMLElement>>(ElementRef).nativeElement;
  const destroyRef = inject(DestroyRef);
  const visible = signal(false);
  // A failed prefetch says nothing: the block's own load retries it and shows its error.
  const load = (): void => void prefetch().catch(() => undefined);
  afterNextRender(() => {
    // --- Visible: the first intersection, then the observer is done ---
    if (typeof IntersectionObserver === 'function') {
      const observer = new IntersectionObserver((entries) => {
        if (entries.some((entry) => entry.isIntersecting)) {
          visible.set(true);
          observer.disconnect();
        }
      });
      observer.observe(host);
      destroyRef.onDestroy(() => observer.disconnect());
    } else {
      visible.set(true);
    }

    // --- Idle: the prefetch on the browser's idle callback, else on the next task ---
    if (typeof requestIdleCallback === 'function') {
      const id = requestIdleCallback(load);
      destroyRef.onDestroy(() => cancelIdleCallback(id));
    } else {
      const id = setTimeout(load);
      destroyRef.onDestroy(() => clearTimeout(id));
    }
  });
  return { visible: visible.asReadonly() };
}

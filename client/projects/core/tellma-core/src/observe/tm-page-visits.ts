// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { afterNextRender, DestroyRef, DOCUMENT, effect, inject, Injectable, Injector, signal, untracked } from '@angular/core';
import { NavigationCancel, NavigationEnd, NavigationError, NavigationSkipped, NavigationStart, Router } from '@angular/router';

import type { TmPageContext } from '../context/tm-page-context';
import { TmPageContexts } from '../context/tm-page-contexts';
import { TmTelemetry } from './tm-telemetry';

// Page visits (spec 0026 §5.1). A visit is one routed page: it starts at the
// router's NavigationStart, or at the document's time origin for its first
// route, and ends at the next NavigationStart or at `pagehide`, emitting one
// sampled `tm.page.visit` timing. Its `ms` runs to the first status change of
// its page context away from `loading` (data on screen, or the error state),
// and its `chromeMs` to the first paint after its context is registered: the
// render after it, then one animation frame. A visit adopts the routed
// context registered after it starts; a navigation that keeps its page, as a
// reused route does, adopts the context it kept one render after it ends. A
// visit that adopted no context, such as the launch page's redirect, is not
// a page's: its start and entry pass to the next visit. Times are
// `performance.now()` readings, milliseconds since the time origin.

/** One visit while it runs. */
interface TmVisit {
  /** When it started. */
  readonly start: number;
  /** How it started: the document's first route, or a navigation. */
  readonly entry: 'load' | 'navigation';
  /** The routed context when it started, which it adopts only once its navigation has ended. */
  readonly kept: TmPageContext | null;
  /** The context it adopted, once it has. */
  own: TmPageContext | null;
  /** The chrome's paint, once stamped. */
  chromeMs?: number;
  /** The data's arrival, once stamped. */
  ms?: number;
  /** The status the data arrived in. */
  outcome?: 'ready' | 'error';
}

/** Times the routed page visits and reports each one. Internal: the app's startup starts it. */
@Injectable({ providedIn: 'root' })
export class TmPageVisits {
  readonly #router = inject(Router);
  readonly #contexts = inject(TmPageContexts);
  readonly #telemetry = inject(TmTelemetry);
  readonly #injector = inject(Injector);
  readonly #window = inject(DOCUMENT).defaultView;

  /** The running visit, or `null` after `pagehide` until the next navigation. */
  readonly #visit = signal<TmVisit | null>({ start: 0, entry: 'load', kept: null, own: null });

  /** Whether the running visit's navigation has ended and rendered. */
  readonly #settled = signal(false);

  /** Whether the first navigation has started; it belongs to the first visit. */
  #navigated = false;

  /**
   * Starts following the visits. Internal: the app's startup calls it before
   * the first navigation.
   */
  ɵstart(): void {
    // --- Each visit's context, chrome and data ---
    effect(
      () => {
        const visit = this.#visit();
        const own = this.#contexts.current();
        // A context registered after the visit started is its own; the one it kept, only once its navigation has ended.
        if (visit === null || own === null || (own === visit.kept && !this.#settled())) {
          return;
        }
        const status = own.status();
        untracked(() => {
          if (visit.own === null) {
            visit.own = own;
            this.#paint(visit);
          }
          if (visit.ms === undefined && status !== 'loading') {
            visit.ms = performance.now() - visit.start;
            visit.outcome = status;
          }
        });
      },
      { injector: this.#injector },
    );

    // --- Boundaries: a navigation starts the next visit; its end lets a kept page count ---
    const events = this.#router.events.subscribe((event) => {
      if (event instanceof NavigationStart) {
        // The document's first navigation belongs to the first visit.
        if (this.#navigated) {
          const carried = this.#end();
          this.#visit.set({
            start: carried?.start ?? performance.now(),
            entry: carried?.entry ?? 'navigation',
            kept: untracked(this.#contexts.current),
            own: null,
          });
        }
        this.#navigated = true;
        this.#settled.set(false);
      } else if (
        event instanceof NavigationEnd ||
        event instanceof NavigationCancel ||
        event instanceof NavigationSkipped ||
        event instanceof NavigationError
      ) {
        afterNextRender(() => this.#settled.set(true), { injector: this.#injector });
      }
    });

    // --- The page hidden for good ends the visit ---
    const hide = (): void => void this.#end();
    this.#window?.addEventListener('pagehide', hide);
    this.#injector.get(DestroyRef).onDestroy(() => {
      events.unsubscribe();
      this.#window?.removeEventListener('pagehide', hide);
    });
  }

  /**
   * Stamps the chrome's paint: the render after the context's registration,
   * then one animation frame.
   *
   * @param visit - The visit whose context was registered.
   */
  #paint(visit: TmVisit): void {
    afterNextRender(
      () => {
        const frame = this.#window?.requestAnimationFrame?.bind(this.#window) ?? ((step: () => void) => setTimeout(step));
        frame(() => (visit.chromeMs ??= performance.now() - visit.start));
      },
      { injector: this.#injector },
    );
  }

  /**
   * Ends the running visit and reports it, unless it adopted no context.
   *
   * @returns The visit when it adopted no context, whose start and entry pass
   *   to the next; `null` once it was reported, or when none was running.
   */
  #end(): TmVisit | null {
    const visit = untracked(this.#visit);
    this.#visit.set(null);
    if (visit === null || visit.own === null) {
      return visit;
    }
    const now = performance.now();
    // A visit left before its data arrived reports how long it was waited for.
    this.#telemetry.timing('tm.page.visit', Math.round(visit.ms ?? now - visit.start), {
      ...(visit.chromeMs === undefined ? {} : { chromeMs: Math.round(visit.chromeMs) }),
      visitS: Math.round((now - visit.start) / 100) / 10,
      entry: visit.entry,
      outcome: visit.outcome ?? 'left',
    });
    return null;
  }
}

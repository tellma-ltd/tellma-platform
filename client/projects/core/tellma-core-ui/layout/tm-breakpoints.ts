// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import {
  computed,
  DestroyRef,
  DOCUMENT,
  inject,
  Injectable,
  signal,
  type Signal,
} from '@angular/core';

import breakpoints from './breakpoints.json';

/**
 * The platform's closed set of width classes, narrowest first. `xs` starts
 * at 0; every other class starts at its threshold in {@link TM_BREAKPOINTS}.
 */
export type TmBreakpoint = 'xs' | 'sm' | 'md' | 'lg' | 'xl' | 'xxl';

/**
 * The lower bound, in CSS pixels, of every width class above `xs`.
 *
 * The values live once, in the package's `breakpoints.json` (exported as
 * `@tellma/core-ui/breakpoints.json`): this constant imports it, and the
 * stylelint rule that polices media and container queries reads the same
 * file, so script and stylesheets can never disagree on a threshold. There
 * are deliberately no breakpoint tokens — a custom property cannot appear in
 * a media or container query, where it fails silently.
 */
export const TM_BREAKPOINTS: Readonly<Record<Exclude<TmBreakpoint, 'xs'>, number>> = Object.freeze({
  sm: breakpoints.sm,
  md: breakpoints.md,
  lg: breakpoints.lg,
  xl: breakpoints.xl,
  xxl: breakpoints.xxl,
});

/** The classes above `xs`, narrowest first — one media query list each. */
const THRESHOLDS = ['sm', 'md', 'lg', 'xl', 'xxl'] as const;

/** Every class in ascending order, for rank comparisons. */
const ORDER: readonly TmBreakpoint[] = ['xs', ...THRESHOLDS];

/**
 * The viewport's width class as signals, for PAGE structure: a grid or
 * tiles, a drawer or a popover, a full-screen launcher.
 *
 * Components never use it to adapt themselves — they set
 * `container-type: inline-size` on an inner wrapper and query
 * `@container (width >= 576px)`, so a component behaves the same in a
 * narrow card on a wide screen.
 *
 * Five `matchMedia('(width >= Npx)')` lists with `change` listeners feed one
 * signal: no resize listener and no layout read runs between changes, so a
 * reader pays nothing until the viewport actually crosses a threshold.
 *
 * @example
 * ```ts
 * private readonly wide = inject(TmBreakpoints).atLeast('lg');
 * protected open(): void {
 *   this.wide() ? this.popover().open(this.bell()) : this.modal.open(BellPanel, { size: 'sm', title });
 * }
 * ```
 */
@Injectable({ providedIn: 'root' })
export class TmBreakpoints {
  /** The widest class the viewport currently reaches (`xs` below the `sm` threshold). */
  readonly current: Signal<TmBreakpoint>;

  /** One signal per class, created on first ask and shared by every later caller. */
  private readonly atLeastSignals = new Map<TmBreakpoint, Signal<boolean>>();

  constructor() {
    const view = inject(DOCUMENT).defaultView;
    // A document without a window (or a window without matchMedia) has no
    // viewport to measure; it reads as the narrowest class, never throws.
    const lists = THRESHOLDS.map((name) =>
      typeof view?.matchMedia === 'function'
        ? view.matchMedia(`(width >= ${TM_BREAKPOINTS[name]}px)`)
        : null,
    );
    // The lists are monotonic (a viewport that reaches lg reaches md), so the
    // last match is the widest class reached.
    const read = (): TmBreakpoint => {
      let widest: TmBreakpoint = 'xs';
      lists.forEach((list, index) => {
        if (list?.matches) {
          widest = THRESHOLDS[index];
        }
      });
      return widest;
    };
    const state = signal<TmBreakpoint>(read());
    const onChange = (): void => state.set(read());
    for (const list of lists) {
      list?.addEventListener('change', onChange);
    }
    inject(DestroyRef).onDestroy(() => {
      for (const list of lists) {
        list?.removeEventListener('change', onChange);
      }
    });
    this.current = state.asReadonly();
  }

  /**
   * Whether the viewport reaches `breakpoint` — memoized, so every caller
   * asking for the same class shares one signal. `atLeast('xs')` is always
   * true.
   */
  atLeast(breakpoint: TmBreakpoint): Signal<boolean> {
    let reached = this.atLeastSignals.get(breakpoint);
    if (reached === undefined) {
      const rank = ORDER.indexOf(breakpoint);
      reached = computed(() => ORDER.indexOf(this.current()) >= rank);
      this.atLeastSignals.set(breakpoint, reached);
    }
    return reached;
  }
}

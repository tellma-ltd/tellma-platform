// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { type Provider, signal, type Signal, type WritableSignal } from '@angular/core';
import type { TmJson, TmReportArgs, TmWidgetSize } from '@tellma/core';
// A widget body's unit test runs without a grid, so the fake gives the body its context through the entry point's own token.
import { type TmWidgetContext, type TmWidgetRun, type TmWidgetTick, ɵTM_WIDGET_CONTEXT } from '@tellma/core/dashboards';

// What a dashboard gives one widget body, for the body's unit test: the
// context injectTmWidget() returns, with every input writable, a refresh
// the test asks for, and the runs the body reports, recorded in order.

/** How a fake dashboard starts. */
export interface TmFakeDashboardOptions {
  /** The widget type's key, such as `acme.cash-position`. */
  readonly type: string;
  /** The widget's key; `fixture` by default. */
  readonly key?: string;
  /** The widget's settings; none by default. */
  readonly settings?: TmJson | null;
  /** The dashboard's supplied arguments; none by default. */
  readonly parameters?: TmReportArgs;
  /** The tile's size, in cells; 6 by 6 by default. */
  readonly size?: TmWidgetSize;
}

/** A dashboard a widget body's test drives. */
export interface TmFakeDashboard {
  /** The widget's settings, which the test sets. */
  readonly settings: WritableSignal<TmJson | null>;
  /** The dashboard's supplied arguments, which the test sets. */
  readonly parameters: WritableSignal<TmReportArgs>;
  /** The dashboard's mode, which the test sets. */
  readonly mode: WritableSignal<'view' | 'edit'>;
  /** The tile's size, which the test sets. */
  readonly size: WritableSignal<TmWidgetSize>;
  /** The body's tick, as the dashboard last gave it. */
  readonly tick: Signal<TmWidgetTick>;
  /** The runs the body reported, oldest first. */
  readonly reports: Signal<readonly TmWidgetRun[]>;
  /** The providers that give a component under test this context through `injectTmWidget()`. */
  readonly providers: Provider[];
  /**
   * Asks the body to refresh, as the scheduler, a forced refresh or the
   * tile's Refresh does: a new generation with the flags given.
   *
   * @param flags - Whether the body clears what it shows first, and whether the run is a background one; neither by default.
   */
  refresh(flags?: { readonly clear?: boolean; readonly background?: boolean }): void;
}

/**
 * A dashboard for one widget body's unit test: the body reads the context
 * as it would in a tile, and the test sets its inputs, ticks it and reads
 * what it reported.
 *
 * @param options - How the dashboard starts.
 * @returns The dashboard.
 *
 * @example
 * ```ts
 * const dashboard = tmFakeDashboard({ type: 'acme.cash-position', settings: { account: 'cash' } });
 * TestBed.configureTestingModule({ providers: [provideTellmaTesting(), ...dashboard.providers] });
 * const fixture = TestBed.createComponent(TmCashPositionWidget);
 * dashboard.refresh({ clear: true });
 * expect(dashboard.reports().at(-1)?.status).toBe('ready');
 * ```
 */
export function tmFakeDashboard(options: TmFakeDashboardOptions): TmFakeDashboard {
  const settings = signal<TmJson | null>(options.settings ?? null);
  const parameters = signal<TmReportArgs>(options.parameters ?? {});
  const mode = signal<'view' | 'edit'>('view');
  const size = signal<TmWidgetSize>(options.size ?? { w: 6, h: 6 });
  const tick = signal<TmWidgetTick>({ generation: 0, clear: false, background: false });
  const reports = signal<readonly TmWidgetRun[]>([]);
  const context: TmWidgetContext = {
    key: options.key ?? 'fixture',
    type: options.type,
    settings: settings.asReadonly(),
    parameters: parameters.asReadonly(),
    mode: mode.asReadonly(),
    size: size.asReadonly(),
    tick: tick.asReadonly(),
    report: (state) => reports.update((runs) => [...runs, state]),
  };
  return {
    settings,
    parameters,
    mode,
    size,
    tick: tick.asReadonly(),
    reports: reports.asReadonly(),
    providers: [{ provide: ɵTM_WIDGET_CONTEXT, useValue: context }],
    refresh: (flags = {}) =>
      tick.update((previous) => ({ generation: previous.generation + 1, clear: flags.clear ?? false, background: flags.background ?? false })),
  };
}

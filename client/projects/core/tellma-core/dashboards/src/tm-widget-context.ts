// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { inject, InjectionToken, type Signal } from '@angular/core';
import type { TmApiError, TmJson, TmReportArgs, TmWidgetSize } from '@tellma/core';

// What a widget type's body reads from its tile (spec 0035 §9.3): its own
// key, type and settings, the dashboard's supplied arguments, the mode, its
// size in cells, and the tick that asks it to refresh. The body reports its
// run back through `report`, which feeds the tile's status line and the
// tile's state in the grid's `tiles`. A body that never reports is live:
// only a forced refresh ticks it.

/** A request to refresh, which a body keys its read on. */
export interface TmWidgetTick {
  /** Rises with each request; a body keys its `resource()` on it. */
  readonly generation: number;
  /** Whether the body drops what it shows before it reads again: a user's refresh always clears. */
  readonly clear: boolean;
  /** Whether the read is a scheduled one, which passes `background` to `TmApi.read`. */
  readonly background: boolean;
}

/** A body's run, as it reports it to its tile. */
export interface TmWidgetRun {
  /** Where the run is. */
  readonly status: 'loading' | 'ready' | 'error';
  /** When the server computed the numbers shown, as ISO 8601; `null` or absent before the first result. */
  readonly executedAt?: string | null;
  /** The run's failure, when it failed. */
  readonly error?: TmApiError | null;
  /** Whether a summary's totals row was left out, which the status line notes. */
  readonly totalsOmitted?: boolean;
}

/** What a widget type's body reads from its tile, and how it reports its run. */
export interface TmWidgetContext {
  /** The widget's key on its grid, such as `w42` or `home.to-me`. */
  readonly key: string;
  /** The widget type's key, such as `core.recents`. */
  readonly type: string;
  /** The type's settings, parsed; `null` when the widget has none. */
  readonly settings: Signal<TmJson | null>;
  /** The dashboard's supplied arguments. */
  readonly parameters: Signal<TmReportArgs>;
  /** Whether the dashboard is viewed or edited. */
  readonly mode: Signal<'view' | 'edit'>;
  /** The tile's size, in cells. */
  readonly size: Signal<TmWidgetSize>;
  /** A new value asks the body to refresh. */
  readonly tick: Signal<TmWidgetTick>;
  /**
   * Reports the body's run: its state, when its numbers were computed, its
   * failure and whether its totals were left out.
   *
   * @param state - The run's state.
   */
  report(state: TmWidgetRun): void;
}

/** The context a tile gives its body. Internal: bodies read it through {@link injectTmWidget}. */
export const TM_WIDGET_CONTEXT = new InjectionToken<TmWidgetContext>('TM_WIDGET_CONTEXT');

/**
 * The context of the tile whose body calls it: the widget's key, type and
 * settings, the dashboard's arguments, the mode, the size and the refresh
 * tick, and `report` for the body's run. Call it in a widget type's body,
 * in an injection context.
 *
 * @returns The tile's context.
 * @throws Error when called outside a dashboard tile's body.
 *
 * @example
 * ```ts
 * export default class CashPosition {
 *   readonly #widget = injectTmWidget();
 *   readonly #api = inject(TmApi);
 *   protected readonly data = resource({
 *     params: () => ({ tick: this.#widget.tick(), settings: this.#widget.settings() }),
 *     loader: ({ params, abortSignal }) =>
 *       this.#api.read('acme/cash-position', params.settings ?? {}, { signal: abortSignal, background: params.tick.background }),
 *   });
 * }
 * ```
 */
export function injectTmWidget(): TmWidgetContext {
  return inject(TM_WIDGET_CONTEXT);
}

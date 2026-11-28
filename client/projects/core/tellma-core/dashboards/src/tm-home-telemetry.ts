// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { inject, Injectable, untracked } from '@angular/core';
import { TmTelemetry, ɵTmLaunchTimer } from '@tellma/core';
import { TmAttention } from '@tellma/core/attention';

// The home page's telemetry (spec 0035 §16): `tm.home.rendered`, timed once
// per visit from the route's start to the greeting and the tiles painted,
// and the bucket of a row's place in its list, which `tm.home.item-opened`
// reports so that no property carries a raw position.

/** A row's place in a home list, as `tm.home.item-opened` reports it. */
export type TmHomePosition = '1' | '2-3' | '4-10';

/**
 * The bucket of a row's place in a home list.
 *
 * @param position - The row's place, counting from 1.
 * @returns Its bucket.
 */
export function tmHomePosition(position: number): TmHomePosition {
  return position <= 1 ? '1' : position <= 3 ? '2-3' : '4-10';
}

/**
 * Times one home visit: `tm.home.rendered`, from the route's start to the
 * first frame in which the home's page has painted its greeting and tiles.
 * Internal: the home's host provides one per visit, and the page that
 * renders calls it once it has painted.
 */
@Injectable()
export class TmHomeRendered {
  readonly #telemetry = inject(TmTelemetry);
  readonly #timer = inject(ɵTmLaunchTimer);
  readonly #attention = inject(TmAttention);

  /** Whether the visit was timed. */
  #done = false;

  /**
   * Reports the visit's timing, once: whether its tenant entry was warm,
   * whether a hatch rendered, and whether the assignment lists had their
   * document or showed their skeleton rows.
   *
   * @param hatch - Whether a feature's hatch rendered in the platform page's place.
   */
  emit(hatch: boolean): void {
    if (this.#done) {
      return;
    }
    this.#done = true;
    const { at, cold } = this.#timer.ɵpage;
    this.#telemetry.timing('tm.home.rendered', Math.round(performance.now() - at), {
      warm: !cold,
      hatch,
      lists: untracked(this.#attention.assignments) === undefined ? 'skeleton' : 'data',
    });
  }
}

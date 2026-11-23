// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { inject, Injectable, signal, type Signal } from '@angular/core';
import { filter, Subject, type Observable } from 'rxjs';

import { TM_NOW } from '../../app/tm-now';
import type { TmClientEvent, TmHubStatus } from './tm-hub-types';

// The leader tab, as the rest of the app reads it (spec 0025 §7.1).
// One tab per browser and tenant holds the tenant's hub lock and with it the
// realtime connection; the election that decides it, the leader's work and
// the followers' checks on the leader live in @tellma/core/leader, which the
// startup loads after the first render and which writes the signals here.
// So this class stays small enough for the first frame, and a consumer reads
// it there before the election has started.

/**
 * The routed tenant's leader tab, as every tab sees it: whether this tab
 * leads, the leader's realtime connection, and the tenant's hub events.
 *
 * @example
 * ```ts
 * const leader = inject(TmLeader);
 * const live = computed(() => leader.hubStatus().state === 'connected');
 * leader.events('job.changed').subscribe(() => reload());
 * ```
 */
@Injectable({ providedIn: 'root' })
export class TmLeader {
  readonly #events = new Subject<TmClientEvent>();

  /** Whether this tab leads the routed tenant. Internal: the election writes it. */
  readonly ɵisLeader = signal(false);

  /** The leader's connection, mirrored in every tab. Internal: the election writes it. */
  readonly ɵhubStatus = signal<TmHubStatus>({ state: 'connecting', since: inject(TM_NOW)(), leaderVisible: true });

  /** Whether this tab leads the routed tenant. */
  readonly isLeader: Signal<boolean> = this.ɵisLeader.asReadonly();

  /** The leader's realtime connection: `connecting`, since now, visible, until the first report. */
  readonly hubStatus: Signal<TmHubStatus> = this.ɵhubStatus.asReadonly();

  /**
   * The routed tenant's hub events of one name, delivered in every tab whose
   * build knows the name.
   *
   * @param name - The event's name, such as `job.changed`.
   * @returns The events, as they arrive.
   */
  events(name: string): Observable<TmClientEvent> {
    return this.#events.pipe(filter((event) => event.name === name));
  }

  /**
   * Delivers an event to this tab's subscribers. Internal: the election
   * calls it.
   *
   * @param event - The event.
   */
  ɵdeliver(event: TmClientEvent): void {
    this.#events.next(event);
  }
}

// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { computed, inject, Injectable, InjectionToken, Injector, type Signal, type Type } from '@angular/core';

import { TmTelemetry } from '../observe/tm-telemetry';
import { TmSession } from '../session/tm-session';
import { TmClientStore } from '../state/store/tm-client-store';

// The device time zone (spec 0024 §7.8): the per-user, per-device option
// that makes later zone changes automatic, and the door into the check that
// notices the device's zone moving. Both live in one document of the store's
// device partition, which outlives every session, keyed by the user's
// subject. The primary holds only the option's reading and the doors: the
// document's writes are a chunk the option's first change loads; the check
// itself, with its triggers, its automatic update and its toast, is a chunk
// the app loads at idle after the first render; and its dialog is a chunk of
// its own that loads only when a change prompts.

/** One user's entry in the device's zone document. */
export interface TmZoneUser {
  /** Whether the user's time zone follows this device's automatically ("change it automatically"). */
  readonly auto: boolean;
  /** Each tenant's id, mapped to the device zone last handled in that tenant. */
  readonly seen: Readonly<Record<string, string>>;
  /** When the entry was last written, in epoch milliseconds: the cap drops the oldest. */
  readonly at: number;
}

/** The device's zone document: what the device remembers for each user who signed in on it. */
export interface TmZoneDoc {
  /** The users' entries, keyed by subject identifier; at most 20, the oldest dropped. */
  readonly users: Readonly<Record<string, TmZoneUser>>;
}

/** The device-zone check's chunk, as far as the primary reads it. Internal. */
export interface TmZoneCheckModule {
  /** The check: its triggers once started, and one check on demand. */
  readonly TmZoneCheck: Type<{ ɵstart(): void; check(): Promise<void> }>;
}

/**
 * Loads the device-zone check's chunk, or nothing, as in a test app, whose
 * device zone never moves. Internal: the seam the state testing providers
 * replace.
 */
export const TM_ZONE_CHECK_LOAD = new InjectionToken<() => Promise<TmZoneCheckModule | null>>('TM_ZONE_CHECK_LOAD', {
  providedIn: 'root',
  factory: () => () => import('./tm-zone-check'),
});

/**
 * The device's time zone, as the user's display zone follows it: the
 * per-device option that makes a change automatic, and the check that
 * notices the device's zone moving and offers, once per change, to switch
 * the user's time zone. The app starts the check after its first render;
 * it runs again on each tenant entry and whenever the tab comes into view.
 *
 * @example
 * ```ts
 * // My settings' "Change my time zone automatically" checkbox.
 * const zone = inject(TmDeviceZone);
 * const checked = zone.auto;
 * await zone.setAuto(true, 'settings');
 * ```
 */
@Injectable({ providedIn: 'root' })
export class TmDeviceZone {
  readonly #injector = inject(Injector);
  readonly #session = inject(TmSession);

  /** The device's zone document. */
  readonly #doc = inject(TmClientStore).doc<TmZoneDoc>({ kind: 'device' }, 'zone');

  /** The check's chunk and service, once loaded; `null` when the app runs none. */
  #check: Promise<{ ɵstart(): void; check(): Promise<void> } | null> | undefined;

  /** Whether the signed-in user's time zone follows this device's automatically; false while no one is signed in. */
  readonly auto: Signal<boolean> = computed(() => {
    const sub = this.#session.profile()?.sub;
    // Read defensively: a malformed document reads as the option off.
    const users = this.#doc()?.data?.users as Readonly<Record<string, Partial<TmZoneUser> | null | undefined>> | null | undefined;
    return sub !== undefined && users?.[sub]?.auto === true;
  });

  /**
   * Turns the option on or off for the signed-in user on this device. It
   * applies at once; nothing is sent to the server. Does nothing while no
   * one is signed in.
   *
   * @param on - Whether later changes of the device's zone apply by themselves.
   * @param source - Where the user chose: the zone prompt's checkbox or My settings.
   * @returns A promise that resolves once the device's document holds the
   *   choice; it rejects when the session no longer allows the write
   *   (`TmStoreRefusedError`) or the chunk that writes it does not load.
   */
  async setAuto(on: boolean, source: 'prompt' | 'settings'): Promise<void> {
    const { ɵtmSetZoneAuto } = await import('./tm-zone-doc');
    await ɵtmSetZoneAuto(this.#injector, on, source);
  }

  /**
   * Checks the device's zone now: in a visible tab of a tenant whose launch
   * documents are present, a change of the device's zone since the tenant
   * last saw it updates the user's time zone (with the option on) or asks
   * the user once. The app already checks on its own; a caller checks after
   * something it knows moved the zone.
   *
   * @returns A promise that resolves once the check is done, after the
   *   user's answer when it asks; it never rejects, and failures go to
   *   telemetry.
   */
  check(): Promise<void> {
    return this.#load().then((check) => check?.check());
  }

  /**
   * Loads the check and starts its triggers: a check now, on each tenant
   * entry, and whenever the tab comes into view. Internal: the startup
   * calls it at idle after the first render.
   */
  ɵstart(): void {
    void this.#load().then((check) => check?.ɵstart());
  }

  /** Loads the check's chunk and creates its service once; a failed load is reported and tried again on the next call. */
  #load(): Promise<{ ɵstart(): void; check(): Promise<void> } | null> {
    this.#check ??= this.#injector
      .get(TM_ZONE_CHECK_LOAD)()
      .then((module) => (module === null ? null : this.#injector.get(module.TmZoneCheck)))
      .catch((error: unknown) => {
        this.#check = undefined;
        this.#injector.get(TmTelemetry).error(error, { where: 'tm.l10n.zone-check' });
        return null;
      });
    return this.#check;
  }
}

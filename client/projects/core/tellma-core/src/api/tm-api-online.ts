// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { computed, DestroyRef, inject, Injectable, signal, type Signal } from '@angular/core';

import { TM_API_HOST } from './tm-api-host';

// Whether the server is reachable (spec 0024 §10.5). It is false while the
// browser says it is offline, or after a request failed at the network level.
// A visible tab then probes `GET /health/live` every 5 s, backing off to
// 30 s; any HTTP answer, even an error status, or the next request that gets
// one, sets it true again. The offline banner reads it.

/** The first wait between probes, in milliseconds. */
const FIRST_PROBE_MS = 5_000;

/** The longest wait between probes, in milliseconds. */
const LAST_PROBE_MS = 30_000;

/** How long one probe may hang before it counts as a failure, in milliseconds. */
const PROBE_TIMEOUT_MS = 10_000;

/** The route every instance answers without a session. */
const PROBE_URL = '/health/live';

/** The HTTP layer's view of connectivity, and the probe that restores it. */
@Injectable({ providedIn: 'root' })
export class TmApiOnline {
  readonly #host = inject(TM_API_HOST);
  readonly #browserOnline = signal(this.#host.onLine());
  readonly #failed = signal(false);
  #timer: ReturnType<typeof setTimeout> | null = null;
  #probing = false;
  #delay = FIRST_PROBE_MS;

  /** Whether the server is reachable, as far as this tab knows. */
  readonly online: Signal<boolean> = computed(() => this.#browserOnline() && !this.#failed());

  /** Follows the browser's connectivity events and the page's visibility. */
  constructor() {
    const stops = [
      this.#host.listen('window', 'online', () => {
        this.#browserOnline.set(true);
        // Back on the network: try at once rather than after the backoff.
        this.#delay = FIRST_PROBE_MS;
        this.#cancel();
        if (this.#failed()) {
          void this.#probe();
        }
      }),
      this.#host.listen('window', 'offline', () => {
        this.#browserOnline.set(false);
        this.#cancel();
      }),
      this.#host.listen('document', 'visibilitychange', () => {
        if (this.#host.visible()) {
          this.#schedule();
        } else {
          this.#cancel();
        }
      }),
    ];
    inject(DestroyRef).onDestroy(() => {
      this.#cancel();
      stops.forEach((stop) => stop());
    });
  }

  /** Records a network-level failure: the server is unreachable until an answer arrives. */
  failed(): void {
    this.#failed.set(true);
    this.#browserOnline.set(this.#host.onLine());
    this.#schedule();
  }

  /** Records an HTTP answer: the server is reachable. */
  answered(): void {
    this.#failed.set(false);
    this.#delay = FIRST_PROBE_MS;
    this.#cancel();
  }

  /** Arms the next probe, unless one is armed or running, or no probe is due. */
  #schedule(): void {
    if (this.#timer === null && !this.#probing && this.#failed() && this.#browserOnline() && this.#host.visible()) {
      this.#timer = setTimeout(() => {
        this.#timer = null;
        void this.#probe();
      }, this.#delay);
    }
  }

  /** Disarms the next probe. */
  #cancel(): void {
    if (this.#timer !== null) {
      clearTimeout(this.#timer);
      this.#timer = null;
    }
  }

  /** Sends one probe: an answer ends the outage; a failure backs off and arms the next. */
  async #probe(): Promise<void> {
    if (this.#probing) {
      return;
    }
    this.#probing = true;
    const controller = new AbortController();
    const timeout = setTimeout(() => controller.abort(), PROBE_TIMEOUT_MS);
    let answered = false;
    try {
      await this.#host.fetch(PROBE_URL, { credentials: 'same-origin', cache: 'no-store', signal: controller.signal });
      answered = true;
    } catch {
      this.#delay = Math.min(this.#delay * 2, LAST_PROBE_MS);
    } finally {
      clearTimeout(timeout);
      this.#probing = false;
    }
    if (answered) {
      this.answered();
    } else {
      this.#schedule();
    }
  }
}

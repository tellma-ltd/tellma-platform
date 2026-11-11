// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { inject, Injectable, signal } from '@angular/core';

import { TM_NOW } from '../app/tm-now';
import { TM_BOOT_RESULT } from '../boot/tm-boot-result';
import { TmTelemetry } from '../observe/tm-telemetry';
import { TM_PROFILE_COOKIE, tmParseProfileCookie } from './tm-profile';
import { TM_SESSION_HOST } from './tm-session-host';

// What the session does beyond the tab's own state when it starts, expires
// or finds another user (spec 0024 §8.1, §8.6, §8.7). The cross-tab paths
// need the tab bus, the client store, the drafts and the session-ended
// dialog (spec 0025), which build on the session; until they arrive, this
// tab-local version runs: a start tells no other tab, an expiry deletes the
// stale profile cookie and reloads, so that the boot takes the tab to the
// sign-in and the user's data leaves the screen, and a changed user reloads
// the app at the root, whose boot sweeps the previous user's data. The full
// paths replace these bodies, not their call sites.
//
// The paths also hold the session's writable state, which `TmSession` shows
// read-only: one injectable instead of two keeps the primary entry point
// small.

/** Where the tab's session stands. */
export type TmSessionState = 'active' | 'expired' | 'signing-out';

/**
 * How long after a sign-in an expiry counts as the sign-in failing: the
 * window of the boot script's own sign-in loop guard.
 */
const SIGN_IN_LOOP_WINDOW_MS = 60_000;

/** The assignment that deletes the profile cookie, with the attributes the server sets it with. */
const PROFILE_COOKIE_DELETE = `${TM_PROFILE_COOKIE}=; Max-Age=0; Path=/; Secure`;

/** The session's paths beyond this tab's own state, and the state they write. */
@Injectable({ providedIn: 'root' })
export class TmSessionPaths {
  readonly #telemetry = inject(TmTelemetry);
  readonly #host = inject(TM_SESSION_HOST);
  readonly #boot = inject(TM_BOOT_RESULT);
  readonly #now = inject(TM_NOW);

  /** The session's state: active until an expiry or a sign-out. */
  readonly state = signal<TmSessionState>('active');

  /**
   * Announces that this tab's session started, after a sign-in, so that the
   * user's other tabs reload. No tab bus exists yet, so nothing is announced.
   *
   * @param sub - The signed-in user's subject identifier.
   */
  started(sub: string): void {
    void sub;
  }

  /**
   * Runs the expiry path, once: the session is marked expired, the expiry is
   * reported, a stale profile cookie is deleted and the page reloads, which
   * leads to the sign-in. When this page itself came back from a sign-in
   * less than a minute ago, the sign-in is failing, so the page shows that
   * instead of reloading into another round, and its Try again button starts
   * the next one. A `401` for a request sent under a session the cookie no
   * longer names belongs to a session already replaced, and runs nothing.
   *
   * @param source - What found the expiry: the cookie check, or a `401` from the server.
   * @param sh - For a `401`, the session handle the request went out under, when known.
   */
  expired(source: 'cookie' | '401', sh?: string): void {
    if (this.state() !== 'active') {
      return;
    }
    // A newer session's cookie must survive a late answer to an older one.
    const current = tmParseProfileCookie(this.#host.cookies());
    if (sh !== undefined && current !== null && current.sh !== sh) {
      return;
    }
    this.state.set('expired');
    this.#telemetry.event('tm.session.expired', { source });
    this.#telemetry.flush();
    const restart = (): void => {
      // After a 401 the cookie may still be present; deleting it keeps the
      // reloaded boot from running the app again into the same 401.
      this.#host.writeCookie(PROFILE_COOKIE_DELETE);
      this.#host.location.reload();
    };
    // The boot's loop guard (spec 0024 §4.2 step 3b) only sees a sign-in that
    // comes back without a cookie; this one catches a sign-in that comes back
    // with a cookie the server then refuses.
    const boot = this.#boot;
    const since = boot?.mark ? this.#now() - boot.mark.at : -1;
    if (boot !== null && since >= 0 && since < SIGN_IN_LOOP_WINDOW_MS) {
      boot.showPage('sign-in-failed', restart);
    } else {
      restart();
    }
  }

  /**
   * Runs the changed-user path: another user's session replaced this tab's,
   * so the app restarts at the root.
   */
  changedUser(): void {
    this.#telemetry.event('tm.session.changed-user');
    this.#telemetry.flush();
    this.#host.location.replace('/');
  }
}

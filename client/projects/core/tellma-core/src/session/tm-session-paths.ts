// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { inject, Injectable, Injector, signal } from '@angular/core';

import { TM_NOW } from '../app/tm-now';
import { TM_BOOT_RESULT } from '../boot/tm-boot-result';
import { TmTelemetry } from '../observe/tm-telemetry';
import { TmClientStore } from '../state/store/tm-client-store';
import { TM_PROFILE_COOKIE, tmParseProfileCookie } from './tm-profile';
import { TmSession } from './tm-session';
import { TM_SESSION_HOST } from './tm-session-host';

// What the session does beyond the tab's own state when it starts, expires
// or finds another user (spec 0024 §8.1, §8.6, §8.7). The cross-tab paths
// need the drafts and the session-ended dialog, which build on the session;
// until they arrive, this tab-local version runs: a start tells no other
// tab; an expiry runs the clean-up's sweep, deletes the stale profile cookie
// and signs in again at once, as the dialog's Sign in will, so the user's
// data leaves the disk and the screen; and a changed user reloads the app at
// the root, whose boot sweeps the previous user's data. The full paths
// replace these bodies, not their call sites. The services the paths reach
// (the store, the session) are resolved when a path runs, since they inject
// the paths' state themselves.
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

/** The time box of the expiry's sweep: the boot script's expiry delete's. */
const EXPIRY_SWEEP_BOX_MS = 1_000;

/** The session's paths beyond this tab's own state, and the state they write. */
@Injectable({ providedIn: 'root' })
export class TmSessionPaths {
  readonly #telemetry = inject(TmTelemetry);
  readonly #host = inject(TM_SESSION_HOST);
  readonly #boot = inject(TM_BOOT_RESULT);
  readonly #now = inject(TM_NOW);
  readonly #injector = inject(Injector);

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
   * reported, every user's stored data is swept, a stale profile cookie is
   * deleted and the tab leaves to sign in again, coming back to the same
   * page. When this page itself came back from a sign-in less than a minute
   * ago, the sign-in is failing, so the page shows that instead of starting
   * another round, and its Try again button starts the next one. A `401` for
   * a request sent under a session the cookie no longer names belongs to a
   * session already replaced, and runs nothing.
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
    const restart = (): void => void this.#signInAgain();
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
   * The expiry's way out, as the session-ended dialog's Sign in takes it:
   * the clean-up's sweep, then the sign-in, which comes
   * back to this page. The sign-in, not a reload, leaves the tab: a reload
   * would reach the boot script's expiry branch and report the expiry a
   * second time.
   */
  async #signInAgain(): Promise<void> {
    // Spec 0024 §8.6, without the dialog and the broadcast.
    const store = this.#injector.get(TmClientStore);
    let box: ReturnType<typeof setTimeout> | undefined;
    const swept = await Promise.race([
      store.sweep('session-ended').then(
        () => true,
        () => false,
      ),
      new Promise<false>((resolve) => (box = setTimeout(() => resolve(false), EXPIRY_SWEEP_BOX_MS))),
    ]);
    clearTimeout(box);
    if (!swept) {
      // A sweep cut short: a boot record that names no user makes the next
      // launch's store sweep every user's data again (spec 0025 §2.6). The
      // last tenant stays, as the expiry path keeps it.
      store.boot.patch({ sub: null });
    }
    // After a 401 the cookie may still be present; deleting it keeps the
    // returning boot from running the app again into the same 401.
    this.#host.writeCookie(PROFILE_COOKIE_DELETE);
    await this.#injector.get(TmSession).signIn();
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

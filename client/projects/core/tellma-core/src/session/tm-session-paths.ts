// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { inject, Injectable } from '@angular/core';

import { TmTelemetry } from '../observe/tm-telemetry';
import { TM_SESSION_HOST } from './tm-session-host';
import { TmSessionStatus } from './tm-session-status';

// What the session does beyond the tab's own state when it starts, expires
// or finds another user (spec 0024 §8.1, §8.6, §8.7). The cross-tab paths
// need the tab bus, the client store and the drafts (spec 0025), which build
// on the session; until they arrive, this tab-local version runs: a start
// tells no other tab, an expiry marks this tab's session expired and reports
// it, and a changed user reloads the app at the root, whose boot sweeps the
// previous user's data. The full paths replace these bodies, not their call
// sites.

/** The session's paths beyond this tab's own state. */
@Injectable({ providedIn: 'root' })
export class TmSessionPaths {
  readonly #status = inject(TmSessionStatus);
  readonly #telemetry = inject(TmTelemetry);
  readonly #host = inject(TM_SESSION_HOST);

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
   * Runs the expiry path: the session is marked expired and the expiry is
   * reported, once.
   *
   * @param source - What found the expiry: the cookie check, or a `401` from the server.
   */
  expired(source: 'cookie' | '401'): void {
    if (this.#status.state() === 'active') {
      this.#status.state.set('expired');
      this.#telemetry.event('tm.session.expired', { source });
    }
  }

  /**
   * Runs the changed-user path: another user's session replaced this tab's,
   * so the app restarts at the root.
   */
  changedUser(): void {
    this.#telemetry.event('tm.session.changed-user');
    this.#host.location.replace('/');
  }
}

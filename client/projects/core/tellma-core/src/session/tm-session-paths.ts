// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { afterNextRender, inject, Injectable, Injector, signal } from '@angular/core';

import { TmTelemetry } from '../observe/tm-telemetry';
import { TmTabBus } from '../state/bus/tm-tab-bus';
import { TM_PROFILE_COOKIE, tmParseProfileCookie } from './tm-profile';
import type { TmSessionFlows } from './tm-session-flows';
import { TM_SESSION_HOST } from './tm-session-host';

// What the session does beyond the tab's own state (spec 0024 §8.4-§8.8):
// sign-out across tabs, signed out elsewhere, the expiry path with its
// session-ended dialog, the changed-user path and the re-check triggers.
// This file is the primary's part: the session's writable state, which
// `TmSession` shows read-only, the expiry's synchronous half (the state
// turns `expired` at once, so the HTTP layer stops sending), and the
// routing of the tab bus's session messages and drafts queries. The flows,
// the re-check triggers among them, live in a chunk of their own, which the app loads at idle
// after its first render and every path loads on demand, since no first
// frame needs them. A chunk that cannot load falls back to what the boot
// script does on its own: the expiry and a signed-out session reload into
// the boot's expiry branch, and a changed user restarts the app at the root.

/** Where the tab's session stands. */
export type TmSessionState = 'active' | 'expired' | 'signing-out';

/** The assignment that deletes the profile cookie, with the attributes the server sets it with. */
export const TM_PROFILE_COOKIE_DELETE = `${TM_PROFILE_COOKIE}=; Max-Age=0; Path=/; Secure`;

/** The session's paths beyond this tab's own state, and the state they write. */
@Injectable({ providedIn: 'root' })
export class TmSessionPaths {
  readonly #injector = inject(Injector);
  readonly #host = inject(TM_SESSION_HOST);
  readonly #telemetry = inject(TmTelemetry);

  /** The flows' chunk, once asked for. */
  #flows: Promise<TmSessionFlows> | undefined;

  /** The session's state: active until an expiry or a sign-out. */
  readonly state = signal<TmSessionState>('active');

  /**
   * Starts listening: the other tabs' session messages and drafts queries
   * go to the flows, and the flows' chunk loads after the first render, so
   * a sign-out shows its overlay in the click's frame. Internal: the startup
   * calls it once.
   */
  ɵstart(): void {
    this.#injector.get(TmTabBus).messages.subscribe((message) => {
      if (message.type === 'drafts.query' || message.type.startsWith('session.')) {
        this.#run((flows) => flows.received(message));
      }
    });
    afterNextRender(() => void this.#load().catch(() => undefined), { injector: this.#injector });
  }

  /**
   * Announces that this tab's session started, after a sign-in, so that the
   * user's other tabs that saw the session end reload.
   *
   * @param sub - The signed-in user's subject identifier.
   */
  started(sub: string): void {
    this.#run((flows) => flows.started(sub));
  }

  /**
   * Runs the expiry path, once: the session turns expired at once, the
   * expiry is reported, and the flows clean up, tell the user's other tabs
   * and show the session-ended dialog. A `401` for a request sent under a
   * session the cookie no longer names belongs to a session already
   * replaced, and runs nothing.
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
    this.#telemetry.event('tm.session.expired', { source });
    this.#telemetry.flush();
    this.state.set('expired');
    this.#run((flows) => flows.expired(), this.#reboot);
  }

  /**
   * Runs the changed-user path: another user's session replaced this tab's,
   * so the tab drops the previous user's drafts, tells the other tabs and
   * restarts the app at the root.
   */
  changedUser(): void {
    this.#run(
      (flows) => flows.changedUser(),
      () => this.#host.location.replace('/'),
    );
  }

  /**
   * Runs the signed-out-elsewhere path: the server ended the session from
   * another device or an administrator's action, which the leader tab heard
   * from its realtime connection. Every tab cleans up and shows the
   * signed-out page. Internal: the leader tab calls it.
   */
  ɵsignedOutElsewhere(): void {
    this.#run((flows) => flows.signedOutElsewhere(), this.#reboot);
  }

  /**
   * Signs out of every tab of the user, after waiting for running saves and
   * asking about unsaved changes. Internal: `TmSession.signOut()` calls it.
   *
   * @returns A promise that never settles once the tab leaves; it resolves
   *   when the user cancels, and rejects when the sign-out fails.
   */
  ɵsignOut(): Promise<void> {
    return this.#load().then((flows) => flows.signOut());
  }

  /** Loads the flows' chunk once; a failed load is tried again next time. */
  #load(): Promise<TmSessionFlows> {
    return (this.#flows ??= import('./tm-session-flows').then(
      ({ TmSessionFlows: flows }) => this.#injector.get(flows),
      (error: unknown) => {
        this.#flows = undefined;
        throw error;
      },
    ));
  }

  /**
   * Runs a path in the flows; when their chunk cannot load, the fallback
   * runs instead, and the failure is reported.
   */
  #run(path: (flows: TmSessionFlows) => unknown, fallback?: () => void): void {
    this.#load().then(path, (error: unknown) => {
      this.#telemetry.error(error, { source: 'tm-session' });
      fallback?.();
    });
  }

  /**
   * The fallback of an ended session: with the stale cookie deleted, a
   * reload reaches the boot script's expiry branch, which deletes the
   * user's stored data and signs in again.
   */
  readonly #reboot = (): void => {
    this.#host.writeCookie(TM_PROFILE_COOKIE_DELETE);
    this.#host.location.reload();
  };
}

// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { inject, Injectable, signal, type Signal } from '@angular/core';

import { TM_NOW } from '../app/tm-now';
import { TM_BOOT_RESULT } from '../boot/tm-boot-result';
import { TmTelemetry } from '../observe/tm-telemetry';
import { tmIsLocalPath, tmLoginUrl, tmRemoveLoginMark, tmWriteLoginMark } from './tm-login-mark';
import { tmParseProfileCookie, type TmProfile } from './tm-profile';
import { TM_SESSION_HOST, type TmSessionStorage } from './tm-session-host';
import { TmSessionPaths, type TmSessionState } from './tm-session-paths';

// The tab's session (spec 0024 §8.1-§8.3). The profile cookie is the
// browser's synchronous signed-in signal: the session reads it at its first
// injection and again at every check, and never asks the network. A sign-in
// and a step-up leave the tab for the BFF's login with the sign-in mark in
// session storage, which the boot script consumes when the tab comes back.

/** The authentication a `401 step-up-required` asks for, as its `WWW-Authenticate` header names it. */
export interface TmStepUpChallenge {
  /** The authentication class the call requires, such as `mfa`. */
  readonly acr: string;
  /** The largest age, in seconds, of a sign-in the call accepts, or `null` for any age. */
  readonly maxAge: number | null;
}

/**
 * What the profile cookie says now, against the user the tab runs as: the
 * same user, no session, or another user.
 */
export type TmSessionCheck = 'same' | 'missing' | 'changed';

/** How to sign in. */
export interface TmSignInOptions {
  /**
   * The local path and query to come back to; the current path and query
   * when omitted. `'/'` lets the platform pick the tenant.
   */
  readonly returnUrl?: string;
  /** The stronger authentication a call asked for; omitted for a plain sign-in. */
  readonly stepUp?: TmStepUpChallenge;
}

/** The prefix of the session-storage keys of a user's drafts. */
const DRAFT_KEY_PREFIX = 'tm.draft:';

/**
 * The tab's session: the signed-in user, where the session stands, and the
 * way to sign in again.
 *
 * @example
 * ```ts
 * const session = inject(TmSession);
 * const name = computed(() => session.profile()?.name ?? '');
 * ```
 */
@Injectable({ providedIn: 'root' })
export class TmSession {
  readonly #host = inject(TM_SESSION_HOST);
  readonly #now = inject(TM_NOW);
  readonly #telemetry = inject(TmTelemetry);
  readonly #paths = inject(TmSessionPaths);
  readonly #profile = signal<TmProfile | null>(tmParseProfileCookie(this.#host.cookies()));

  /** The signed-in user, or `null` when the tab started with no session. */
  readonly profile: Signal<TmProfile | null> = this.#profile.asReadonly();

  /** Where the tab's session stands: active, expired, or signing out. */
  readonly state: Signal<TmSessionState> = this.#paths.state.asReadonly();

  /** Creates the session and consumes the sign-in the boot script reported, if any. */
  constructor() {
    this.#consumeSignIn();
  }

  /**
   * Reads the profile cookie now and compares it with the tab's user. A
   * cookie of the same user with other details (a new name or locale)
   * updates the profile.
   *
   * @returns `'same'` for the tab's user, `'missing'` for no session, `'changed'` for another user.
   */
  check(): TmSessionCheck {
    const now = tmParseProfileCookie(this.#host.cookies());
    if (now === null) {
      return 'missing';
    }
    const current = this.#profile();
    if (current === null || current.sub !== now.sub) {
      return 'changed';
    }
    // Both are rebuilt by the decoder with the same members in the same
    // order, so their JSON differs exactly when a detail does.
    if (JSON.stringify(current) !== JSON.stringify(now)) {
      this.#profile.set(now);
    }
    return 'same';
  }

  /**
   * Leaves the tab for the sign-in page, which returns to `returnUrl` signed
   * in. Unsaved drafts survive the round trip in session storage. The
   * returned promise never settles, because the page unloads.
   *
   * @param options - Where to return and, for a step-up, what the call asked for.
   * @returns A promise that never settles.
   * @throws Error when `returnUrl` is not a local path, or the step-up challenge is malformed.
   */
  signIn(options: TmSignInOptions = {}): Promise<never> {
    const location = this.#host.location;
    const current = location.pathname + location.search;
    const { returnUrl: requested, stepUp } = options;
    if (requested !== undefined && !tmIsLocalPath(requested)) {
      throw new Error(`The sign-in return URL '${requested}' is not a local path.`);
    }
    if (
      stepUp !== undefined &&
      (typeof stepUp.acr !== 'string' ||
        stepUp.acr === '' ||
        !(stepUp.maxAge === null || (Number.isSafeInteger(stepUp.maxAge) && stepUp.maxAge >= 0)))
    ) {
      throw new Error('The step-up challenge needs an authentication class and a whole, non-negative maximum age.');
    }
    // A current path that is not local (a doubled slash) never travels as a
    // return URL; the root lets the platform pick instead.
    const returnUrl = requested ?? (tmIsLocalPath(current) ? current : '/');
    tmWriteLoginMark(this.#host.sessionStorage, {
      at: this.#now(),
      expired: false,
      returnUrl,
      auto: returnUrl === '/',
      sub: this.#profile()?.sub ?? null,
      // The fragment belongs to the current page, so it travels only when the
      // tab returns to that page.
      hash: returnUrl === current ? location.hash : '',
    });
    if (stepUp !== undefined) {
      this.#telemetry.event('tm.session.step-up');
    }
    let url = tmLoginUrl(returnUrl, this.#host.language() || 'en');
    if (stepUp !== undefined) {
      // The class and the age the call asked for (spec 0024 §8.3).
      url += `&acr_values=${encodeURIComponent(stepUp.acr)}`;
      if (stepUp.maxAge !== null) {
        url += `&max_age=${stepUp.maxAge}`;
      }
    }
    location.assign(url);
    return new Promise<never>(() => undefined);
  }

  /**
   * Consumes the sign-in the boot script reported: the session starts, an
   * expiry that led to the sign-in is reported, and a previous user's drafts
   * are dropped.
   */
  #consumeSignIn(): void {
    // Spec 0024 §8.1, "After a sign-in".
    const mark = inject(TM_BOOT_RESULT)?.mark ?? null;
    const profile = this.#profile();
    if (mark === null || profile === null) {
      return;
    }
    // The boot script consumed the mark already; this removal only covers a
    // boot that could not.
    tmRemoveLoginMark(this.#host.sessionStorage);
    this.#paths.started(profile.sub);
    if (mark.expired) {
      this.#telemetry.event('tm.session.expired', { source: 'boot' });
    }
    if (mark.sub !== null && mark.sub !== profile.sub) {
      removeDrafts(this.#host.sessionStorage, mark.sub);
    }
  }
}

/** Removes a user's drafts from session storage; a refusing storage is ignored. */
function removeDrafts(storage: TmSessionStorage | null, sub: string): void {
  if (storage === null) {
    return;
  }
  const prefix = `${DRAFT_KEY_PREFIX}${sub}:`;
  try {
    // Keys are collected first, since removing an item shifts the indexes.
    const keys: string[] = [];
    for (let i = 0; i < storage.length; i++) {
      const key = storage.key(i);
      if (key?.startsWith(prefix)) {
        keys.push(key);
      }
    }
    for (const key of keys) {
      storage.removeItem(key);
    }
  } catch {
    // Nothing to do: a storage that refuses holds no drafts this tab can reach.
  }
}

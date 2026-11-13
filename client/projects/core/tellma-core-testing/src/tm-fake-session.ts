// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { Injectable, signal, untracked, type Signal } from '@angular/core';
import type { TmProfile, TmSession, TmSessionCheck, TmSessionState, TmSignInOptions } from '@tellma/core';

/**
 * The profile of the test user, with any member replaced.
 *
 * @param patch - The members to replace.
 * @returns The profile.
 */
export function tmTestProfile(patch: Partial<Omit<TmProfile, 'v'>> = {}): TmProfile {
  return {
    v: 2,
    sub: 'test-user',
    sh: 'test-session',
    name: 'Test User',
    email: 'test.user@example.com',
    locale: 'en',
    ...patch,
  };
}

/**
 * A {@link TmSession} the test drives: signed in as {@link tmTestProfile}'s
 * user and active from the start, it changes only when the test says so, and
 * records sign-ins instead of leaving the page.
 *
 * @example
 * ```ts
 * TestBed.configureTestingModule({ providers: [provideTellmaTesting(), provideTellmaStateTesting()] });
 * TestBed.inject(TmFakeSession).setState('expired');
 * await expect(TestBed.inject(TmClientStore).put(partition, 'me', doc)).rejects.toThrow(TmStoreRefusedError);
 * ```
 */
@Injectable()
export class TmFakeSession implements Pick<TmSession, 'profile' | 'state' | 'check' | 'signIn'> {
  readonly #profile = signal<TmProfile | null>(tmTestProfile());
  readonly #state = signal<TmSessionState>('active');
  readonly #signIns = signal<readonly TmSignInOptions[]>([]);
  /** What the cookie check reports, or `null` to follow the profile. */
  #check: TmSessionCheck | null = null;

  /** The signed-in user, or `null`: the test user until the test sets another. */
  readonly profile: Signal<TmProfile | null> = this.#profile.asReadonly();

  /** The session's state: active until the test sets another. */
  readonly state: Signal<TmSessionState> = this.#state.asReadonly();

  /** The options of every sign-in the code under test started, oldest first. */
  readonly signIns: Signal<readonly TmSignInOptions[]> = this.#signIns.asReadonly();

  /**
   * What the profile cookie says now: what the test set with
   * {@link TmFakeSession.setCheck}, else `same` with a profile and `missing`
   * without one. Tracks nothing, as the real check.
   *
   * @returns `'same'`, `'missing'` or `'changed'`.
   */
  check(): TmSessionCheck {
    return this.#check ?? (untracked(this.#profile) === null ? 'missing' : 'same');
  }

  /**
   * Records the sign-in; the page stays.
   *
   * @param options - Where to return and, for a step-up, what the call asked for.
   * @returns A promise that never settles, as the real sign-in's.
   */
  signIn(options: TmSignInOptions = {}): Promise<never> {
    this.#signIns.update((signIns) => [...signIns, options]);
    return new Promise<never>(() => undefined);
  }

  /**
   * Sets the signed-in user.
   *
   * @param profile - The user's profile, or `null` for no session.
   */
  setProfile(profile: TmProfile | null): void {
    this.#profile.set(profile);
  }

  /**
   * Sets the session's state.
   *
   * @param state - The state.
   */
  setState(state: TmSessionState): void {
    this.#state.set(state);
  }

  /**
   * Sets what the cookie check reports, as when another tab signed out or in.
   *
   * @param check - The result, or `null` to follow the profile again.
   */
  setCheck(check: TmSessionCheck | null): void {
    this.#check = check;
  }
}

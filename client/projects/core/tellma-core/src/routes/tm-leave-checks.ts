// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { DestroyRef, Injectable } from '@angular/core';

// The leave checks of one routed page (spec 0024 §9.2; spec 0031 §11.3). The
// route host provides a registry beside the page's scope; the details
// framework's leave guard registers a check in it while its page lives, and
// the page route's `canDeactivate` runs every registered check before a
// navigation leaves the page.

/** One leave check: whether the page may be left, asking the user when it must. */
export type TmLeaveCheck = () => boolean | Promise<boolean>;

/**
 * The leave checks registered in one routed page. Internal: the page
 * frameworks register through their own leave guard.
 */
@Injectable()
export class TmLeaveChecks {
  /** The registered checks, in registration order. */
  readonly #checks = new Set<TmLeaveCheck>();

  /**
   * Registers a check while its caller lives.
   *
   * @param check - The check.
   * @param destroyRef - The caller's life; the check leaves the registry when it ends.
   */
  add(check: TmLeaveCheck, destroyRef: DestroyRef): void {
    this.#checks.add(check);
    destroyRef.onDestroy(() => this.#checks.delete(check));
  }

  /**
   * Runs the checks in turn; the first refusal stops the rest.
   *
   * @returns Whether the page may be left.
   */
  async run(): Promise<boolean> {
    for (const check of [...this.#checks]) {
      if (!(await check())) {
        return false;
      }
    }
    return true;
  }
}

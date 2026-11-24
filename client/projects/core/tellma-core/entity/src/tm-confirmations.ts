// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { inject, Injectable, Injector } from '@angular/core';
import { TmApiError, type TmId } from '@tellma/core';

import { ɵtmLoad } from './tm-entity-actions';

// The confirmation loop (spec 0031 §9.3): a write the server answers with a
// 409 `confirmation-required` lists the warnings the user must confirm, and
// the client resends it with their keys, which `TmApi.write` sends as
// `Tellma-Confirm`. Saves, deletes, activation and entity actions all run
// through it, and the search page's bulk actions share it. The first send is
// here; the dialog and the rounds after it load on the first warning, so a
// page whose writes are never warned never downloads them.

/** One send of a write under {@link TmConfirmations.run}: the keys confirmed so far, and the rows to leave out. */
export interface TmConfirmationAttempt {
  /** The warning keys confirmed so far in this run, to send as `Tellma-Confirm`. */
  readonly confirmations: readonly string[];
  /** The rows the user chose to leave out ("Continue without these"), to drop from the request's ids. */
  readonly skip: readonly TmId[];
}

/** What a confirmation loop's dialog needs to know of the write it confirms. */
export interface TmConfirmationContext {
  /** The entity the write's rows belong to, such as `acme.Customer`, which names the rows the warnings list. */
  readonly entity?: string;
  /** The rows a bulk write targets, so a warning on some of them offers to continue without them. */
  readonly ids?: readonly TmId[];
  /** The warning keys already confirmed, sent from the first try. */
  readonly confirmations?: readonly string[];
}

/**
 * Runs a write through the server's warnings: when the server asks the
 * user to confirm one or more warnings, a dialog lists them, with their
 * rows, and the write is sent again with the confirmed keys. A bulk write
 * whose warnings name only some of its rows also offers to continue
 * without those rows. The keys confirmed live for one run only.
 *
 * @example
 * ```ts
 * const confirmations = inject(TmConfirmations);
 * const result = await confirmations.run(
 *   ({ confirmations: keys, skip }) => api.write('customers/delete', { ids: ids.filter((id) => !skip.includes(id)) }, { confirmations: keys }),
 *   { entity: 'acme.Customer', ids },
 * ); // null: the user declined
 * ```
 */
@Injectable({ providedIn: 'root' })
export class TmConfirmations {
  readonly #injector = inject(Injector);

  /**
   * Sends a write, and again after each warning the user confirms. A fifth
   * warning response in one run, or more confirmed keys than a request
   * carries, fails as a `server` error; when the dialog's code cannot
   * load, the warned write fails as `offline`, not done.
   *
   * @param send - Sends the write with an attempt's keys and the rows to leave out.
   * @param context - The write's entity and rows, and any keys already confirmed.
   * @returns The write's result; `null` when the user declined a warning.
   * @throws TmApiError for any failure other than a warning, as `send` rejects.
   */
  async run<T>(send: (attempt: TmConfirmationAttempt) => Promise<T>, context: TmConfirmationContext = {}): Promise<T | null> {
    try {
      return await send({ confirmations: context.confirmations ?? [], skip: [] });
    } catch (error) {
      if (!(error instanceof TmApiError) || error.kind !== 'confirmation') {
        throw error;
      }
      // The dialog and the rounds after the first warning load now.
      const { ɵtmConfirmationRounds } = await ɵtmLoad(() => import('./tm-confirmations-dialog'));
      return ɵtmConfirmationRounds(this.#injector, send, context, error);
    }
  }
}

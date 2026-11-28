// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { inject, Injectable, Injector } from '@angular/core';

// The editor's dialogs (spec 0031 §9.1, §9.2, §11.3): a save that met a
// newer version, a save that met a deleted record, and a dirty page being
// left. The service ships with the route and its dialogs' copy and logic
// load on first use, so a page that never asks downloads none of them. Any
// editor page may reuse them.

/** Who saved the newer version a save met, as the server's conflict names it; every member may be absent. */
export interface TmConflictInfo {
  /** The id of the user who saved it. */
  readonly modifiedById?: number;
  /** That user's name, in the request's language. */
  readonly modifiedByName?: string;
  /** When it was saved: the stored stamp, as the server sent it. */
  readonly modifiedAt?: string;
}

/** The dialogs' implementations, as their chunk exports them. */
type TmDetailsDialogsBody = typeof import('./tm-details-dialogs-body');

/**
 * The editor's dialogs: the concurrency dialog, the deletion dialog and the
 * leave dialog, each an alert dialog focused on its primary choice.
 *
 * @example
 * ```ts
 * const choice = await inject(TmDetailsDialogs).conflict({ modifiedByName: 'Sara', modifiedAt: stamp });
 * if (choice === 'overwrite') {
 *   await save('Override');
 * }
 * ```
 */
@Injectable({ providedIn: 'root' })
export class TmDetailsDialogs {
  readonly #injector = inject(Injector);

  /**
   * Asks what to do about a save that met a newer version: reload the saved
   * one, or overwrite it. The copy names who saved it and when: another
   * user, the current user in another tab or window, or no one known.
   *
   * @param conflict - Who saved the newer version, and when; `null` when unknown.
   * @returns `reload`, `overwrite`, or `null` when the dialog was dismissed.
   */
  async conflict(conflict: TmConflictInfo | null): Promise<'reload' | 'overwrite' | null> {
    return (await this.#body()).ɵtmConflictDialog(this.#injector, conflict);
  }

  /**
   * Says that the record was deleted while it was edited, so the changes cannot be saved.
   *
   * @returns `discard`, or `null` when the user kept editing to copy values out.
   */
  async deleted(): Promise<'discard' | null> {
    return (await this.#body()).ɵtmDeletedDialog(this.#injector);
  }

  /**
   * Asks whether to leave a page with unsaved changes.
   *
   * @returns `discard` to leave without saving, or `stay`, which Escape also answers.
   */
  async leave(): Promise<'discard' | 'stay'> {
    return (await this.#body()).ɵtmLeaveDialog(this.#injector);
  }

  /**
   * Loads the dialogs' chunk.
   *
   * @returns Its exports.
   */
  #body(): Promise<TmDetailsDialogsBody> {
    return import('./tm-details-dialogs-body');
  }
}

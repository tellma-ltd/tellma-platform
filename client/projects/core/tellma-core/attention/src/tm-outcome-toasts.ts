// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { inject, Injectable } from '@angular/core';
import { Router } from '@angular/router';
import { TM_TOAST_FEEDBACK_MS, TmLocalization, TmUrls } from '@tellma/core';
import { type TmToastRef, TmToasts } from '@tellma/core-ui/toast';

import { tmRegisterAttentionStrings } from './tm-attention-strings';

// The outcomes the platform's own surfaces toast (spec 0028 §8.1), each
// raised by the acting tab: a job started in the background, with Track,
// and a synchronous export in progress, with Cancel. Every other outcome on
// the closed list calls TmToasts with its own copy and TM_TOAST_FEEDBACK_MS.

/**
 * The outcome toasts of the background work a page starts.
 *
 * @example
 * ```ts
 * const outcomes = inject(TmOutcomeToasts);
 * outcomes.jobStarted(accepted.jobId);
 * const exporting = outcomes.exporting(() => controller.abort());
 * exporting.update({ detail: '5,580 of 9,000 rows' });
 * exporting.close();
 * ```
 */
@Injectable({ providedIn: 'root' })
export class TmOutcomeToasts {
  readonly #toasts = inject(TmToasts);
  readonly #urls = inject(TmUrls);
  readonly #router = inject(Router);
  readonly #l10n = inject(TmLocalization);

  /** Creates the service, which registers the attention strings. */
  constructor() {
    tmRegisterAttentionStrings(this.#l10n);
  }

  /**
   * "Started in the background.", after a start the server accepted as a
   * job; Track opens the job's page.
   *
   * @param jobId - The job the start enqueued.
   * @returns The toast.
   */
  jobStarted(jobId: number): TmToastRef {
    const target = this.#urls.target('core.Job', jobId);
    return this.#toasts.show({
      tone: 'accent',
      title: this.#text('outcomes.started'),
      duration: TM_TOAST_FEEDBACK_MS,
      key: `job:${jobId}`,
      ...(target === null ? {} : { action: { label: this.#text('outcomes.track'), run: () => void this.#router.navigateByUrl(target) } }),
    });
  }

  /**
   * "Exporting…", sticky while a synchronous export runs in the tab; its
   * progress line updates through the returned toast, which the export
   * closes when it ends.
   *
   * @param cancel - Cancels the export; the toast closes with it.
   * @returns The toast.
   */
  exporting(cancel: () => void): TmToastRef {
    return this.#toasts.show({
      tone: 'neutral',
      title: this.#text('outcomes.exporting'),
      duration: 'sticky',
      action: { label: this.#text('outcomes.cancel'), run: cancel },
    });
  }

  /** An attention string. */
  #text(key: string): string {
    return this.#l10n.ɵtext(`tm.attention.${key}`)();
  }
}

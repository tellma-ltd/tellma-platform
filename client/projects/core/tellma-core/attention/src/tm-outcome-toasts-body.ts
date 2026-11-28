// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { inject } from '@angular/core';
import { Router } from '@angular/router';
import { TM_TOAST_FEEDBACK_MS, TmLocalization, TmUrls } from '@tellma/core';
import { type TmToastRef, TmToasts } from '@tellma/core-ui/toast';

// The outcome toasts' body (spec 0028 §8.1): a job started in the
// background, with Track, and a synchronous export in progress, with
// Cancel. It loads as a chunk of its own at the first outcome, so the first
// frame carries only the facade that loads it.

/**
 * The outcome toasts themselves. Internal: `TmOutcomeToasts` creates it in
 * an injection context once its chunk loads.
 */
export class TmOutcomeToastsBody {
  readonly #toasts = inject(TmToasts);
  readonly #urls = inject(TmUrls);
  readonly #router = inject(Router);
  readonly #l10n = inject(TmLocalization);

  /**
   * "Started in the background.", with Track when the job has a page.
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
   * "Exporting…", sticky, with Cancel.
   *
   * @param cancel - Cancels the export.
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

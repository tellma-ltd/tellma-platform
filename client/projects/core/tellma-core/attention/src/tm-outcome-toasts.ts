// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { inject, Injectable, Injector, runInInjectionContext } from '@angular/core';
import { TmLocalization, TmTelemetry } from '@tellma/core';
import type { TmToast, TmToastRef } from '@tellma/core-ui/toast';

import { tmRegisterAttentionStrings } from './tm-attention-strings';
import type { TmOutcomeToastsBody } from './tm-outcome-toasts-body';

// The outcomes the platform's own surfaces toast (spec 0028 §8.1), each
// raised by the acting tab: a job started in the background, with Track,
// and a synchronous export in progress, with Cancel. Every other outcome on
// the closed list calls TmToasts with its own copy and TM_TOAST_FEEDBACK_MS.
// The toasts themselves load as a chunk at the first outcome, so the first
// frame carries only this facade; each call answers at once with a handle
// that holds its updates and its close until the toast shows.

/**
 * The outcome toasts of the background work a page starts. Each call
 * answers its toast's handle at once; the toast shows once its code has
 * loaded, at the first outcome. An update before then applies as it shows,
 * a close before then shows nothing, and a load that fails is reported and
 * shows nothing.
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
  readonly #injector = inject(Injector);
  readonly #telemetry = inject(TmTelemetry);

  /** The toasts' body, once its chunk has loaded. */
  #body: Promise<TmOutcomeToastsBody> | null = null;

  /** Creates the service, which registers the attention strings. */
  constructor() {
    tmRegisterAttentionStrings(inject(TmLocalization));
  }

  /**
   * "Started in the background.", after a start the server accepted as a
   * job; Track opens the job's page.
   *
   * @param jobId - The job the start enqueued.
   * @returns The toast's handle.
   */
  jobStarted(jobId: number): TmToastRef {
    return this.#show((body) => body.jobStarted(jobId));
  }

  /**
   * "Exporting…", sticky while a synchronous export runs in the tab; its
   * progress line updates through the returned toast, which the export
   * closes when it ends.
   *
   * @param cancel - Cancels the export; the toast closes with it.
   * @returns The toast's handle.
   */
  exporting(cancel: () => void): TmToastRef {
    return this.#show((body) => body.exporting(cancel));
  }

  /**
   * Shows a toast once the body's chunk has loaded, answering its handle at
   * once. A chunk that cannot load is reported, closes the handle and loads
   * again next time.
   */
  #show(show: (body: TmOutcomeToastsBody) => TmToastRef): TmToastRef {
    let shown: TmToastRef | undefined;
    // The updates made before the toast shows; `undefined` once it is closed before then.
    let held: Partial<TmToast> | undefined = {};
    let settle!: (reason: Awaited<TmToastRef['closed']>) => void;
    const closed = new Promise<Awaited<TmToastRef['closed']>>((resolve) => (settle = resolve));
    this.#body ??= import('./tm-outcome-toasts-body').then(({ TmOutcomeToastsBody }) =>
      runInInjectionContext(this.#injector, () => new TmOutcomeToastsBody()),
    );
    void this.#body.then(
      (body) => {
        // A close before the chunk landed shows nothing.
        if (held) {
          shown = show(body);
          // With no update made meanwhile, an empty one in the frame it shows changes nothing.
          shown.update(held);
          void shown.closed.then(settle);
        }
      },
      (error: unknown) => {
        this.#body = null;
        this.#telemetry.error(error, { source: 'tm-outcome-toasts' });
        settle('api');
      },
    );
    return {
      closed,
      update: (patch) => (shown ? shown.update(patch) : held && (held = { ...held, ...patch })),
      // A close is the API's, whether or not the toast showed.
      close: () => {
        held = undefined;
        shown?.close();
        settle('api');
      },
    };
  }
}

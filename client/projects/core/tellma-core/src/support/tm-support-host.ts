// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { NgComponentOutlet } from '@angular/common';
import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  DOCUMENT,
  effect,
  inject,
  type Injector,
  signal,
  type Type,
  untracked,
} from '@angular/core';
import { Router } from '@angular/router';
import { TmButton } from '@tellma/core-ui/button';
import { TM_MODAL_DATA, TmModal, TmModalFooter, TmModalRef } from '@tellma/core-ui/modal';
import { TmSkeleton } from '@tellma/core-ui/skeleton';

import type { TmApiError } from '../api/tm-api-error';
import { TM_NOW } from '../app/tm-now';
import type { TmPageContext } from '../context/tm-page-context';
import { TmPageContexts } from '../context/tm-page-contexts';
import type { TmJsonObject } from '../json/tm-json';
import { TmLocalization } from '../l10n/tm-localization';
import { TmTelemetry } from '../observe/tm-telemetry';
import { TM_SUPPORT_LOAD, type TmPageSnapshotModule, type TmSupportContexts, type TmSupportSnapshot } from './tm-support';
import { ɵTmSupportOffline, tmRegisterSupportHostStrings } from './tm-support-offline';

// The support host (spec 0026 §10.4, §12.1), a lazy chunk of the primary
// that the support facade preloads at idle: it snapshots the page contexts
// synchronously, opens the dialog in the press's frame with a placeholder in
// the dialog's final size, and then loads @tellma/core/support into it. When
// that entry point cannot load, the dialog shows the line that names Copy
// diagnostics' shortcut. A download goes the same way without a dialog.
// The chunk also hands that line to the About dialog's Diagnostics tab,
// which shows it when the entry point cannot load there (spec 0026 §8.4).

export { ɵTmSupportOffline };

/** What the support dialog's host receives. */
interface TmSupportHostData {
  /** The snapshot taken in the press. */
  readonly snapshot: TmSupportSnapshot;
  /** The error the dialog opened from, if any. */
  readonly error: TmApiError | undefined;
}

/**
 * Snapshots the page contexts, both with page data and without, and the
 * page's address, synchronously. A context whose snapshot throws is left
 * out and reported to telemetry.
 *
 * @param injector - The root injector.
 * @param pages - The page-snapshot entry point.
 * @param error - The error the dialog opens from, if any.
 * @returns The snapshot.
 */
function snapshot(injector: Injector, pages: TmPageSnapshotModule, error: TmApiError | undefined): TmSupportSnapshot {
  return untracked(() => {
    const contexts = injector.get(TmPageContexts);
    const current = contexts.current();
    const overlays = contexts.overlays();
    // --- Each context read once per serialisation ---
    const read = (includeData: boolean): TmSupportContexts<TmJsonObject> => {
      const one = (context: TmPageContext): TmJsonObject | null => {
        try {
          return pages.tmSerializePageContext(context, { includeData, injector });
        } catch (failure) {
          injector.get(TmTelemetry).error(failure, { where: 'tm.support.snapshot' });
          return null;
        }
      };
      return {
        current: current === null ? null : one(current),
        overlays: overlays.map(one).filter((shown): shown is TmJsonObject => shown !== null),
      };
    };
    return {
      at: injector.get(TM_NOW)(),
      withData: read(true),
      withoutData: read(false),
      url: injector.get(Router).url,
      routeTemplate: current?.routeTemplate ?? '',
      historyLength: injector.get(DOCUMENT).defaultView?.history.length ?? 0,
      ...(error === undefined ? {} : { reported: { traceId: error.traceId, kind: error.kind, code: error.code } }),
    };
  });
}

/**
 * The support dialog's content until the support entry point lands: a
 * placeholder in the dialog's final size, then the entry point's dialog, or
 * the offline line with Close when it cannot load. Internal.
 */
@Component({
  selector: 'tm-support-host',
  imports: [NgComponentOutlet, TmButton, TmModalFooter, TmSkeleton, ɵTmSupportOffline],
  changeDetection: ChangeDetectionStrategy.OnPush,
  styles: `
    /* The dialog's final size, so nothing moves when the content replaces the placeholder. */
    .tm-support-host__placeholder {
      display: grid;
      gap: var(--space-4);
      min-block-size: 24rem;
    }
  `,
  template: `
    @switch (state()) {
      @case ('loading') {
        <!-- The placeholder, busy until the support entry point lands. -->
        <div class="tm-support-host__placeholder" role="status" aria-busy="true" [attr.aria-label]="text.loading()">
          <tm-skeleton [lines]="2" />
          <tm-skeleton shape="table" [lines]="6" [columns]="4" />
        </div>
      }
      @case ('ready') {
        <!-- The support entry point's dialog, over the snapshot taken in the press. -->
        <ng-container *ngComponentOutlet="dialog(); inputs: inputs" />
      }
      @case ('failed') {
        <!-- The entry point did not load: the shortcut still copies the diagnostics. -->
        <tm-support-offline kind="dialog" />
        <div tmModalFooter>
          <button tmButton type="button" variant="primary" (click)="close()">{{ text.close() }}</button>
        </div>
      }
    }
  `,
})
export class ɵTmSupportHost {
  readonly #data = inject<TmSupportHostData>(TM_MODAL_DATA);
  readonly #ref = inject(TmModalRef);
  readonly #l10n = inject(TmLocalization);

  /** The labels. */
  protected readonly text = { loading: this.#l10n.ɵtext('tm.supportHost.loading'), close: this.#l10n.ɵtext('tm.supportHost.close') };

  /** Where the content stands. */
  protected readonly state = signal<'loading' | 'ready' | 'failed'>('loading');

  /** The entry point's dialog, once loaded. */
  protected readonly dialog = signal<Type<unknown> | null>(null);

  /** The dialog's inputs. */
  protected readonly inputs: Readonly<Record<string, unknown>> = { snapshot: this.#data.snapshot, error: this.#data.error };

  /** Loads the support entry point into the dialog. */
  constructor() {
    let alive = true;
    inject(DestroyRef).onDestroy(() => (alive = false));
    const telemetry = inject(TmTelemetry);
    inject(TM_SUPPORT_LOAD)().then(
      (module) => {
        if (alive) {
          this.dialog.set(module.TmSupportDialog);
          this.state.set('ready');
        }
      },
      (error: unknown) => {
        telemetry.error(error, { where: 'tm.support.load' });
        if (alive) {
          this.state.set('failed');
        }
      },
    );
  }

  /** Close, when the entry point did not load. */
  protected close(): void {
    this.#ref.close();
  }
}

/**
 * Snapshots the page contexts and opens the support dialog over them, all
 * in the caller's frame. Internal: the support facade calls it.
 *
 * @param injector - The root injector.
 * @param pages - The page-snapshot entry point.
 * @param error - The error the dialog opens from, if any.
 */
export function ɵtmOpenSupport(injector: Injector, pages: TmPageSnapshotModule, error: TmApiError | undefined): void {
  // The snapshot comes first, so the dialog is not in it.
  const data: TmSupportHostData = { snapshot: snapshot(injector, pages, error), error };
  const l10n = injector.get(TmLocalization);
  tmRegisterSupportHostStrings(l10n);
  const title = l10n.ɵtext('tm.supportHost.title');
  const ref = injector.get(TmModal).open(ɵTmSupportHost, { size: 'md', role: 'dialog', title: untracked(title), data });
  // --- The title follows a language switch while the dialog is open ---
  const retitle = effect(() => ref.ɵsetTitle(title()), { injector });
  void ref.closed.then(() => retitle.destroy());
}

/**
 * Snapshots the page contexts, then builds the dump without page data and
 * downloads it. Internal: the support facade calls it.
 *
 * @param injector - The root injector.
 * @param pages - The page-snapshot entry point.
 * @returns A promise that settles once the download has started.
 */
export async function ɵtmDownloadSupport(injector: Injector, pages: TmPageSnapshotModule): Promise<void> {
  const taken = snapshot(injector, pages, undefined);
  const support = await injector.get(TM_SUPPORT_LOAD)();
  support.tmDownloadSupportDump(await support.tmBuildSupportDump(taken, { includeData: false, injector }), injector);
}

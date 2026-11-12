// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { Component, inject, signal, TemplateRef, viewChild } from '@angular/core';

import { TmButton } from '@tellma/core-ui/button';
import { TmModal } from '@tellma/core-ui/modal';
import { TmToasts, type TmToast, type TmToastRef } from '@tellma/core-ui/toast';

/**
 * TmToasts: every tone, a detail and an action, a sticky error, a keyed
 * toast replaced in place, five at once (two behind the pill), focus() and
 * clear(), and a toast shown over an open modal. The log line records how
 * each toast closed.
 */
@Component({
  imports: [TmButton],
  template: `
    <h2>Toasts</h2>

    <!-- One button per kind of toast. -->
    <section class="row">
      <button tmButton type="button" data-testid="show-neutral" (click)="show({ tone: 'neutral', title: 'Draft saved' })">
        Neutral
      </button>
      <button
        tmButton
        type="button"
        data-testid="show-success"
        (click)="show({ tone: 'success', title: 'Invoice posted', detail: 'SAL-0042 is now final.' })"
      >
        Success with detail
      </button>
      <button tmButton type="button" data-testid="show-accent" (click)="showWithAction()">
        Accent with action
      </button>
      <button
        tmButton
        type="button"
        data-testid="show-warning"
        (click)="show({ tone: 'warning', title: 'Low stock', detail: 'Three items are below their reorder level.' })"
      >
        Warning
      </button>
      <button
        tmButton
        type="button"
        data-testid="show-error"
        (click)="show({ tone: 'error', title: 'Could not save', detail: 'The server did not answer.' })"
      >
        Error (sticky)
      </button>
      <button tmButton type="button" data-testid="show-keyed" (click)="showKeyed()">
        Keyed (replaces)
      </button>
      <button tmButton type="button" data-testid="show-five" (click)="showFive()">Five at once</button>
      <button tmButton type="button" data-testid="show-arabic" (click)="show({ tone: 'success', title: 'تم الحفظ', detail: 'أصبحت الفاتورة نهائية.' })">
        Arabic
      </button>
    </section>
    <section class="row">
      <button tmButton type="button" data-testid="focus-toasts" (click)="focused.set(toasts.focus())">
        Focus the toasts
      </button>
      <button tmButton type="button" data-testid="clear-toasts" (click)="toasts.clear()">Clear</button>
      <button tmButton type="button" data-testid="open-modal" (click)="openModal()">
        Open a modal, then toast
      </button>
    </section>
    <p class="log" data-testid="toast-log">{{ events().join(', ') }}</p>
    <p data-testid="toast-focused">focus(): {{ focused() }}</p>

    <ng-template #modalBody>
      <p data-testid="modal-body">A toast shows above this modal.</p>
      <button tmButton type="button" data-testid="modal-focus-toasts" (click)="focused.set(toasts.focus())">
        Focus the toasts
      </button>
    </ng-template>
  `,
  styles: `
    .row {
      display: flex;
      flex-wrap: wrap;
      align-items: center;
      gap: 12px;
      margin-block-end: 16px;
    }
    .log {
      min-block-size: 1.5em;
    }
  `,
})
export class ToastStory {
  protected readonly toasts = inject(TmToasts);
  private readonly modal = inject(TmModal);

  /** The modal's body. */
  private readonly modalBody = viewChild.required<TemplateRef<unknown>>('modalBody');

  /** How each toast closed, in order ("Draft saved: timeout"). */
  protected readonly events = signal<readonly string[]>([]);

  /** What the last focus() answered. */
  protected readonly focused = signal<boolean | null>(null);

  /** The keyed toast's step: each show replaces the last. */
  private keyedStep = 0;

  /** Shows a toast and logs how it closes. */
  protected show(toast: TmToast): TmToastRef {
    const ref = this.toasts.show(toast);
    void ref.closed.then((reason) => this.events.set([...this.events(), `${toast.title}: ${reason}`]));
    return ref;
  }

  /** An accent toast with a "Track" action. */
  protected showWithAction(): void {
    this.show({
      tone: 'accent',
      title: 'Started in the background.',
      action: { label: 'Track', run: () => this.events.set([...this.events(), 'Track ran']) },
    });
  }

  /** A keyed toast: a second click replaces the first in place. */
  protected showKeyed(): void {
    this.keyedStep++;
    this.show({ tone: 'neutral', title: `Upload step ${this.keyedStep}`, key: 'upload' });
  }

  /** Five toasts at once: three show, two hide behind the pill. */
  protected showFive(): void {
    for (const title of ['First', 'Second', 'Third', 'Fourth', 'Fifth']) {
      this.show({ tone: 'neutral', title });
    }
  }

  /** Opens a modal, then shows a toast that must sit above it. */
  protected openModal(): void {
    this.modal.open(this.modalBody(), { title: 'A modal' });
    this.show({ tone: 'success', title: 'Shown over the modal' });
  }
}

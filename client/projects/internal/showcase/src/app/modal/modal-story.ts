// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import {
  Component,
  inject,
  signal,
  TemplateRef,
  viewChild,
  type WritableSignal,
} from '@angular/core';

import { TmButton } from '@tellma/core-ui/button';
import {
  TM_MODAL_DATA,
  TmModal,
  TmModalFooter,
  TmModalRef,
  type TmModalResult,
} from '@tellma/core-ui/modal';
import { TmOption, TmSelect } from '@tellma/core-ui/select';

/** Basic content: displays the injected data, closes itself with results. */
@Component({
  selector: 'showcase-modal-basic-content',
  imports: [TmButton, TmModalFooter],
  template: `
    <p data-testid="modal-content">Post the document “{{ data }}”? Posting is irreversible.</p>
    <p>
      A modal body scrolls past its max height while the footer stays pinned; this one is short.
    </p>
    <div tmModalFooter>
      <button tmButton variant="ghost" data-testid="modal-cancel" (click)="ref.close()">
        Cancel
      </button>
      <button tmButton data-testid="modal-save" (click)="ref.close('posted')">Post</button>
    </div>
  `,
})
export class BasicModalContent {
  protected readonly data = inject(TM_MODAL_DATA);
  protected readonly ref = inject(TmModalRef) as TmModalRef<string>;
}

/**
 * Content with an editable field guarded by an unsaved-changes confirm.
 * The draft state lives with the OPENER (passed in as a writable signal
 * via data) so its canDismiss guard can read the dirty state — the
 * consumer-owns-state pattern.
 */
@Component({
  selector: 'showcase-modal-guarded-content',
  imports: [TmButton, TmModalFooter],
  template: `
    <label>
      Draft note:
      <input data-testid="guarded-input" [value]="note()" (input)="onInput($event)" />
    </label>
    <div tmModalFooter>
      <button tmButton data-testid="guarded-save" (click)="ref.close(note())">Save</button>
    </div>
  `,
})
export class GuardedModalContent {
  protected readonly ref = inject(TmModalRef) as TmModalRef<string>;
  protected readonly note = inject(TM_MODAL_DATA) as WritableSignal<string>;

  protected onInput(event: Event): void {
    this.note.set((event.target as HTMLInputElement).value);
  }
}

/** The confirm layer the guard opens (a stacked modal). */
@Component({
  selector: 'showcase-modal-confirm-content',
  imports: [TmButton, TmModalFooter],
  template: `
    <p data-testid="confirm-content">Discard the unsaved note?</p>
    <div tmModalFooter>
      <button tmButton variant="ghost" data-testid="confirm-keep" (click)="ref.close(false)">
        Keep editing
      </button>
      <button tmButton variant="danger" data-testid="confirm-discard" (click)="ref.close(true)">
        Discard
      </button>
    </div>
  `,
})
export class ConfirmModalContent {
  protected readonly ref = inject(TmModalRef) as TmModalRef<boolean>;
}

/** Content that opens another modal — the plain stacking demo. */
@Component({
  selector: 'showcase-modal-stacking-content',
  imports: [TmButton, TmModalFooter],
  template: `
    <p data-testid="stacking-content">This modal opens another on top of itself.</p>
    <div tmModalFooter>
      <button tmButton data-testid="open-inner" (click)="openInner()">Open inner modal</button>
    </div>
  `,
})
export class StackingModalContent {
  private readonly modal = inject(TmModal);

  protected openInner(): void {
    this.modal.open(ConfirmModalContent, { size: 'sm', title: 'Inner modal' });
  }
}

/** Content hosting a dropdown — pins the top-layer interplay. */
@Component({
  selector: 'showcase-modal-select-content',
  imports: [TmSelect, TmOption],
  template: `
    <label id="modal-status-label">Status</label>
    <tm-select aria-labelledby="modal-status-label" data-testid="modal-select">
      <tm-option value="draft">Draft</tm-option>
      <tm-option value="posted">Posted</tm-option>
      <tm-option value="void">Void</tm-option>
    </tm-select>
  `,
})
export class SelectModalContent {}

/**
 * tm-modal demo host — sizes, all four dismissal paths (surfaced in the
 * result readout), the canDismiss unsaved-changes guard, plain stacking,
 * template content, and a dropdown inside a modal. Drives the Playwright
 * battery.
 */
@Component({
  imports: [TmButton],
  template: `
    <h2>Modal</h2>

    <section>
      <h3>Open</h3>
      <div class="row">
        <button tmButton data-testid="open-basic" (click)="openBasic('md')">Open (md)</button>
        <button tmButton variant="secondary" data-testid="open-sm" (click)="openBasic('sm')">
          Open (sm)
        </button>
        <button tmButton variant="secondary" data-testid="open-lg" (click)="openBasic('lg')">
          Open (lg)
        </button>
        <button tmButton variant="secondary" data-testid="open-no-close" (click)="openNoClose()">
          No X, no backdrop
        </button>
      </div>
      <output data-testid="modal-result">{{ lastResult() }}</output>
    </section>

    <section>
      <h3>Guard, stacking, top layer, template</h3>
      <div class="row">
        <button tmButton variant="secondary" data-testid="open-guarded" (click)="openGuarded()">
          Guarded (unsaved changes)
        </button>
        <button tmButton variant="secondary" data-testid="open-stacked" (click)="openStacked()">
          Stacking
        </button>
        <button tmButton variant="secondary" data-testid="open-select" (click)="openSelect()">
          Dropdown inside
        </button>
        <button tmButton variant="secondary" data-testid="open-template" (click)="openTemplate()">
          Template content
        </button>
      </div>
    </section>

    <ng-template #templateContent let-ref let-data="data">
      <p data-testid="template-content">Template body for {{ data }}.</p>
      <button tmButton data-testid="template-done" (click)="ref.close('from-template')">
        Done
      </button>
    </ng-template>
  `,
  styles: `
    section {
      margin-block-end: 24px;
    }
    .row {
      display: flex;
      flex-wrap: wrap;
      gap: 8px;
      margin-block-end: 12px;
    }
  `,
})
export class ModalStory {
  private readonly modal = inject(TmModal);
  private readonly templateContent =
    viewChild.required<TemplateRef<unknown>>('templateContent');

  readonly lastResult = signal('none');

  private report(result: TmModalResult<unknown>): void {
    this.lastResult.set(JSON.stringify(result));
  }

  protected openBasic(size: 'sm' | 'md' | 'lg'): void {
    const ref = this.modal.open<string>(BasicModalContent, {
      size,
      title: 'Post document',
      data: 'INV-00042',
    });
    void ref.closed.then((result) => this.report(result));
  }

  protected openNoClose(): void {
    const ref = this.modal.open<string>(BasicModalContent, {
      title: 'Explicit choice',
      data: 'INV-00042',
      showClose: false,
      backdropDismiss: false,
    });
    void ref.closed.then((result) => this.report(result));
  }

  protected openGuarded(): void {
    const note = signal('');
    const ref = this.modal.open<string>(GuardedModalContent, {
      title: 'Edit note',
      data: note,
      canDismiss: () => {
        if (note() === '') {
          return true;
        }
        // The unsaved-changes pattern: confirm via a stacked modal.
        return this.modal
          .open<boolean>(ConfirmModalContent, { size: 'sm', title: 'Unsaved changes' })
          .closed.then((result) => result.via === 'api' && result.value === true);
      },
    });
    void ref.closed.then((result) => this.report(result));
  }

  protected openStacked(): void {
    const ref = this.modal.open(StackingModalContent, { title: 'Outer modal' });
    void ref.closed.then((result) => this.report(result));
  }

  protected openSelect(): void {
    const ref = this.modal.open(SelectModalContent, { title: 'Dropdown inside a modal' });
    void ref.closed.then((result) => this.report(result));
  }

  protected openTemplate(): void {
    const ref = this.modal.open<string>(this.templateContent(), {
      title: 'Template modal',
      data: 'INV-00042',
    });
    void ref.closed.then((result) => this.report(result));
  }
}

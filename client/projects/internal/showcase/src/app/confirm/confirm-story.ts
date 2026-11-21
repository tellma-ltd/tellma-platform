// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { Component, inject, signal } from '@angular/core';

import { TmButton } from '@tellma/core-ui/button';
import { TmConfirm } from '@tellma/core-ui/confirm';

/**
 * TmConfirm: the leave guard's three-way question, a destructive delete
 * (focus on Cancel, the warning glyph), and a plain yes-or-no with the
 * default labels. The last answer shows under the buttons.
 */
@Component({
  imports: [TmButton],
  template: `
    <h2>Confirm</h2>

    <section class="row">
      <button tmButton type="button" data-testid="open-leave" (click)="leave()">
        Leave the page
      </button>
      <button tmButton variant="danger" type="button" data-testid="open-delete" (click)="remove()">
        Delete the center
      </button>
      <button tmButton type="button" data-testid="open-post" (click)="post()">
        Post the document
      </button>
    </section>
    <p data-testid="confirm-answer">Answer: {{ answer() }}</p>
  `,
  styles: `
    .row {
      display: flex;
      flex-wrap: wrap;
      gap: 12px;
    }
  `,
})
export class ConfirmStory {
  private readonly confirm = inject(TmConfirm);

  /** The last answer, as text. */
  protected readonly answer = signal('none');

  /** The leave guard: discard, stay, or a dismissal. */
  protected async leave(): Promise<void> {
    const choice = await this.confirm.ask({
      title: 'Leave without saving?',
      message: 'This center has unsaved changes.',
      choices: [
        { key: 'discard', label: 'Discard changes', variant: 'ghost' },
        { key: 'stay', label: 'Stay', variant: 'primary' },
      ],
    });
    this.answer.set(String(choice));
  }

  /** A destructive confirm. */
  protected async remove(): Promise<void> {
    const ok = await this.confirm.confirm({
      title: 'Delete this center?',
      message: 'Jeddah sales and its 14 sub-centers will be deleted.\nThis cannot be undone.',
      confirmLabel: 'Delete',
      destructive: true,
    });
    this.answer.set(String(ok));
  }

  /** A plain confirm with the default labels. */
  protected async post(): Promise<void> {
    const ok = await this.confirm.confirm({
      title: 'Post the document?',
      message: 'A posted document can only be reversed, not edited.',
    });
    this.answer.set(String(ok));
  }
}

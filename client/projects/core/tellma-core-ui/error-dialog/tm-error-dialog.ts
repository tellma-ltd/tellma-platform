// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { inject, Injectable, signal, type WritableSignal } from '@angular/core';

import { TmModal, type TmModalRef } from '@tellma/core-ui/modal';

import {
  type ɵTmErrorDialogData,
  type ɵTmErrorDialogOutcome,
  type ɵTmErrorDialogState,
  ɵTmErrorDialogContent,
} from './internal/tm-error-dialog-content';

/** A button inside an error dialog's Details disclosure. */
export interface TmErrorDialogAction {
  /** The button's text. */
  readonly label: string;
  /** The name of a registered icon shown before the text. */
  readonly icon?: string;
  /** What the button does. The dialog stays open unless `closes` is set. */
  readonly run: () => void;
  /** Whether the dialog closes (resolving `'close'`) after `run`. Default `false`. */
  readonly closes?: boolean;
}

/** A footer button the caller adds to an error dialog. */
export interface TmErrorDialogButton {
  /** The button's text. */
  readonly label: string;
  /** The button's accessible name while `run` is pending. Default: `label`. */
  readonly pendingLabel?: string;
  /**
   * What the button does before the dialog closes. While the promise is
   * pending the button shows a spinner; `true` closes the dialog and
   * resolves the button's key, `false` keeps it open. Without `run`, a
   * click closes the dialog at once.
   */
  readonly run?: () => Promise<boolean>;
}

/** The failure {@link TmErrorDialog.show} reports. */
export interface TmErrorDialogOptions {
  /** The dialog's title, which is also its accessible name. */
  readonly title: string;
  /** What went wrong, which is also the dialog's description. */
  readonly message: string;
  /** Things the failure concerns, listed under the message: the first 10, then "and N more". */
  readonly items?: readonly string[];
  /** A trace id, shown in a reference row the user can copy. */
  readonly reference?: string;
  /** Labelled facts, listed inside the Details disclosure. The caller localizes the labels. */
  readonly details?: readonly { readonly label: string; readonly value: string }[];
  /** Buttons inside the Details disclosure, after the details. */
  readonly actions?: readonly TmErrorDialogAction[];
  /** The first footer button, styled primary; Close then becomes secondary. */
  readonly primary?: TmErrorDialogButton;
  /** A footer button after `primary`. */
  readonly secondary?: TmErrorDialogButton;
}

/** How an error dialog's {@link TmErrorDialog.show} promise settles. */
export type TmErrorDialogResult = 'primary' | 'secondary' | 'close' | 'replaced';

/** The dialog on screen, and the promise of the latest `show` it answers. */
interface ActiveDialog {
  /** The open modal. */
  readonly ref: TmModalRef<ɵTmErrorDialogOutcome>;
  /** What the dialog shows; replaced in place by a later `show`. */
  readonly state: WritableSignal<ɵTmErrorDialogState>;
  /** Settles the latest `show`'s promise. */
  resolve: (result: TmErrorDialogResult) => void;
}

let nextMessageId = 0;

/**
 * Shows a failure the user must acknowledge, in an alert dialog: the
 * message, any affected items, a reference to quote to support, and a
 * Details disclosure, with a Close button and up to two caller buttons in
 * the footer. Focus starts on the first footer button.
 *
 * One error dialog shows at a time: a `show` while one is open replaces
 * its content in place, keeping focus on the same footer position, and
 * the earlier promise resolves `'replaced'`.
 *
 * ```ts
 * const answer = await errorDialog.show({
 *   title: 'Something went wrong',
 *   message: 'The server could not complete the request.',
 *   reference: traceId,
 *   primary: { label: 'Reload', pendingLabel: 'Reloading', run: () => app.reload() },
 *   details: [{ label: 'Status', value: '500' }],
 * }); // 'primary', 'close' or 'replaced'
 * ```
 */
@Injectable({ providedIn: 'root' })
export class TmErrorDialog {
  private readonly modal = inject(TmModal);

  /** The dialog on screen, if any. */
  private active: ActiveDialog | null = null;

  /**
   * Shows the failure, or replaces the one on screen. Resolves to the
   * footer button that closed the dialog (`'primary'`, `'secondary'`,
   * or `'close'` for Close, Escape, the close button and a closing
   * action), or `'replaced'` when a later `show` took the dialog over.
   */
  show(options: TmErrorDialogOptions): Promise<TmErrorDialogResult> {
    return new Promise((resolve) => {
      const active = this.active;
      if (active !== null) {
        // Replace in place: the earlier caller learns it was superseded,
        // and a run still pending from it is ignored (the generation moves).
        active.resolve('replaced');
        active.resolve = resolve;
        active.ref.ɵsetTitle(options.title);
        active.state.update((state) => ({ options, generation: state.generation + 1 }));
        return;
      }
      const messageId = `tm-error-dialog-message-${nextMessageId++}`;
      const state = signal<ɵTmErrorDialogState>({ options, generation: 0 });
      const data: ɵTmErrorDialogData = {
        state: state.asReadonly(),
        messageId,
        // A footer button closes: the dialog stops taking replacements at
        // once, so a show in the same frame opens a new dialog.
        close: (outcome) => {
          if (this.active === entry) {
            this.active = null;
          }
          ref.close(outcome);
        },
      };
      const ref = this.modal.open<ɵTmErrorDialogOutcome>(ɵTmErrorDialogContent, {
        size: 'sm',
        role: 'alertdialog',
        describedBy: messageId,
        title: options.title,
        // Acknowledged on purpose: Escape and the close button answer,
        // a stray click outside does not.
        backdropDismiss: false,
        data,
      });
      const entry: ActiveDialog = { ref, state, resolve };
      this.active = entry;
      void ref.closed.then((result) => {
        if (this.active === entry) {
          this.active = null;
        }
        entry.resolve(result.via === 'api' ? (result.value ?? 'close') : 'close');
      });
    });
  }
}

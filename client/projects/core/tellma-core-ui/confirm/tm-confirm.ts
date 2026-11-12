// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { inject, Injectable, isDevMode } from '@angular/core';

import { TM_UI_TRANSLATE } from '@tellma/core-ui';
import { TmModal } from '@tellma/core-ui/modal';

import { type ɵTmConfirmData, ɵTmConfirmContent } from './internal/tm-confirm-content';

/** The look of a confirm answer's button, as the `tmButton` variants. */
export type TmConfirmVariant = 'primary' | 'secondary' | 'ghost' | 'danger';

/** The glyph in the tinted disc beside a confirm's message. */
export type TmConfirmIcon = 'warning' | 'error' | 'info';

/** One answer to a {@link TmConfirm.ask} question: a footer button that resolves to its key. */
export interface TmConfirmChoice<K extends string> {
  /** What `ask` resolves to when this button is clicked. */
  readonly key: K;
  /** The button's text. */
  readonly label: string;
  /** The button's look. */
  readonly variant: TmConfirmVariant;
}

/** The question {@link TmConfirm.ask} puts to the user. */
export interface TmConfirmOptions<K extends string> {
  /** The dialog's title, which is also its accessible name. */
  readonly title: string;
  /** The question, which is also the dialog's description. */
  readonly message: string;
  /**
   * The glyph in a 26 px tinted disc beside the message, or `null` for
   * none. Default: `warning` when a choice is `danger`, else none.
   */
  readonly icon?: TmConfirmIcon | null;
  /** The answers, as footer buttons in this order, aligned to the inline end. */
  readonly choices: readonly TmConfirmChoice<K>[];
  /**
   * The answer focused on open. Default: the `primary` choice, else the
   * first choice that is not `danger`.
   */
  readonly initialFocus?: K;
}

/** The yes-or-no question {@link TmConfirm.confirm} puts to the user. */
export interface TmConfirmYesNoOptions {
  /** The dialog's title, which is also its accessible name. */
  readonly title: string;
  /** The question, which is also the dialog's description. */
  readonly message: string;
  /** The confirm button's text. Default "OK". */
  readonly confirmLabel?: string;
  /** The cancel button's text. Default "Cancel". */
  readonly cancelLabel?: string;
  /**
   * Whether confirming destroys something: the confirm button turns
   * `danger`, a warning glyph shows, and focus starts on Cancel so a
   * stray Enter does not confirm.
   */
  readonly destructive?: boolean;
}

let nextMessageId = 0;

/**
 * Asks the user a question in an alert dialog and resolves to the answer.
 * `ask` takes two or more answers and resolves to the chosen key;
 * `confirm` is the common yes-or-no case and resolves to a boolean.
 * Escape, the close button and the backdrop all answer `null` (`false`
 * from `confirm`).
 *
 * ```ts
 * const choice = await confirm.ask({
 *   title: 'Leave without saving?',
 *   message: 'This center has unsaved changes.',
 *   choices: [
 *     { key: 'discard', label: 'Discard changes', variant: 'ghost' },
 *     { key: 'stay', label: 'Stay', variant: 'primary' },
 *   ],
 * }); // 'discard', 'stay' or null
 * const ok = await confirm.confirm({ title: 'Delete this center?', message, confirmLabel: 'Delete', destructive: true });
 * ```
 */
@Injectable({ providedIn: 'root' })
export class TmConfirm {
  private readonly modal = inject(TmModal);
  private readonly translate = inject(TM_UI_TRANSLATE);

  /**
   * Opens the question as a small alert dialog: the title, the message
   * (the dialog's description) beside an optional glyph, and one footer
   * button per choice. Resolves to the clicked choice's key, or `null`
   * when the user dismissed the dialog with Escape, the close button or
   * the backdrop.
   */
  async ask<K extends string>(options: TmConfirmOptions<K>): Promise<K | null> {
    if (isDevMode() && options.choices.length === 0) {
      console.error(
        '[tellma-ui] TmConfirm.ask called without choices: the only answer is to dismiss.',
      );
    }
    const messageId = `tm-confirm-message-${nextMessageId++}`;
    const data: ɵTmConfirmData = {
      message: options.message,
      messageId,
      icon: options.icon === undefined ? defaultIcon(options.choices) : options.icon,
      choices: options.choices,
      focusIndex: focusIndex(options.choices, options.initialFocus),
    };
    const ref = this.modal.open<K>(ɵTmConfirmContent, {
      size: 'sm',
      role: 'alertdialog',
      describedBy: messageId,
      title: options.title,
      data,
    });
    const result = await ref.closed;
    return result.via === 'api' ? (result.value ?? null) : null;
  }

  /**
   * Asks a yes-or-no question with a Cancel and a confirm button (in that
   * order). Resolves `true` when the user confirmed, and `false` when they
   * cancelled or dismissed the dialog.
   */
  async confirm(options: TmConfirmYesNoOptions): Promise<boolean> {
    const destructive = options.destructive ?? false;
    const answer = await this.ask<'cancel' | 'confirm'>({
      title: options.title,
      message: options.message,
      icon: destructive ? 'warning' : null,
      choices: [
        {
          key: 'cancel',
          label: options.cancelLabel ?? this.translate('confirm.cancel')(),
          variant: 'secondary',
        },
        {
          key: 'confirm',
          label: options.confirmLabel ?? this.translate('confirm.ok')(),
          variant: destructive ? 'danger' : 'primary',
        },
      ],
      initialFocus: destructive ? 'cancel' : 'confirm',
    });
    return answer === 'confirm';
  }
}

/** A question with a destructive answer carries the warning glyph unless told otherwise. */
function defaultIcon(choices: readonly TmConfirmChoice<string>[]): TmConfirmIcon | null {
  return choices.some((choice) => choice.variant === 'danger') ? 'warning' : null;
}

/**
 * The index of the choice focused on open: the requested one, else the
 * primary one, else the first that is not destructive, else the first.
 */
function focusIndex(
  choices: readonly TmConfirmChoice<string>[],
  requested: string | undefined,
): number {
  const candidates = [
    requested === undefined ? -1 : choices.findIndex((choice) => choice.key === requested),
    choices.findIndex((choice) => choice.variant === 'primary'),
    choices.findIndex((choice) => choice.variant !== 'danger'),
  ];
  return candidates.find((index) => index >= 0) ?? 0;
}

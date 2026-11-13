// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { ComponentHarness, TestKey } from '@angular/cdk/testing';

/** A footer button of an error dialog, as the harness reports it. */
export interface TmErrorDialogButtonState {
  /** Which button: `primary`, `secondary` or `close`. */
  readonly key: string;
  /** The visible text. */
  readonly label: string;
  /** Whether its run is pending. */
  readonly pending: boolean;
  /** Whether it is disabled while the other caller button's run is pending. */
  readonly disabled: boolean;
  /** The accessible name set while pending, or `null` when the text names it. */
  readonly ariaLabel: string | null;
}

/**
 * Harness for an open `TmErrorDialog` dialog.
 *
 * The dialog renders in the CDK overlay container at the document root,
 * outside any fixture — locate it with a document-root loader
 * (`TestbedHarnessEnvironment.documentRootLoader(fixture)`).
 */
export class TmErrorDialogHarness extends ComponentHarness {
  /** The selector for the modal shell that holds an error dialog. */
  static hostSelector = 'tm-modal-shell:has(tm-error-dialog-content)';

  /** The title. */
  async getTitle(): Promise<string> {
    return (await (await this.locatorFor('.tm-modal__title')()).text()).trim();
  }

  /** The message. */
  async getMessage(): Promise<string> {
    return (await (await this.locatorFor('.tm-error-dialog__message')()).text()).trim();
  }

  /** The listed items, at most ten. */
  async getItems(): Promise<string[]> {
    return this.textsOf('.tm-error-dialog__items > li');
  }

  /** The count of the items past the list ("and 5 more"), or `null` when all are listed. */
  async getMoreText(): Promise<string | null> {
    const more = await this.locatorForOptional('.tm-error-dialog__more')();
    return more === null ? null : (await more.text()).trim();
  }

  /** Whether a reference row shows. */
  async hasReference(): Promise<boolean> {
    return (await this.locatorForOptional('tm-reference')()) !== null;
  }

  /** Whether the dialog has a Details disclosure. */
  async hasDetails(): Promise<boolean> {
    return (await this.locatorForOptional('.tm-error-dialog__toggle')()) !== null;
  }

  /** Whether the Details disclosure is open. */
  async isDetailsOpen(): Promise<boolean> {
    const toggle = await this.locatorFor('.tm-error-dialog__toggle')();
    return (await toggle.getAttribute('aria-expanded')) === 'true';
  }

  /** Opens or closes the Details disclosure. */
  async toggleDetails(): Promise<void> {
    return (await this.locatorFor('.tm-error-dialog__toggle')()).click();
  }

  /** The details as label and value pairs (the disclosure must be open). */
  async getDetails(): Promise<{ label: string; value: string }[]> {
    const [labels, values] = await Promise.all([
      this.textsOf('.tm-error-dialog__region dt'),
      this.textsOf('.tm-error-dialog__region dd'),
    ]);
    return labels.map((label, index) => ({ label, value: values[index] ?? '' }));
  }

  /** The Details actions' labels (the disclosure must be open). */
  async getActionLabels(): Promise<string[]> {
    return this.textsOf('.tm-error-dialog__actions > button');
  }

  /** Clicks the Details action with this label (the disclosure must be open). */
  async runAction(label: string): Promise<void> {
    for (const action of await this.locatorForAll('.tm-error-dialog__actions > button')()) {
      if ((await action.text()).trim() === label) {
        return action.click();
      }
    }
    throw new Error(`TmErrorDialogHarness: no action labelled "${label}".`);
  }

  /** The footer buttons, in order. */
  async getButtons(): Promise<TmErrorDialogButtonState[]> {
    const [buttons, labels] = await Promise.all([
      this.locatorForAll('.tm-error-dialog__button')(),
      this.textsOf('.tm-error-dialog__button .tm-error-dialog__label > :first-child'),
    ]);
    return Promise.all(
      buttons.map(async (button, index) => ({
        key: (await button.getAttribute('data-key')) ?? '',
        label: labels[index] ?? '',
        pending: (await button.getAttribute('aria-busy')) === 'true',
        disabled: await button.getProperty<boolean>('disabled'),
        ariaLabel: await button.getAttribute('aria-label'),
      })),
    );
  }

  /** The key of the focused footer button, or `null` when focus is elsewhere. */
  async getFocusedButtonKey(): Promise<string | null> {
    for (const button of await this.locatorForAll('.tm-error-dialog__button')()) {
      if (await button.isFocused()) {
        return button.getAttribute('data-key');
      }
    }
    return null;
  }

  /** Clicks a footer button: `primary`, `secondary` or `close`. */
  async clickButton(key: 'primary' | 'secondary' | 'close'): Promise<void> {
    return (await this.locatorFor(`.tm-error-dialog__button[data-key="${key}"]`)()).click();
  }

  /** Presses Escape in the dialog (it resolves `'close'`). */
  async pressEscape(): Promise<void> {
    return (await this.host()).sendKeys(TestKey.ESCAPE);
  }

  /** The trimmed texts of every element matching `selector`, in order. */
  private async textsOf(selector: string): Promise<string[]> {
    const elements = await this.locatorForAll(selector)();
    return Promise.all(elements.map(async (element) => (await element.text()).trim()));
  }
}

// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { ComponentHarness, TestKey } from '@angular/cdk/testing';

/**
 * Harness for an open `TmConfirm` dialog.
 *
 * The dialog renders in the CDK overlay container at the document root,
 * outside any fixture — locate it with a document-root loader
 * (`TestbedHarnessEnvironment.documentRootLoader(fixture)`).
 */
export class TmConfirmHarness extends ComponentHarness {
  /** The selector for the modal shell that holds a confirm. */
  static hostSelector = 'tm-modal-shell:has(tm-confirm-content)';

  /** The title. */
  async getTitle(): Promise<string> {
    return (await (await this.locatorFor('.tm-modal__title')()).text()).trim();
  }

  /** The message. */
  async getMessage(): Promise<string> {
    return (await (await this.locatorFor('.tm-confirm__message')()).text()).trim();
  }

  /** The glyph beside the message (`warning`, `error`, `info`), or `null` for none. */
  async getIcon(): Promise<string | null> {
    const icon = await this.locatorForOptional('.tm-confirm__icon')();
    return icon === null ? null : icon.getAttribute('data-tone');
  }

  /** The answers' labels, in footer order. */
  async getChoiceLabels(): Promise<string[]> {
    const choices = await this.locatorForAll('.tm-confirm__choice')();
    return Promise.all(choices.map(async (choice) => (await choice.text()).trim()));
  }

  /** The label of the focused answer, or `null` when focus is elsewhere. */
  async getFocusedChoiceLabel(): Promise<string | null> {
    for (const choice of await this.locatorForAll('.tm-confirm__choice')()) {
      if (await choice.isFocused()) {
        return (await choice.text()).trim();
      }
    }
    return null;
  }

  /** Clicks the answer with this label. */
  async choose(label: string): Promise<void> {
    for (const choice of await this.locatorForAll('.tm-confirm__choice')()) {
      if ((await choice.text()).trim() === label) {
        return choice.click();
      }
    }
    throw new Error(`TmConfirmHarness: no answer labelled "${label}".`);
  }

  /** Clicks the close button (the question resolves `null`). */
  async close(): Promise<void> {
    return (await this.locatorFor('.tm-modal__close')()).click();
  }

  /** Presses Escape in the dialog (the question resolves `null`). */
  async pressEscape(): Promise<void> {
    return (await this.host()).sendKeys(TestKey.ESCAPE);
  }
}

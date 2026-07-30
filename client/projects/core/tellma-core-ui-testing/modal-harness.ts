// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { ComponentHarness } from '@angular/cdk/testing';

/**
 * Harness for an open Tellma modal (the shell `TmModal` wraps content in).
 *
 * Modals render in the CDK overlay container at the document root, outside
 * any fixture — locate them with a document-root loader
 * (`TestbedHarnessEnvironment.documentRootLoader(fixture)`).
 */
export class TmModalHarness extends ComponentHarness {
  /** The selector for the modal shell element. */
  static hostSelector = 'tm-modal-shell';

  /** Gets the header title, or null when the modal renders no title. */
  async getTitle(): Promise<string | null> {
    const title = await this.locatorForOptional('.tm-modal__title')();
    return title === null ? null : (await title.text()).trim();
  }

  /** The body's text content, trimmed. */
  async getBodyText(): Promise<string> {
    return (await (await this.locatorFor('.tm-modal__body')()).text()).trim();
  }

  /** Whether the header shows the X close button. */
  async hasCloseButton(): Promise<boolean> {
    return (await this.locatorForOptional('.tm-modal__close')()) !== null;
  }

  /**
   * Clicks the X close button (a user dismissal: it consults `canDismiss`
   * and resolves `closed` with `{ via: 'close-button' }`).
   */
  async close(): Promise<void> {
    return (await this.locatorFor('.tm-modal__close')()).click();
  }
}

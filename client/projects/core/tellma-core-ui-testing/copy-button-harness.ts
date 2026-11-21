// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { ComponentHarness } from '@angular/cdk/testing';

/** Harness for `tm-copy-button`. */
export class TmCopyButtonHarness extends ComponentHarness {
  /** The selector for the `tm-copy-button` host element. */
  static hostSelector = 'tm-copy-button';

  /** The button inside the host. */
  private readonly button = this.locatorFor('.tm-copy-button__button');

  /** The manual-copy input, while the fallback is up. */
  private readonly manual = this.locatorForOptional('.tm-copy-button__manual');

  /** Clicks the button, which copies. */
  async click(): Promise<void> {
    return (await this.button()).click();
  }

  /** The button's accessible name: its `aria-label`, or its visible label. */
  async getLabel(): Promise<string> {
    const button = await this.button();
    const label = await button.getAttribute('aria-label');
    if (label !== null) {
      return label;
    }
    const shown = await this.locatorFor('.tm-copy-button__label:not(.tm-copy-button__label--off)')();
    return (await shown.text()).trim();
  }

  /** Whether the button is showing "Copied" after a copy. */
  async isCopied(): Promise<boolean> {
    return (await this.host()).hasClass('tm-copy-button--copied');
  }

  /** The polite announcement in force ("Copied"), or `''`. */
  async getAnnouncement(): Promise<string> {
    return (await (await this.locatorFor('[role="status"]')()).text()).trim();
  }

  /** Whether the manual fallback ("Press Ctrl+C to copy") is up. */
  async isShowingManualCopy(): Promise<boolean> {
    return (await this.manual()) !== null;
  }

  /** The text the manual fallback offers, or `null` while it is down. */
  async getManualText(): Promise<string | null> {
    const input = await this.manual();
    return input === null ? null : input.getProperty<string>('value');
  }

  /** The manual fallback's prompt ("Press Ctrl+C to copy"), or `null` while it is down. */
  async getManualPrompt(): Promise<string | null> {
    const input = await this.manual();
    return input === null ? null : input.getAttribute('aria-label');
  }
}

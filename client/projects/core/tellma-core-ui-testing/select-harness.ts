// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { ComponentHarness, TestKey, type HarnessPredicate } from '@angular/cdk/testing';

/** Harness for a single `tm-option`. */
export class TmOptionHarness extends ComponentHarness {
  /** The selector for an option row rendered in the open panel. */
  static hostSelector = '.tm-option__row';

  /** Gets the option's rendered display text, trimmed. */
  async getText(): Promise<string> {
    return (await (await this.locatorFor('.tm-option__content')()).text()).trim();
  }

  /** Whether the option is the selected one (aria-selected). */
  async isSelected(): Promise<boolean> {
    return (await (await this.host()).getAttribute('aria-selected')) === 'true';
  }

  /** Whether the option is the active (keyboard-highlighted) one. */
  async isActive(): Promise<boolean> {
    return (await (await this.host()).getAttribute('data-active')) === 'true';
  }

  /** Whether the option is disabled. */
  async isDisabled(): Promise<boolean> {
    return (await (await this.host()).getAttribute('aria-disabled')) === 'true';
  }

  /** Clicks the option row. */
  async click(): Promise<void> {
    return (await this.host()).click();
  }
}

/**
 * Collection harness for `tm-select`: open the panel, list and
 * select options, read the trigger. The panel renders lazily on open; the
 * option locators only resolve while it is open.
 */
export class TmSelectHarness extends ComponentHarness {
  /** The selector for the `tm-select` host element. */
  static hostSelector = 'tm-select';

  private readonly trigger = this.locatorFor('.tm-select__trigger');

  /** Whether the options panel is open. */
  async isOpen(): Promise<boolean> {
    return (await (await this.trigger()).getAttribute('aria-expanded')) === 'true';
  }

  /** Opens the options panel by clicking the trigger (no-op when already open). */
  async open(): Promise<void> {
    if (!(await this.isOpen())) {
      const trigger = await this.trigger();
      // A real click focuses the trigger; synthetic clicks don't — and the
      // aria combobox collapses when neither trigger nor popup is focused.
      await trigger.focus();
      await trigger.click();
    }
  }

  /** Closes the options panel via Escape (no-op when already closed). */
  async close(): Promise<void> {
    if (await this.isOpen()) {
      await this.sendTriggerKeys(TestKey.ESCAPE);
    }
  }

  /** Gets the text shown in the trigger (the selected label or the placeholder). */
  async getTriggerText(): Promise<string> {
    return (await (await this.locatorFor('.tm-select__value')()).text()).trim();
  }

  /** Whether the trigger shows the placeholder rather than a selected value. */
  async isPlaceholderShown(): Promise<boolean> {
    const value = await this.locatorFor('.tm-select__value')();
    return (await value.getAttribute('class'))!.includes('tm-select__value--placeholder');
  }

  /** Whether the select is disabled. */
  async isDisabled(): Promise<boolean> {
    return (await (await this.trigger()).getAttribute('aria-disabled')) === 'true';
  }

  /** Whether the select announces pending async validation (aria-busy). */
  async isBusy(): Promise<boolean> {
    return (await (await this.trigger()).getAttribute('aria-busy')) === 'true';
  }

  /** Opens the panel and gets harnesses for the rendered options, optionally filtered. */
  async getOptions(filter?: HarnessPredicate<TmOptionHarness>): Promise<TmOptionHarness[]> {
    await this.open();
    return this.documentRootLocatorFactory().locatorForAll(filter ?? TmOptionHarness)();
  }

  /** Opens the panel and clicks the option with the given visible text. */
  async selectOption(text: string): Promise<void> {
    await this.open();
    const options = await this.getOptions();
    for (const option of options) {
      if ((await option.getText()) === text) {
        await option.click();
        return;
      }
    }
    throw new Error(`tm-select has no option with text "${text}"`);
  }

  /** Sends keys to the trigger (arrows/Enter/Escape/typeahead characters). */
  async sendTriggerKeys(...keys: (string | TestKey)[]): Promise<void> {
    const trigger = await this.trigger();
    await trigger.focus();
    await trigger.sendKeys(...keys);
  }
}

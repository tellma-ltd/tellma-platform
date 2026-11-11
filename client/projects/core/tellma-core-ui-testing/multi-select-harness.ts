// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { ComponentHarness, TestKey, type HarnessPredicate } from '@angular/cdk/testing';

import { TmOptionGroupHarness, TmOptionHarness } from './select-harness';

/**
 * Harness for `tm-multi-select`: open the panel, toggle options by their
 * visible text, and read the trigger's joined value. The panel renders
 * lazily on open, and stays open across toggles; the option locators only
 * resolve while it is open.
 */
export class TmMultiSelectHarness extends ComponentHarness {
  /** The selector for the `tm-multi-select` host element. */
  static hostSelector = 'tm-multi-select';

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

  /**
   * Gets the trigger's text: the selected labels joined as a list ("Service,
   * Operation"), or the placeholder when nothing is selected.
   */
  async getValueText(): Promise<string> {
    return (await (await this.locatorFor('.tm-select__value')()).text()).trim();
  }

  /** Whether the trigger shows the placeholder rather than any selected label. */
  async isPlaceholderShown(): Promise<boolean> {
    const value = await this.locatorFor('.tm-select__value')();
    return (await value.getAttribute('class'))!.includes('tm-select__value--placeholder');
  }

  /** Whether the multi-select is disabled. */
  async isDisabled(): Promise<boolean> {
    return (await (await this.trigger()).getAttribute('aria-disabled')) === 'true';
  }

  /** Opens the panel and gets harnesses for the rendered options, optionally filtered. */
  async getOptions(filter?: HarnessPredicate<TmOptionHarness>): Promise<TmOptionHarness[]> {
    await this.open();
    return this.documentRootLocatorFactory().locatorForAll(filter ?? TmOptionHarness)();
  }

  /** Opens the panel and gets harnesses for its option groups, in order (none for a flat list). */
  async getGroups(): Promise<TmOptionGroupHarness[]> {
    await this.open();
    return this.documentRootLocatorFactory().locatorForAll(TmOptionGroupHarness)();
  }

  /**
   * Opens the panel and toggles the option with the given visible text, as
   * a click would. The panel stays open.
   *
   * @throws Error when no option has that text.
   */
  async toggle(label: string): Promise<void> {
    return (await this.optionWithText(label)).click();
  }

  /**
   * Opens the panel and reports whether the option with the given visible
   * text is selected.
   *
   * @throws Error when no option has that text.
   */
  async isOptionSelected(label: string): Promise<boolean> {
    return (await this.optionWithText(label)).isSelected();
  }

  /** Sends keys to the trigger (arrows/Enter/Space/Escape/typeahead characters). */
  async sendTriggerKeys(...keys: (string | TestKey)[]): Promise<void> {
    const trigger = await this.trigger();
    await trigger.focus();
    await trigger.sendKeys(...keys);
  }

  /** The open panel's option with the given visible text. */
  private async optionWithText(label: string): Promise<TmOptionHarness> {
    for (const option of await this.getOptions()) {
      if ((await option.getText()) === label) {
        return option;
      }
    }
    throw new Error(`tm-multi-select has no option with text "${label}"`);
  }
}

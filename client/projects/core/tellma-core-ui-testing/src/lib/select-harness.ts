import { ComponentHarness, TestKey, type HarnessPredicate } from '@angular/cdk/testing';

/** Harness for a single `tm-option` (spec §10). */
export class TmOptionHarness extends ComponentHarness {
  static hostSelector = '.tm-option__row';


  async getText(): Promise<string> {
    return (await (await this.locatorFor('.tm-option__content')()).text()).trim();
  }

  async isSelected(): Promise<boolean> {
    return (await (await this.host()).getAttribute('aria-selected')) === 'true';
  }

  async isActive(): Promise<boolean> {
    return (await (await this.host()).getAttribute('data-active')) === 'true';
  }

  async isDisabled(): Promise<boolean> {
    return (await (await this.host()).getAttribute('aria-disabled')) === 'true';
  }

  async click(): Promise<void> {
    return (await this.host()).click();
  }
}

/**
 * Collection harness for `tm-select` (spec §10): open the panel, list and
 * select options, read the trigger. The panel renders lazily on open; the
 * option locators only resolve while it is open.
 */
export class TmSelectHarness extends ComponentHarness {
  static hostSelector = 'tm-select';

  private readonly trigger = this.locatorFor('.tm-select__trigger');

  async isOpen(): Promise<boolean> {
    return (await (await this.trigger()).getAttribute('aria-expanded')) === 'true';
  }

  async open(): Promise<void> {
    if (!(await this.isOpen())) {
      const trigger = await this.trigger();
      // A real click focuses the trigger; synthetic clicks don't — and the
      // aria combobox collapses when neither trigger nor popup is focused.
      await trigger.focus();
      await trigger.click();
    }
  }

  async close(): Promise<void> {
    if (await this.isOpen()) {
      await this.sendTriggerKeys(TestKey.ESCAPE);
    }
  }

  async getTriggerText(): Promise<string> {
    return (await (await this.locatorFor('.tm-select__value')()).text()).trim();
  }

  async isPlaceholderShown(): Promise<boolean> {
    const value = await this.locatorFor('.tm-select__value')();
    return (await value.getAttribute('class'))!.includes('tm-select__value--placeholder');
  }

  async isDisabled(): Promise<boolean> {
    return (await (await this.trigger()).getAttribute('aria-disabled')) === 'true';
  }

  async isBusy(): Promise<boolean> {
    return (await (await this.trigger()).getAttribute('aria-busy')) === 'true';
  }

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

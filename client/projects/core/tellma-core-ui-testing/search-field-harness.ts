// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { ComponentHarness, type TestKey } from '@angular/cdk/testing';

/** Harness for `tm-search-field`. */
export class TmSearchFieldHarness extends ComponentHarness {
  /** The selector for the `tm-search-field` host element. */
  static hostSelector = 'tm-search-field';

  private readonly input = this.locatorFor('input.tm-search');
  private readonly clearButton = this.locatorFor('.tm-search-field__clear');

  /** The input's text. */
  async getValue(): Promise<string> {
    return (await this.input()).getProperty<string>('value');
  }

  /** Types like a user: focus, clear the text, send the keys. */
  async setValue(text: string): Promise<void> {
    const input = await this.input();
    await input.clear();
    if (text !== '') {
      await input.sendKeys(text);
    }
  }

  /** The placeholder. */
  async getPlaceholder(): Promise<string> {
    return (await this.input()).getProperty<string>('placeholder');
  }

  /** The input's accessible name. */
  async getAriaLabel(): Promise<string | null> {
    return (await this.input()).getAttribute('aria-label');
  }

  /** Whether the clear button shows (it keeps its slot while hidden). */
  async isClearVisible(): Promise<boolean> {
    return !(await (await this.clearButton()).hasClass('tm-search-field__clear--hidden'));
  }

  /** Clicks the clear button. */
  async clear(): Promise<void> {
    return (await this.clearButton()).click();
  }

  /** Presses a key in the input (Enter searches at once; Escape clears). */
  async press(key: TestKey): Promise<void> {
    return (await this.input()).sendKeys(key);
  }

  /** Whether the input announces itself as invalid. */
  async isInvalid(): Promise<boolean> {
    return (await (await this.input()).getAttribute('aria-invalid')) === 'true';
  }

  /** Whether the input has focus. */
  async isFocused(): Promise<boolean> {
    return (await this.input()).isFocused();
  }

  /** Focuses the input. */
  async focus(): Promise<void> {
    return (await this.input()).focus();
  }
}

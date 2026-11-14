// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { ComponentHarness, type TestKey } from '@angular/cdk/testing';

/** Harness for `tm-time-field`. */
export class TmTimeFieldHarness extends ComponentHarness {
  /** The selector for the `tm-time-field` host element. */
  static hostSelector = 'tm-time-field';

  private readonly input = this.locatorFor('.tm-time-field__input');

  /** The displayed text (the locale-formatted time or the raw entry). */
  async getText(): Promise<string> {
    return (await this.input()).getProperty<string>('value');
  }

  /** Types like a user: focus, clear, send keys, leaving the input focused. */
  async setText(text: string): Promise<void> {
    const input = await this.input();
    await input.clear();
    if (text !== '') {
      await input.sendKeys(text);
    }
  }

  /** Presses a key in the input (Up and Down step the time; Enter reformats). */
  async press(key: TestKey): Promise<void> {
    return (await this.input()).sendKeys(key);
  }

  /** Blurs the input (reformats typed text). */
  async blur(): Promise<void> {
    return (await this.input()).blur();
  }

  /** Focuses the input. */
  async focus(): Promise<void> {
    return (await this.input()).focus();
  }

  /** The input's aria-describedby attribute (space-separated ids), or null when absent. */
  async getDescribedBy(): Promise<string | null> {
    return (await this.input()).getAttribute('aria-describedby');
  }

  /** Whether the input announces itself as invalid (aria-invalid). */
  async isInvalid(): Promise<boolean> {
    return (await (await this.input()).getAttribute('aria-invalid')) === 'true';
  }
}

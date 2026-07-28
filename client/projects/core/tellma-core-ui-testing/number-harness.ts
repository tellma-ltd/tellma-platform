// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { ComponentHarness } from '@angular/cdk/testing';

/** Harness for the `tmNumber` directive. */
export class TmNumberHarness extends ComponentHarness {
  /** The selector for the `input[tmNumber]` host element. */
  static hostSelector = 'input[tmNumber]';

  /** Gets the displayed text (the locale-formatted value or the raw entry). */
  async getText(): Promise<string> {
    return (await this.host()).getProperty<string>('value');
  }

  /** Types like a user: focus, clear, send keys, leaving the input focused. */
  async setText(text: string): Promise<void> {
    const host = await this.host();
    await host.clear();
    if (text !== '') {
      await host.sendKeys(text);
    }
  }

  /** Blurs the input (commits: canonical reformat + display-scale rounding). */
  async blur(): Promise<void> {
    return (await this.host()).blur();
  }

  /** Focuses the input. */
  async focus(): Promise<void> {
    return (await this.host()).focus();
  }

  /** Whether the input currently announces itself as invalid (aria-invalid). */
  async isInvalid(): Promise<boolean> {
    return (await (await this.host()).getAttribute('aria-invalid')) === 'true';
  }

  /** Whether the input is disabled. */
  async isDisabled(): Promise<boolean> {
    return (await this.host()).getProperty<boolean>('disabled');
  }

  /** Gets the input's placeholder text. */
  async getPlaceholder(): Promise<string> {
    return (await this.host()).getProperty<string>('placeholder');
  }
}

// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { ComponentHarness } from '@angular/cdk/testing';

/** Harness for `tm-switch`. */
export class TmSwitchHarness extends ComponentHarness {
  /** The selector for the `tm-switch` host element. */
  static hostSelector = 'tm-switch';

  private readonly native = this.locatorFor('input[role="switch"]');
  private readonly labelEl = this.locatorFor('.tm-switch__label');

  /** Whether the switch is on. */
  async isChecked(): Promise<boolean> {
    return (await this.native()).getProperty<boolean>('checked');
  }

  /** Whether the switch is disabled. */
  async isDisabled(): Promise<boolean> {
    return (await this.native()).getProperty<boolean>('disabled');
  }

  /** Whether the switch announces itself as invalid. */
  async isInvalid(): Promise<boolean> {
    return (await (await this.native()).getAttribute('aria-invalid')) === 'true';
  }

  /** The projected label text, trimmed. */
  async getLabelText(): Promise<string> {
    return (await (await this.labelEl()).text()).trim();
  }

  /** Toggles the switch by clicking it, as the user would. */
  async toggle(): Promise<void> {
    return (await this.native()).click();
  }

  /** Focuses the switch. */
  async focus(): Promise<void> {
    return (await this.native()).focus();
  }

  /** Blurs the switch. */
  async blur(): Promise<void> {
    return (await this.native()).blur();
  }
}

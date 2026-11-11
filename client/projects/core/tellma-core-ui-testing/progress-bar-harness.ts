// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { ComponentHarness } from '@angular/cdk/testing';

/** Harness for `tm-progress-bar`. */
export class TmProgressBarHarness extends ComponentHarness {
  /** The selector for the `tm-progress-bar` host element. */
  static hostSelector = 'tm-progress-bar';

  /** Whether the bar is on screen (active, or within its minimum display time). */
  async isShown(): Promise<boolean> {
    return !(await (await this.host()).hasClass('tm-progress-bar--hidden'));
  }

  /** The value (0–100), or `null` while indeterminate or hidden. */
  async getValue(): Promise<number | null> {
    const value = await (await this.host()).getAttribute('aria-valuenow');
    return value === null ? null : Number(value);
  }

  /** Whether a shown bar is indeterminate (sweeping). */
  async isIndeterminate(): Promise<boolean> {
    return (await this.isShown()) && (await this.getValue()) === null;
  }

  /** The accessible name given through `label`, or `null`. */
  async getLabel(): Promise<string | null> {
    return (await this.host()).getAttribute('aria-label');
  }

  /** The fill tone, read off the host classes. */
  async getTone(): Promise<'accent' | 'success' | 'warning' | 'error'> {
    const host = await this.host();
    for (const tone of ['success', 'warning', 'error'] as const) {
      if (await host.hasClass(`tm-progress-bar--${tone}`)) {
        return tone;
      }
    }
    return 'accent';
  }
}

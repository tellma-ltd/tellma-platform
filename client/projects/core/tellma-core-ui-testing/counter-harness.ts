// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { ComponentHarness } from '@angular/cdk/testing';

/** Harness for `tm-counter`. */
export class TmCounterHarness extends ComponentHarness {
  /** The selector for the `tm-counter` host element. */
  static hostSelector = 'tm-counter';

  /** The shown text ("12", "99+"), or `''` when the counter renders nothing. */
  async getText(): Promise<string> {
    return (await (await this.host()).text()).trim();
  }

  /** Whether the counter shows anything (a `null`, or a hidden zero, does not). */
  async isShown(): Promise<boolean> {
    return !(await (await this.host()).hasClass('tm-counter--empty'));
  }

  /** The fill tone, read off the host's `data-tone`. */
  async getTone(): Promise<'neutral' | 'accent' | 'attention'> {
    const tone = await (await this.host()).getAttribute('data-tone');
    return tone === 'accent' || tone === 'attention' ? tone : 'neutral';
  }

  /** Whether the counter is inline or overlaid on its parent's corner. */
  async getPlacement(): Promise<'inline' | 'overlay'> {
    return (await (await this.host()).hasClass('tm-counter--overlay')) ? 'overlay' : 'inline';
  }

  /** Whether the counter is hidden from assistive technology (overlay counters are). */
  async isHiddenFromAssistiveTechnology(): Promise<boolean> {
    return (await (await this.host()).getAttribute('aria-hidden')) === 'true';
  }
}

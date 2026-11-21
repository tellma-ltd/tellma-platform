// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { ComponentHarness } from '@angular/cdk/testing';

import { TmTooltipHarness } from './tooltip-harness';

/** Harness for `tm-relative-time`. */
export class TmRelativeTimeHarness extends ComponentHarness {
  /** The selector for the `tm-relative-time` host element. */
  static hostSelector = 'tm-relative-time';

  /** The `<time>` element the text renders in. */
  private readonly time = this.locatorFor('time');

  /** The relative text ("2 min ago"). */
  async getText(): Promise<string> {
    return (await (await this.time()).text()).trim();
  }

  /** The machine-readable instant (the `datetime` attribute). */
  async getDateTime(): Promise<string | null> {
    return (await this.time()).getAttribute('datetime');
  }

  /**
   * The absolute date and time — the tooltip's text, which is also the
   * accessible description, so it is available without hovering.
   */
  async getAbsoluteText(): Promise<string | null> {
    return (await this.locatorFor(TmTooltipHarness)()).getDescription();
  }
}

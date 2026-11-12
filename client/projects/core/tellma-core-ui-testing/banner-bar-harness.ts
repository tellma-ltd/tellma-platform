// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { ComponentHarness } from '@angular/cdk/testing';

/** Harness for `tm-banner-bar`. */
export class TmBannerBarHarness extends ComponentHarness {
  /** The selector for the `tm-banner-bar` host element. */
  static hostSelector = 'tm-banner-bar';

  /** The tone (`info`, `warning`, `error`). */
  async getTone(): Promise<string | null> {
    return (await this.host()).getAttribute('data-tone');
  }

  /** The message as drawn (without the hidden tone prefix). */
  async getMessage(): Promise<string> {
    const message = await (await this.locatorFor('.tm-banner-bar__message')()).text();
    const prefix = await (await this.locatorFor('.tm-banner-bar__tone')()).text();
    return message.slice(prefix.length).trim();
  }

  /** The hidden tone prefix read before the message ("Warning:"). */
  async getTonePrefix(): Promise<string> {
    return (await (await this.locatorFor('.tm-banner-bar__tone')()).text()).trim();
  }

  /** The action button's label, or `null` when there is none. */
  async getActionLabel(): Promise<string | null> {
    const action = await this.locatorForOptional('.tm-banner-bar__action')();
    return action === null ? null : (await action.text()).trim();
  }

  /** The pager's position text ("1 of 3"), or `null` when the pager is off. */
  async getPosition(): Promise<string | null> {
    const position = await this.locatorForOptional('.tm-banner-bar__position')();
    return position === null ? null : (await position.text()).trim();
  }

  /** Whether the bar has a dismiss button. */
  async isDismissible(): Promise<boolean> {
    return (await this.locatorForOptional('.tm-banner-bar__dismiss')()) !== null;
  }

  /** The accessible names of the bar's icon buttons, in order. */
  async getButtonLabels(): Promise<Array<string | null>> {
    const buttons = await this.locatorForAll('.tm-banner-bar__button')();
    return Promise.all(buttons.map((button) => button.getAttribute('aria-label')));
  }

  /** Clicks the action button. */
  async clickAction(): Promise<void> {
    return (await this.locatorFor('.tm-banner-bar__action')()).click();
  }

  /** Clicks the dismiss button. */
  async dismiss(): Promise<void> {
    return (await this.locatorFor('.tm-banner-bar__dismiss')()).click();
  }

  /** Clicks the pager's previous button. */
  async previous(): Promise<void> {
    return (await this.locatorFor('.tm-banner-bar__previous')()).click();
  }

  /** Clicks the pager's next button. */
  async next(): Promise<void> {
    return (await this.locatorFor('.tm-banner-bar__next')()).click();
  }
}

// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { ComponentHarness } from '@angular/cdk/testing';

import { TmSelectHarness } from './select-harness';

/**
 * Harness for `tm-pager`: the status, the previous and next buttons, and
 * the page-size select.
 */
export class TmPagerHarness extends ComponentHarness {
  /** The selector for the `tm-pager` host element. */
  static hostSelector = 'tm-pager';

  /** The previous button. */
  private readonly previousButton = this.locatorFor('.tm-pager__previous');

  /** The next button. */
  private readonly nextButton = this.locatorFor('.tm-pager__next');

  /** `range` (pages of a list) or `item` (single items). */
  async getMode(): Promise<'range' | 'item'> {
    return (await (await this.host()).getAttribute('data-mode')) === 'item' ? 'item' : 'range';
  }

  /** The visible status, such as "1–20 / 55", or `null` while the total is unknown. */
  async getStatusText(): Promise<string | null> {
    const fraction = await this.locatorForOptional('.tm-pager__fraction')();
    return fraction === null ? null : (await fraction.text()).trim();
  }

  /** The sentence a screen reader reads, such as "Items 1 to 20 of 55", or `null`. */
  async getStatusSentence(): Promise<string | null> {
    const sentence = await this.locatorForOptional('.tm-pager__sentence')();
    return sentence === null ? null : (await sentence.text()).trim();
  }

  /** The previous button's accessible name. */
  async getPreviousLabel(): Promise<string | null> {
    return (await this.previousButton()).getAttribute('aria-label');
  }

  /** The next button's accessible name. */
  async getNextLabel(): Promise<string | null> {
    return (await this.nextButton()).getAttribute('aria-label');
  }

  /** Whether the previous button is disabled. */
  async isPreviousDisabled(): Promise<boolean> {
    return (await this.previousButton()).getProperty<boolean>('disabled');
  }

  /** Whether the next button is disabled. */
  async isNextDisabled(): Promise<boolean> {
    return (await this.nextButton()).getProperty<boolean>('disabled');
  }

  /** Clicks the previous button. */
  async previous(): Promise<void> {
    return (await this.previousButton()).click();
  }

  /** Clicks the next button. */
  async next(): Promise<void> {
    return (await this.nextButton()).click();
  }

  /**
   * The page-size select, or `null` when the pager offers no sizes. The
   * select loads on demand: when only its stand-in is there, a pointer over
   * the stand-in loads it, and this waits for it to land (`null` if it
   * has not landed after a bounded wait).
   */
  async getSizeSelect(): Promise<TmSelectHarness | null> {
    const proxy = await this.locatorForOptional('.tm-pager__proxy')();
    if (proxy !== null) {
      await proxy.dispatchEvent('pointerenter');
      // The chunk arrives in its own time; settle until the stand-in
      // retires, a bounded number of times.
      for (let attempt = 0; attempt < 100; attempt += 1) {
        if ((await this.locatorForOptional('.tm-pager__proxy')()) === null) {
          break;
        }
        await this.forceStabilize();
      }
    }
    return this.locatorForOptional(TmSelectHarness)();
  }
}

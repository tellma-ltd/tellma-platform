// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { ComponentHarness } from '@angular/cdk/testing';
import { TmBannerBarHarness } from '@tellma/core-ui-testing';

// The banner row a shell renders: whether it is open, the banner it shows
// (through the component library's banner-bar harness, which reads its
// message, tone, action and pager and presses its buttons), and the live
// region that announces each banner as it is queued.

/**
 * Harness for `tm-banner-host`, the shell's banner row.
 *
 * @example
 * ```ts
 * const host = await TestbedHarnessEnvironment.loader(fixture).getHarness(TmBannerHostHarness);
 * expect(await host.getAnnouncement()).toBe('Connection lost. Reconnecting…');
 * await (await host.getBar())?.dismiss();
 * ```
 */
export class TmBannerHostHarness extends ComponentHarness {
  /** The selector of the `tm-banner-host` element. */
  static hostSelector = 'tm-banner-host';

  /** The live region, which is read but never drawn. */
  readonly #live = this.locatorFor('.tm-banner-host__live');

  /** The empty box an emptied row holds until the idle gate closes it. */
  readonly #empty = this.locatorForOptional('.tm-banner-host__empty');

  /**
   * The banner the row shows.
   *
   * @returns The bar's harness, or `null` while the row shows no banner.
   */
  getBar(): Promise<TmBannerBarHarness | null> {
    return this.locatorForOptional(TmBannerBarHarness)();
  }

  /**
   * Whether the row is open: showing a banner, or emptied and waiting for
   * the idle gate to close it.
   *
   * @returns Whether the row takes its height.
   */
  async isOpen(): Promise<boolean> {
    return (await this.getBar()) !== null || (await this.#empty()) !== null;
  }

  /**
   * Whether the row is open and empty: its last banner closed while the
   * idle gate held the row open.
   *
   * @returns Whether the row shows the empty box.
   */
  async isEmpty(): Promise<boolean> {
    return (await this.#empty()) !== null;
  }

  /**
   * What the live region says now.
   *
   * @returns The announcement; empty while nothing is announced.
   */
  async getAnnouncement(): Promise<string> {
    return (await (await this.#live()).text()).trim();
  }

  /**
   * How urgently the live region speaks: assertive for a level-1 banner.
   *
   * @returns The region's politeness.
   */
  async getPoliteness(): Promise<'assertive' | 'polite'> {
    return (await (await this.#live()).getAttribute('aria-live')) === 'assertive' ? 'assertive' : 'polite';
  }
}

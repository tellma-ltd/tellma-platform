// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { ComponentHarness } from '@angular/cdk/testing';

import { TmDashboardGridHarness } from './tm-dashboard-grid-harness';

// The tenant home as a test reads it, through its host: whether the
// platform's page, a feature's hatch or the hatch's loading skeleton shows,
// the greeting and its date line, and the grid of the home widgets.

/** What the home's host shows. */
export type TmHomePageView = 'page' | 'hatch' | 'loading';

/**
 * Harness for the tenant home, `tm-home-host`.
 *
 * @example
 * ```ts
 * const home = await loader.getHarness(TmHomePageHarness);
 * expect(await home.getGreeting()).toBe('Good morning, Farah');
 * expect(await (await home.getGrid())?.getTitles()).toEqual(['My assignments', 'Recently visited', 'Assigned by me']);
 * ```
 */
export class TmHomePageHarness extends ComponentHarness {
  /** The selector of the `tm-home-host` element. */
  static hostSelector = 'tm-home-host';

  /** The platform's home page. */
  readonly #page = this.locatorForOptional('tm-home-page');

  /** The skeleton a hatch's chunk loads behind. */
  readonly #skeleton = this.locatorForOptional('tm-page-skeleton');

  /** The greeting's heading. */
  readonly #title = this.locatorForOptional('.tm-home-greeting__title');

  /** The date line. */
  readonly #date = this.locatorForOptional('.tm-home-greeting__date');

  /**
   * What the host shows: the platform's page, a feature's hatch, or the
   * skeleton the hatch's chunk loads behind.
   *
   * @returns The view.
   */
  async getView(): Promise<TmHomePageView> {
    if ((await this.#page()) !== null) {
      return 'page';
    }
    return (await this.#skeleton()) === null ? 'hatch' : 'loading';
  }

  /**
   * The greeting.
   *
   * @returns The heading's text, or `null` when the platform's page does not show.
   */
  async getGreeting(): Promise<string | null> {
    const title = await this.#title();
    return title === null ? null : (await title.text()).trim();
  }

  /**
   * The date line under the greeting.
   *
   * @returns Today's date as the line writes it, or `null` when the platform's page does not show.
   */
  async getDate(): Promise<string | null> {
    const date = await this.#date();
    return date === null ? null : (await date.text()).trim();
  }

  /**
   * The grid of the home widgets.
   *
   * @returns The grid, or `null` when the platform's page does not show.
   */
  getGrid(): Promise<TmDashboardGridHarness | null> {
    return this.locatorForOptional(TmDashboardGridHarness)();
  }
}

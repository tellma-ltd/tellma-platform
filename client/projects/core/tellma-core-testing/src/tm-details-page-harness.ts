// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { ComponentHarness, HarnessPredicate } from '@angular/cdk/testing';
import { TmButtonHarness, TmEmptyStateHarness, TmProgressBarHarness } from '@tellma/core-ui-testing';

import { TmDetailsSummaryHarness } from './tm-details-summary-harness';

// The harness of @tellma/core/details's page: the routed host and the
// source page draw the same frame, so one harness reads both. It reads the
// header (crumb, title, badges, toolbar), the band's busy and error states,
// the error summary and the polite status, and drives the toolbar as a user
// would.

/** The toolbar's buttons, by what they do. */
export type TmDetailsPageButton = 'edit' | 'create' | 'save' | 'cancel' | 'refresh' | 'actions';

/** Harness for a details page: `tm-details-host` on a details route, or `tm-details-page` over a source. */
export class TmDetailsPageHarness extends ComponentHarness {
  /** The selector for the routed host and the source page. */
  static hostSelector = 'tm-details-host, tm-details-page';

  /** The title. */
  private readonly title = this.locatorFor('.tm-details__title');

  /** The band. */
  private readonly band = this.locatorFor('.tm-details__band');

  /** The progress bar over the band. */
  private readonly bar = this.locatorFor(TmProgressBarHarness);

  /** The polite status region. */
  private readonly live = this.locatorFor('.tm-details__live');

  /** The crumb's text, or `null` on a page without one, such as a source page. */
  async getCrumb(): Promise<string | null> {
    const crumb = await this.locatorForOptional('.tm-details__crumb')();
    return crumb === null ? null : (await crumb.text()).trim();
  }

  /** The crumb's link, or `null` on a page without one. */
  async getCrumbHref(): Promise<string | null> {
    const crumb = await this.locatorForOptional('.tm-details__crumb')();
    return crumb === null ? null : crumb.getAttribute('href');
  }

  /** The title's text; empty while its skeleton holds its place. */
  async getTitle(): Promise<string> {
    return (await (await this.title()).text()).trim();
  }

  /** Whether the title is a skeleton: the record's name is not known yet. */
  async isTitleLoading(): Promise<boolean> {
    return (await this.locatorForOptional('.tm-details__title tm-skeleton')()) !== null;
  }

  /** The badges shown in the title row, in order: Active or Inactive, then Unsaved changes while dirty. */
  async getBadges(): Promise<string[]> {
    const boxes = await this.locatorForAll('.tm-details__badge:not(.tm-details__badge--hidden) tm-badge')();
    return Promise.all(boxes.map(async (badge) => (await badge.text()).trim()));
  }

  /**
   * A toolbar button, or `null` when the toolbar does not show it in the
   * current mode, such as Save in view mode.
   *
   * @param button - The button.
   * @returns Its harness.
   */
  async getButton(button: TmDetailsPageButton): Promise<TmButtonHarness | null> {
    return this.locatorForOptional(new HarnessPredicate(TmButtonHarness, { selector: `.tm-details__${button}` }))();
  }

  /**
   * Whether a toolbar button is shown and may be used: neither natively
   * disabled nor disabled with a reason.
   *
   * @param button - The button.
   * @returns Whether it is enabled; `false` when it is not shown.
   */
  async isEnabled(button: TmDetailsPageButton): Promise<boolean> {
    const harness = await this.getButton(button);
    return harness !== null && !(await harness.isDisabled()) && (await harness.getDisabledReason()) === null;
  }

  /**
   * Why a toolbar button is disabled, for a fact the user can act on.
   *
   * @param button - The button.
   * @returns The reason, or `null` when it has none or is not shown.
   */
  async getDisabledReason(button: TmDetailsPageButton): Promise<string | null> {
    const harness = await this.getButton(button);
    return harness === null ? null : harness.getDisabledReason();
  }

  /**
   * Clicks a toolbar button. Fails when the toolbar does not show it.
   *
   * @param button - The button.
   */
  async click(button: TmDetailsPageButton): Promise<void> {
    const harness = await this.getButton(button);
    if (harness === null) {
      throw new Error(`The details page shows no ${button} button in its current mode.`);
    }
    await harness.click();
  }

  /** The mode the toolbar shows: `view` with Edit or Refresh, `edit` with Save. */
  async getMode(): Promise<'view' | 'edit'> {
    return (await this.getButton('save')) === null ? 'view' : 'edit';
  }

  /** Whether the progress bar shows over the band: a read runs over a held record. */
  async isProgressShown(): Promise<boolean> {
    return (await this.bar()).isShown();
  }

  /** Whether the band is busy: a read that is not quiet runs. */
  async isBusy(): Promise<boolean> {
    return (await (await this.band()).getAttribute('aria-busy')) === 'true';
  }

  /** What the polite status region says, such as "Loading". */
  async getStatus(): Promise<string> {
    return (await (await this.live()).text()).trim();
  }

  /** The band's error state, or `null` while the record shows. */
  async getErrorState(): Promise<TmEmptyStateHarness | null> {
    return this.locatorForOptional(TmEmptyStateHarness)();
  }

  /** The error summary a failed Save opened in the status strip, or `null` while it shows none. */
  async getErrorSummary(): Promise<TmDetailsSummaryHarness | null> {
    return this.locatorForOptional(TmDetailsSummaryHarness)();
  }

  /** The error state's way out to the list, such as "Customers", or `null`. */
  async getErrorLink(): Promise<TmButtonHarness | null> {
    return this.locatorForOptional(new HarnessPredicate(TmButtonHarness, { selector: 'tm-error-state a[tmButton]' }))();
  }
}

// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import {
  ComponentHarness,
  HarnessPredicate,
  type BaseHarnessFilters,
  type ModifierKeys,
} from '@angular/cdk/testing';

import { TmGridHarness } from './grid-harness';

/** What a chart shows: the host's no-data state, the card, the table view, or the plot. */
export type TmChartView = 'empty' | 'card' | 'table' | 'plot';

/** Filters for {@link TmChartHarness}. */
export interface TmChartHarnessFilters extends BaseHarnessFilters {
  /** The plot's accessible name ("Sales, column chart"), or a pattern it matches. */
  name?: string | RegExp;
}

/**
 * Harness for `tm-chart`: what it shows, the plot's accessible name and
 * description, the card's value, label and badge, the card's activation,
 * and the table view's grid. The plot's marks are canvas pixels, so a
 * point is drilled through the table view (or a page-level pointer).
 */
export class TmChartHarness extends ComponentHarness {
  /** The selector for the `tm-chart` host element. */
  static hostSelector = 'tm-chart';

  /** Gets a predicate that finds charts by the plot's accessible name. */
  static with(options: TmChartHarnessFilters = {}): HarnessPredicate<TmChartHarness> {
    return new HarnessPredicate(TmChartHarness, options).addOption(
      'name',
      options.name,
      async (harness, name) => HarnessPredicate.stringMatches(await harness.getName(), name),
    );
  }

  /** The plot's figure, while the spec draws. */
  private readonly figure = this.locatorForOptional('.tm-chart__figure');
  /** The plot's `role="img"` element. */
  private readonly plot = this.locatorForOptional('.tm-chart__plot');
  /** The card. */
  private readonly card = this.locatorForOptional('.tm-chart__card');
  /** The card's button, when the card drills. */
  private readonly cardButton = this.locatorForOptional('.tm-chart__card-body--action');
  /** The card's value. */
  private readonly cardValue = this.locatorForOptional('.tm-chart__card-value');
  /** The card's measure label. */
  private readonly cardLabel = this.locatorForOptional('.tm-chart__card-label');
  /** The card's threshold badge. */
  private readonly cardBadge = this.locatorForOptional('.tm-chart__card-badge');
  /** The table view's container. */
  private readonly table = this.locatorForOptional('.tm-chart__table');

  /** What the chart shows now. */
  async getView(): Promise<TmChartView> {
    if ((await this.card()) !== null) {
      return 'card';
    }
    if ((await this.table()) !== null) {
      return 'table';
    }
    const figure = await this.figure();
    if (figure !== null && (await figure.getProperty<boolean>('hidden')) === false) {
      return 'plot';
    }
    return 'empty';
  }

  /** The plot's accessible name, or `''` where the spec does not draw. */
  async getName(): Promise<string> {
    return (await (await this.plot())?.getAttribute('aria-label')) ?? '';
  }

  /** The plot's accessible description, or `''` where the spec does not draw. */
  async getDescription(): Promise<string> {
    const plot = await this.plot();
    const id = plot === null ? null : await plot.getAttribute('aria-describedby');
    if (id === null) {
      return '';
    }
    const description = await this.locatorForOptional(`[id="${id}"]`)();
    return (await description?.getProperty<string>('textContent'))?.trim() ?? '';
  }

  /** Whether the plot holds the engine's canvas: the engine loaded and drew. */
  async hasCanvas(): Promise<boolean> {
    return (await this.locatorForOptional('.tm-chart__plot canvas')()) !== null;
  }

  /** The card's formatted value, or `null` where the chart is not a card. */
  async getCardValue(): Promise<string | null> {
    return (await (await this.cardValue())?.text()) ?? null;
  }

  /** The card's measure label, or `null` where the chart is not a card. */
  async getCardLabel(): Promise<string | null> {
    return (await (await this.cardLabel())?.text()) ?? null;
  }

  /** The text of the card's threshold badge, or `null` when no threshold is reached. */
  async getCardBadge(): Promise<string | null> {
    return (await (await this.cardBadge())?.text()) ?? null;
  }

  /** Whether the card is a button: it drills. */
  async isCardActivatable(): Promise<boolean> {
    return (await this.cardButton()) !== null;
  }

  /** Clicks the card, with modifiers for a new tab (Ctrl or ⌘). */
  async activateCard(modifiers: ModifierKeys = {}): Promise<void> {
    const button = await this.cardButton();
    if (button === null) {
      throw new Error('TmChartHarness: the card does not drill.');
    }
    await button.click(modifiers);
  }

  /** The table view's grid, or `null` while the table view is not shown. */
  async getTable(): Promise<TmGridHarness | null> {
    return this.locatorForOptional(TmGridHarness)();
  }
}

// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { ComponentHarness, HarnessPredicate } from '@angular/cdk/testing';
import { TmChipSetHarness, TmMenuHarness, TmRadioGroupHarness, TmSwitchHarness } from '@tellma/core-ui-testing';

import { TmColumnFilterHarness } from './tm-column-filter-harness';
import { TmExpressionEditorHarness } from './tm-expression-editor-harness';

// The search page's Filters popup, in its popover or its drawer: the tabs
// below sm, the filter expression's editor, Active only, the declared
// custom filters, the applied column filters and a hidden order as chips,
// Add column filter, and the footer's Clear all and Apply. What it stages
// applies only on Apply; a dismissal discards it.

/**
 * Harness for the search page's Filters popup.
 *
 * @example
 * ```ts
 * const filters = await page.openFilters();
 * await (await filters.getActiveOnly())!.toggle();
 * await filters.apply();
 * ```
 */
export class TmSearchFiltersHarness extends ComponentHarness {
  /** The selector of the popup's body. */
  static hostSelector = 'tm-search-filters-form';

  /** The footer's Apply. */
  readonly #apply = this.locatorFor('[data-tm-filters-apply]');

  /** The footer's Clear all. */
  readonly #clear = this.locatorFor('[data-tm-filters-clear]');

  /**
   * The tabs' radio group, which the popup holds below `sm`.
   *
   * @returns Its harness, or `null` when the tabs show elsewhere or none are declared.
   */
  getTabs(): Promise<TmRadioGroupHarness | null> {
    return this.locatorForOptional(new HarnessPredicate(TmRadioGroupHarness, { selector: '[data-tm-filters-tabs]' }))();
  }

  /**
   * The filter expression's editor.
   *
   * @returns Its harness, or `null` when the page takes no filter expression.
   */
  getEditor(): Promise<TmExpressionEditorHarness | null> {
    return this.locatorForOptional(TmExpressionEditorHarness)();
  }

  /**
   * The Active only switch, on an activatable entity.
   *
   * @returns Its harness, or `null` when the popup offers none.
   */
  getActiveOnly(): Promise<TmSwitchHarness | null> {
    return this.locatorForOptional(new HarnessPredicate(TmSwitchHarness, { selector: '[data-tm-filters-active]' }))();
  }

  /**
   * A declared switch filter.
   *
   * @param key - The filter's key.
   * @returns Its harness, or `null` when no switch filter has the key.
   */
  getCustomSwitch(key: string): Promise<TmSwitchHarness | null> {
    return this.locatorForOptional(new HarnessPredicate(TmSwitchHarness, { selector: `[data-tm-filters-custom="${key}"]` }))();
  }

  /**
   * A declared filter with options, as a radio group whose first option is Any.
   *
   * @param key - The filter's key.
   * @returns Its harness, or `null` when no such filter has the key.
   */
  getCustomOptions(key: string): Promise<TmRadioGroupHarness | null> {
    return this.locatorForOptional(new HarnessPredicate(TmRadioGroupHarness, { selector: `[data-tm-filters-custom="${key}"]` }))();
  }

  /**
   * The chips of the applied column filters and of an order on a column the page does not show.
   *
   * @returns The chip set's harness, or `null` when there are none.
   */
  getApplied(): Promise<TmChipSetHarness | null> {
    return this.locatorForOptional(new HarnessPredicate(TmChipSetHarness, { selector: '[data-tm-filters-applied]' }))();
  }

  /**
   * The chips' labels, in order.
   *
   * @returns The labels.
   */
  async getAppliedLabels(): Promise<string[]> {
    const applied = await this.getApplied();
    if (applied === null) {
      return [];
    }
    return Promise.all((await applied.getChips()).map((chip) => chip.getLabel()));
  }

  /**
   * The columns Add column filter offers.
   *
   * @returns Their labels, or an empty list when the button does not show.
   */
  async getAddableColumns(): Promise<string[]> {
    const add = await this.locatorForOptional('[data-tm-filters-add]')();
    if (add === null) {
      return [];
    }
    await add.click();
    const labels = await (await this.documentRootLocatorFactory().locatorFor(TmMenuHarness)()).getItemLabels();
    await add.click();
    return labels;
  }

  /**
   * Opens a column's filter in place from Add column filter.
   *
   * @param label - The column's label.
   * @returns The column filter's harness.
   */
  async addColumnFilter(label: string): Promise<TmColumnFilterHarness> {
    await (await this.locatorFor('[data-tm-filters-add]')()).click();
    await (await this.documentRootLocatorFactory().locatorFor(TmMenuHarness)()).clickItem(label);
    return this.locatorFor(TmColumnFilterHarness)();
  }

  /**
   * The column filter shown in place, from a chip or Add column filter.
   *
   * @returns Its harness, or `null` while the sections show.
   */
  getColumnFilter(): Promise<TmColumnFilterHarness | null> {
    return this.locatorForOptional(TmColumnFilterHarness)();
  }

  /** Goes back from a column filter in place to the sections. */
  async back(): Promise<void> {
    return (await this.locatorFor('[data-tm-filters-back]')()).click();
  }

  /** Clicks Clear all, which stages every filter's default. */
  async clearAll(): Promise<void> {
    return (await this.#clear()).click();
  }

  /**
   * Whether Apply is disabled: the last completed check of the expression found problems.
   *
   * @returns Whether it is.
   */
  async isApplyDisabled(): Promise<boolean> {
    return (await (await this.#apply()).getProperty<boolean>('disabled')) === true;
  }

  /** Clicks Apply, which applies what the popup staged and closes it. */
  async apply(): Promise<void> {
    return (await this.#apply()).click();
  }
}

// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { ComponentHarness, HarnessPredicate } from '@angular/cdk/testing';
import { TmButtonHarness, TmEmptyStateHarness, TmGridHarness, TmPagerHarness, TmProgressBarHarness } from '@tellma/core-ui-testing';

// The search page an entity's search route renders: its title, the Create
// control and Refresh of its toolbar, the search field and its clear
// button, the pager, the grid of its rows (through the component library's
// grid harness, which reads the headers and cells and sorts by a header),
// the first load's skeleton, the progress bar of a re-read, and the state a
// read shows in the rows' place with the way out it offers.

/** What the Create control of a search page is: a link to the create page, or a button that says why it is disabled. */
export interface TmSearchPageCreate {
  /** The create page's URL; `null` for the disabled button. */
  readonly href: string | null;
  /** The label, or the accessible name of the icon-only control. */
  readonly label: string;
  /** Why the button is disabled; `null` for the link. */
  readonly disabledReason: string | null;
}

/**
 * Harness for `tm-search-host`, an entity's search page.
 *
 * @example
 * ```ts
 * const page = await TestbedHarnessEnvironment.loader(fixture).getHarness(TmSearchPageHarness);
 * expect(await (await page.getGrid()).getHeaderTexts()).toEqual(['Code', 'Name']);
 * expect(await page.getStateHeading()).toBe('No matches');
 * await page.refresh();
 * ```
 */
export class TmSearchPageHarness extends ComponentHarness {
  /** The selector of the `tm-search-host` element. */
  static hostSelector = 'tm-search-host';

  /** The page's title. */
  readonly #title = this.locatorFor('[tmPageTitle]');

  /** The Create control. */
  readonly #create = this.locatorForOptional(new HarnessPredicate(TmButtonHarness, { selector: '[data-tm-create]' }));

  /** Refresh. */
  readonly #refresh = this.locatorFor('[data-tm-refresh]');

  /** The More button. */
  readonly #more = this.locatorForOptional('[data-tm-more]');

  /** The first load's skeleton. */
  readonly #skeleton = this.locatorForOptional('[data-tm-skeleton]');

  /** The results region. */
  readonly #region = this.locatorFor('.tm-search-host__region');

  /** The Columns button of the grid's row-end header. */
  readonly #columns = this.locatorForOptional('[data-tm-columns]');

  /** The state in the rows' place. */
  readonly #state = this.locatorForOptional('tm-search-state');

  /** The search field's input. */
  readonly #field = this.locatorFor('[data-tm-search]');

  /** The search field's clear button. */
  readonly #clearSearch = this.locatorFor('[data-tm-search-clear]');

  /**
   * The page's title, the entity's plural label.
   *
   * @returns The title.
   */
  async getTitle(): Promise<string> {
    return (await (await this.#title()).text()).trim();
  }

  /**
   * The grid of the rows.
   *
   * @returns The grid's harness, or `null` when the entity's own results component replaces it.
   */
  getGrid(): Promise<TmGridHarness | null> {
    return this.locatorForOptional(TmGridHarness)();
  }

  /**
   * The pager at the toolbar's end.
   *
   * @returns The pager's harness.
   */
  getPager(): Promise<TmPagerHarness> {
    return this.locatorFor(TmPagerHarness)();
  }

  /**
   * The Create control.
   *
   * @returns What it is, or `null` when the page offers none.
   */
  async getCreate(): Promise<TmSearchPageCreate | null> {
    const create = await this.#create();
    if (create === null) {
      return null;
    }
    const host = await create.host();
    const label = (await host.getAttribute('aria-label')) ?? (await create.getText());
    return (await create.isLink())
      ? { href: await host.getAttribute('href'), label, disabledReason: null }
      : { href: null, label, disabledReason: await create.getDisabledReason() };
  }

  /** Clicks Refresh, which reads the query again. */
  async refresh(): Promise<void> {
    return (await this.#refresh()).click();
  }

  /**
   * The search field's text, as typed.
   *
   * @returns The text.
   */
  async getSearchText(): Promise<string> {
    return (await this.#field()).getProperty<string>('value');
  }

  /**
   * The search field's placeholder, which is also its accessible name.
   *
   * @returns The placeholder.
   */
  async getSearchPlaceholder(): Promise<string | null> {
    return (await this.#field()).getAttribute('placeholder');
  }

  /**
   * Whether the search field's clear button shows: it does while the field holds text.
   *
   * @returns Whether it does.
   */
  async isClearSearchShown(): Promise<boolean> {
    return !(await (await this.#clearSearch()).hasClass('tm-search-page-field__clear--hidden'));
  }

  /** Clicks the search field's clear button, which clears the text as an undoable edit and searches everything. */
  async clearSearchField(): Promise<void> {
    return (await this.#clearSearch()).click();
  }

  /**
   * Whether the toolbar shows the More button.
   *
   * @returns Whether it does.
   */
  async hasMore(): Promise<boolean> {
    return (await this.#more()) !== null;
  }

  /**
   * Whether the first load's skeleton rows show.
   *
   * @returns Whether they do.
   */
  async isFirstLoad(): Promise<boolean> {
    return (await this.#skeleton()) !== null;
  }

  /**
   * Whether a read runs: the region is busy and its progress bar shows.
   *
   * @returns Whether one does.
   */
  async isLoading(): Promise<boolean> {
    const busy = (await (await this.#region()).getAttribute('aria-busy')) === 'true';
    const bar = await this.locatorForOptional(TmProgressBarHarness)();
    return busy && bar !== null && (await bar.isShown());
  }

  /**
   * Whether the Columns button heads the grid's row-end column.
   *
   * @returns Whether it does.
   */
  async hasColumnsButton(): Promise<boolean> {
    return (await this.#columns()) !== null;
  }

  /**
   * The state in the rows' place: no data, no matches, an invalid filter or
   * link, a calculation that failed, or the error view.
   *
   * @returns The state's harness, or `null` while rows or the first load show.
   */
  getState(): Promise<TmEmptyStateHarness | null> {
    return this.locatorForOptional(new HarnessPredicate(TmEmptyStateHarness, { ancestor: 'tm-search-state' }))();
  }

  /**
   * The heading of the state in the rows' place.
   *
   * @returns The heading, or `null` for no state.
   */
  async getStateHeading(): Promise<string | null> {
    return (await this.getState())?.getHeading() ?? null;
  }

  /**
   * The code a state shows: an invalid filter's token.
   *
   * @returns The text, or `null` for none.
   */
  async getStateToken(): Promise<string | null> {
    const token = await this.locatorForOptional('tm-search-state code')();
    return token === null ? null : (await token.text()).trim();
  }

  /**
   * The labels of the state's buttons and links, such as Clear filters, Try
   * again or the entity's "New" link.
   *
   * @returns The labels, in order.
   */
  async getStateActions(): Promise<string[]> {
    if ((await this.#state()) === null) {
      return [];
    }
    const actions = await this.locatorForAll('tm-search-state button, tm-search-state a')();
    return Promise.all(actions.map(async (action) => (await action.text()).trim()));
  }

  /**
   * Clicks one of the state's buttons or links.
   *
   * @param label - Its label.
   */
  async clickStateAction(label: string): Promise<void> {
    const actions = await this.locatorForAll('tm-search-state button, tm-search-state a')();
    for (const action of actions) {
      if ((await action.text()).trim() === label) {
        return action.click();
      }
    }
    throw new Error(`The search page's state has no action labelled "${label}".`);
  }
}

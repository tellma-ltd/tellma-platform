// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { ComponentHarness, HarnessPredicate, type TestElement, TestKey } from '@angular/cdk/testing';
import { TmAlertHarness, TmButtonHarness, TmEmptyStateHarness, TmSearchFieldHarness } from '@tellma/core-ui-testing';

// The tenant picker at /tenants: its rows, each with its names, badges and
// Connect; the search, which shows from seven memberships; the notice of a
// tenant the user can no longer open; and its other states: the skeleton,
// the error state, the empty state and the search's no-match message.

/** One of a row's names, as the picker shows it. */
export interface TmTenantPickerName {
  /** The name. */
  readonly text: string;
  /** The language it is marked with, or `null`. */
  readonly lang: string | null;
}

/** The text an element shows, its runs of white space folded to one space. */
async function folded(element: TestElement): Promise<string> {
  return (await element.text()).replace(/\s+/g, ' ').trim();
}

/**
 * Harness for one row of the tenant picker.
 *
 * @example
 * ```ts
 * const [row] = await picker.getRows();
 * expect(await row.getName()).toEqual({ text: 'Acme', lang: 'en' });
 * expect(await row.getConnectHref()).toBe('/1');
 * ```
 */
export class TmTenantPickerRowHarness extends ComponentHarness {
  /** The selector of a row; the skeleton's rows, hidden from assistive technology, are not rows. */
  static hostSelector = '.tm-tenant-picker__row:not([aria-hidden])';

  readonly #name = this.locatorFor('.tm-tenant-picker__name');
  readonly #more = this.locatorFor('.tm-tenant-picker__more');
  readonly #others = this.locatorForAll('.tm-tenant-picker__more [lang]');
  readonly #badges = this.locatorForAll('tm-badge');
  readonly #connect = this.locatorFor(TmButtonHarness);

  /**
   * The name in the active language, in bold.
   *
   * @returns The name and its language.
   */
  async getName(): Promise<TmTenantPickerName> {
    const name = await this.#name();
    return { text: await folded(name), lang: await name.getAttribute('lang') };
  }

  /**
   * The second line: the other names, then the tenant's id.
   *
   * @returns The line's text, such as `أكمي · Tenant 1`.
   */
  async getSecondLine(): Promise<string> {
    return folded(await this.#more());
  }

  /**
   * The other names on the second line, each with its language.
   *
   * @returns The names, in order.
   */
  async getOtherNames(): Promise<TmTenantPickerName[]> {
    return Promise.all(
      (await this.#others()).map(async (element) => ({ text: await folded(element), lang: await element.getAttribute('lang') })),
    );
  }

  /**
   * The badges' labels.
   *
   * @returns The labels, in order.
   */
  async getBadges(): Promise<string[]> {
    return Promise.all((await this.#badges()).map(folded));
  }

  /**
   * Whether the row is a sandbox listed beneath its live tenant.
   *
   * @returns Whether it is indented.
   */
  async isNested(): Promise<boolean> {
    return (await this.host()).hasClass('tm-tenant-picker__row--nested');
  }

  /**
   * Whether the row shows only as a matching sandbox's live tenant.
   *
   * @returns Whether it is dimmed.
   */
  async isDimmed(): Promise<boolean> {
    return (await this.host()).hasClass('tm-tenant-picker__row--dimmed');
  }

  /**
   * Connect's accessible name.
   *
   * @returns The name, such as `Connect to Acme`.
   */
  async getConnectName(): Promise<string | null> {
    return (await (await this.#connect()).host()).getAttribute('aria-label');
  }

  /**
   * Where Connect leads, while the tenant can be opened.
   *
   * @returns The link's `href`, or `null` while Connect is unavailable.
   */
  async getConnectHref(): Promise<string | null> {
    const connect = await this.#connect();
    return (await connect.isLink()) ? (await connect.host()).getAttribute('href') : null;
  }

  /**
   * Why Connect is unavailable.
   *
   * @returns The reason, or `null` while the tenant can be opened.
   */
  async getUnavailableReason(): Promise<string | null> {
    const connect = await this.#connect();
    return (await connect.isLink()) ? null : connect.getDisabledReason();
  }

  /** Clicks Connect. */
  async connect(): Promise<void> {
    await (await this.#connect()).click();
  }

  /** Hovers Connect, which preloads the tenant's home. */
  async hoverConnect(): Promise<void> {
    await (await (await this.#connect()).host()).hover();
  }
}

/**
 * Harness for `tm-tenant-picker`, the page at `/tenants`.
 *
 * @example
 * ```ts
 * const picker = await TestbedHarnessEnvironment.documentRootLoader(fixture).getHarness(TmTenantPickerHarness);
 * await picker.search('acme');
 * expect(await Promise.all((await picker.getRows()).map(async (row) => (await row.getName()).text))).toEqual(['Acme']);
 * ```
 */
export class TmTenantPickerHarness extends ComponentHarness {
  /** The selector of the `tm-tenant-picker` element. */
  static hostSelector = 'tm-tenant-picker';

  readonly #title = this.locatorFor('h1');
  readonly #skeleton = this.locatorForOptional('[aria-busy="true"]');
  readonly #search = this.locatorForOptional(TmSearchFieldHarness);
  readonly #notice = this.locatorForOptional(TmAlertHarness);
  readonly #empty = this.locatorForOptional(new HarnessPredicate(TmEmptyStateHarness, { selector: '.tm-tenant-picker__empty' }));
  readonly #noMatch = this.locatorForOptional('.tm-tenant-picker__no-match');
  readonly #error = this.locatorForOptional('tm-error-state');
  readonly #dismiss = this.locatorFor('tm-alert [tmButton]');
  readonly #emptySignOut = this.locatorFor('.tm-tenant-picker__empty [tmButton]');
  readonly #errorActions = this.locatorForAll('tm-error-state [tmButton]');

  /**
   * The page's heading.
   *
   * @returns The heading's text.
   */
  async getTitle(): Promise<string> {
    return folded(await this.#title());
  }

  /**
   * The rows the list shows, in order.
   *
   * @returns The rows' harnesses; none while the skeleton, the error state or the empty state shows.
   */
  getRows(): Promise<TmTenantPickerRowHarness[]> {
    return this.locatorForAll(TmTenantPickerRowHarness)();
  }

  /**
   * Whether the skeleton shows, while no list is stored and the read runs.
   *
   * @returns Whether it shows.
   */
  async isLoading(): Promise<boolean> {
    return (await this.#skeleton()) !== null;
  }

  /**
   * Whether the search shows.
   *
   * @returns Whether it shows.
   */
  async hasSearch(): Promise<boolean> {
    return (await this.#search()) !== null;
  }

  /**
   * The search text.
   *
   * @returns The text, or `null` while the search is hidden.
   */
  async getSearchText(): Promise<string | null> {
    return (await this.#search())?.getValue() ?? null;
  }

  /**
   * Types a search, replacing the text.
   *
   * @param text - The text.
   * @throws Error when the search is hidden.
   */
  async search(text: string): Promise<void> {
    await (await this.#requireSearch()).setValue(text);
  }

  /**
   * Presses Enter in the search, which follows the only enabled match.
   *
   * @throws Error when the search is hidden.
   */
  async submitSearch(): Promise<void> {
    await (await this.#requireSearch()).press(TestKey.ENTER);
  }

  /**
   * Whether the search's input has focus.
   *
   * @returns Whether it has focus; `false` while the search is hidden.
   */
  async isSearchFocused(): Promise<boolean> {
    return (await (await this.#search())?.isFocused()) ?? false;
  }

  /**
   * The search's no-match message.
   *
   * @returns The message, or `null` while a row matches.
   */
  async getNoMatch(): Promise<string | null> {
    const element = await this.#noMatch();
    return element === null ? null : folded(element);
  }

  /**
   * The notice of a tenant the user can no longer open.
   *
   * @returns The notice's text, or `null` while none shows.
   */
  async getNotice(): Promise<string | null> {
    const notice = await this.#notice();
    return notice === null ? null : (await notice.getText()).replace(/\s+/g, ' ').trim();
  }

  /**
   * Dismisses the notice.
   *
   * @throws Error when no notice shows.
   */
  async dismissNotice(): Promise<void> {
    if ((await this.#notice()) === null) {
      throw new Error('The tenant picker shows no notice.');
    }
    await (await this.#dismiss()).click();
  }

  /**
   * The empty state of a user without memberships.
   *
   * @returns Its heading and message, or `null` while it does not show.
   */
  async getEmptyState(): Promise<{ readonly heading: string; readonly message: string } | null> {
    const empty = await this.#empty();
    return empty === null ? null : { heading: await empty.getHeading(), message: (await empty.getDetail()).replace(/\s+/g, ' ').trim() };
  }

  /** Clicks the empty state's Sign out. */
  async signOutFromEmptyState(): Promise<void> {
    if ((await this.#empty()) === null) {
      throw new Error('The tenant picker shows no empty state.');
    }
    await (await this.#emptySignOut()).click();
  }

  /**
   * The error state of a failed read with nothing stored.
   *
   * @returns Its text, or `null` while it does not show.
   */
  async getError(): Promise<string | null> {
    const error = await this.#error();
    return error === null ? null : folded(error);
  }

  /**
   * Clicks one of the error state's actions, such as Try again.
   *
   * @param label - The action's label.
   * @throws Error when no error state shows, or it has no such action.
   */
  async clickErrorAction(label: string): Promise<void> {
    if ((await this.#error()) === null) {
      throw new Error('The tenant picker shows no error state.');
    }
    for (const button of await this.#errorActions()) {
      if ((await folded(button)) === label) {
        await button.click();
        return;
      }
    }
    throw new Error(`The error state has no action "${label}".`);
  }

  /** The search's harness, which must show. */
  async #requireSearch(): Promise<TmSearchFieldHarness> {
    const search = await this.#search();
    if (search === null) {
      throw new Error('The tenant picker shows no search: it shows from seven memberships.');
    }
    return search;
  }
}

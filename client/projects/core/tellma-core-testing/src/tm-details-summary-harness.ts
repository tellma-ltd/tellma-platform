// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { ComponentHarness, TestKey } from '@angular/cdk/testing';

// The harness of a details page's error summary, which a failed Save opens
// in the status strip: it reads the heading, whether the list shows, and
// each item's text, Fixed mark and whether it is a link, and drives the
// toggle, an item and the key that closes the list.

/** Harness for one item of the error summary. */
export class TmDetailsSummaryItemHarness extends ComponentHarness {
  /** The selector for an item. */
  static hostSelector = '.tm-details-summary__item';

  /** The item's link, when it goes to its field. */
  private readonly link = this.locatorForOptional('.tm-details-summary__link');

  /** The item's words: its link or its plain text. */
  private readonly words = this.locatorFor('.tm-details-summary__link, .tm-details-summary__text');

  /** The Fixed box. */
  private readonly fixedBox = this.locatorFor('.tm-details-summary__fixed');

  /** What the item says, such as "Name: Required". */
  async getText(): Promise<string> {
    return (await (await this.words()).text()).trim();
  }

  /** Whether the item shows "Fixed": its error has cleared since the save. */
  async isFixed(): Promise<boolean> {
    return (await (await this.fixedBox()).text()).trim() !== '';
  }

  /** Whether the item is a button that goes to its field, rather than plain text. */
  async isLink(): Promise<boolean> {
    return (await this.link()) !== null;
  }

  /** Activates the item: the list collapses, and it goes to its field. Fails for an item shown as plain text. */
  async activate(): Promise<void> {
    const link = await this.link();
    if (link === null) {
      throw new Error('The error summary item is plain text, not a link.');
    }
    await link.click();
  }
}

/** Harness for the error summary in a details page's status strip. */
export class TmDetailsSummaryHarness extends ComponentHarness {
  /** The selector for the summary. */
  static hostSelector = 'tm-details-summary';

  /** The heading. */
  private readonly heading = this.locatorFor('.tm-details-summary__heading');

  /** The list's toggle. */
  private readonly toggleButton = this.locatorFor('.tm-details-summary__toggle');

  /** The heading's text, such as "Fix 2 problems to save". */
  async getHeading(): Promise<string> {
    return (await (await this.heading()).text()).trim();
  }

  /** Whether the heading has focus, as it does once a failed Save opens the summary. */
  async isHeadingFocused(): Promise<boolean> {
    return (await this.heading()).isFocused();
  }

  /** Whether the list's toggle has focus, as it does once Escape closes the list. */
  async isToggleFocused(): Promise<boolean> {
    return (await this.toggleButton()).isFocused();
  }

  /** Whether the list shows. */
  async isExpanded(): Promise<boolean> {
    return (await (await this.toggleButton()).getAttribute('aria-expanded')) === 'true';
  }

  /** Shows or hides the list, as Show problems does. */
  async toggle(): Promise<void> {
    await (await this.toggleButton()).click();
  }

  /** The listed items, in order; none while the list is hidden. */
  async getItems(): Promise<TmDetailsSummaryItemHarness[]> {
    return this.locatorForAll(TmDetailsSummaryItemHarness)();
  }

  /** The listed items' texts, in order. */
  async getItemTexts(): Promise<string[]> {
    return Promise.all((await this.getItems()).map((item) => item.getText()));
  }

  /** The line counting the problems beyond the listed ones, such as "and 2 more", or `null` when every one is listed. */
  async getMore(): Promise<string | null> {
    const more = await this.locatorForOptional('.tm-details-summary__more')();
    return more === null ? null : (await more.text()).trim();
  }

  /** Presses Escape on the heading, which collapses the list and focuses the toggle. */
  async pressEscape(): Promise<void> {
    await (await this.heading()).sendKeys(TestKey.ESCAPE);
  }
}

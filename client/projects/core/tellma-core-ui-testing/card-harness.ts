// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { ComponentHarness } from '@angular/cdk/testing';

// Every query is anchored at the host with child combinators down to the
// card's own header, heading and region, so it never reaches a card nested
// in the body.
/** The card's own header. */
const HEADER = ':scope > .tm-card__header';
/** The card's own heading row. */
const HEADING_ROW = `${HEADER} > .tm-card__heading-row`;
/** The card's own heading element. */
const HEADING = `${HEADING_ROW} > .tm-card__heading`;
/** The card's own collapsible region. */
const REGION = ':scope > .tm-card__region';

/** Harness for `tm-card`. Its queries never reach a card nested in the body. */
export class TmCardHarness extends ComponentHarness {
  /** The selector for the `tm-card` host element. */
  static hostSelector = 'tm-card';

  /** The heading as drawn, or `null` when the card has none. */
  async getHeading(): Promise<string | null> {
    const title = await this.locatorForOptional(`${HEADING} .tm-card__title`)();
    return title === null ? null : (await title.text()).trim();
  }

  /** The heading element's level (2 to 6), or `null` when the card has no heading. */
  async getHeadingLevel(): Promise<number | null> {
    for (const level of [2, 3, 4, 5, 6]) {
      if (
        (await this.locatorForOptional(`${HEADING_ROW} > h${level}.tm-card__heading`)()) !== null
      ) {
        return level;
      }
    }
    return null;
  }

  /**
   * The heading's accessible name: the heading text, or "{heading}, has
   * errors" while the card is invalid. `null` when the card has no heading.
   */
  async getHeadingName(): Promise<string | null> {
    const errorName = await this.locatorForOptional(`${HEADING} .tm-card__error-name`)();
    return errorName === null ? this.getHeading() : (await errorName.text()).trim();
  }

  /** The counter's text after the heading, or `null` when no count shows. */
  async getCount(): Promise<string | null> {
    const count = await this.locatorForOptional(`${HEADING_ROW} > .tm-card__count`)();
    return count === null ? null : (await count.text()).trim();
  }

  /** Whether the heading is a disclosure button. */
  async isCollapsible(): Promise<boolean> {
    return (await this.locatorForOptional(`${HEADING} .tm-card__toggle`)()) !== null;
  }

  /** Whether the body shows (always, for a card that cannot collapse). */
  async isExpanded(): Promise<boolean> {
    return !(await (await this.host()).hasClass('tm-card--collapsed'));
  }

  /** Clicks the disclosure button. */
  async toggle(): Promise<void> {
    return (await this.locatorFor(`${HEADING} .tm-card__toggle`)()).click();
  }

  /** Whether the card shows its error dot. */
  async isInvalid(): Promise<boolean> {
    return (await this.host()).hasClass('tm-card--invalid');
  }

  /** Whether the card reserves the error dot's box (`invalid` is bound). */
  async reservesErrorDot(): Promise<boolean> {
    return (await this.locatorForOptional(`${HEADING} .tm-card__dot`)()) !== null;
  }

  /** Whether the body is in the DOM: always while expanded, and while collapsed under `preserveContent`. */
  async isBodyRendered(): Promise<boolean> {
    return (await this.locatorForOptional(REGION)()) !== null;
  }

  /** Whether the body is in the DOM but hidden and inert (collapsed under `preserveContent`). */
  async isBodyInert(): Promise<boolean> {
    const region = await this.locatorForOptional(REGION)();
    if (region === null) {
      return false;
    }
    const [hidden, inert] = await Promise.all([
      region.getProperty<boolean>('hidden'),
      region.getAttribute('inert'),
    ]);
    return hidden && inert !== null;
  }

  /** The subtitle line's text, or `null` when the card has none. */
  async getSubtitle(): Promise<string | null> {
    const subtitle = await this.locatorFor(`${HEADER} > .tm-card__subtitle`)();
    const text = (await subtitle.text()).trim();
    return text === '' ? null : text;
  }
}

// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { ComponentHarness } from '@angular/cdk/testing';

/**
 * Harness for a `tmTooltip` host element.
 *
 * The floating surface renders in the top-layer overlay outside the host's
 * subtree, so the visibility/text queries go through the document root.
 * The DESCRIPTION, by contrast, is always available — `getDescription()`
 * works whether or not the tooltip has ever opened.
 */
export class TmTooltipHarness extends ComponentHarness {
  /**
   * The selector for an element carrying a tooltip — the directive's host
   * class, because a bound `[tmTooltip]` input stamps no DOM attribute.
   */
  static hostSelector = '.tm-tooltip-host';

  /** Hovers the host (the tooltip then shows after its delay). */
  async hover(): Promise<void> {
    return (await this.host()).hover();
  }

  /** Moves the pointer away from the host. */
  async mouseAway(): Promise<void> {
    return (await this.host()).mouseAway();
  }

  /**
   * The host's accessible description (the tooltip text as AT reads it) —
   * resolved through `aria-describedby`, available without opening.
   */
  async getDescription(): Promise<string | null> {
    const describedBy = await (await this.host()).getAttribute('aria-describedby');
    if (describedBy === null || describedBy.trim() === '') {
      return null;
    }
    const root = this.documentRootLocatorFactory();
    const parts: string[] = [];
    for (const id of describedBy.trim().split(/\s+/)) {
      const message = await root.locatorForOptional(`[id="${id}"]`)();
      if (message !== null) {
        parts.push((await message.text()).trim());
      }
    }
    return parts.length === 0 ? null : parts.join(' ');
  }

  /** Whether a tooltip surface is currently visible (any host's). */
  async isTooltipVisible(): Promise<boolean> {
    return (await this.documentRootLocatorFactory().locatorForOptional('.tm-tooltip__panel')()) !== null;
  }

  /** The visible tooltip surface's text, or null when none is shown. */
  async getTooltipText(): Promise<string | null> {
    const panel = await this.documentRootLocatorFactory().locatorForOptional('.tm-tooltip__panel')();
    return panel === null ? null : (await panel.text()).trim();
  }
}

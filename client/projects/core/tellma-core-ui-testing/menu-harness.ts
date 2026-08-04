// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { ComponentHarness, type HarnessPredicate } from '@angular/cdk/testing';

/** Harness for a single actionable `tm-menu` item row. */
export class TmMenuItemHarness extends ComponentHarness {
  /** The selector for an item row rendered in the open menu panel. */
  static hostSelector = '.tm-menu__item';

  /** Gets the item's rendered label text, trimmed. */
  async getText(): Promise<string> {
    return (await (await this.locatorFor('.tm-menu__label')()).text()).trim();
  }

  /** Whether the item is disabled (navigable, but never activates). */
  async isDisabled(): Promise<boolean> {
    return (await (await this.host()).getAttribute('aria-disabled')) === 'true';
  }

  /** Whether the item is the active (keyboard-highlighted) one. */
  async isActive(): Promise<boolean> {
    return (await (await this.host()).getAttribute('data-active')) === 'true';
  }

  /** Clicks the item row. */
  async click(): Promise<void> {
    return (await this.host()).click();
  }
}

/**
 * Harness for the `tm-menu` panel. The panel is portaled into the CDK
 * overlay and exists in the DOM only while the menu is OPEN, so this
 * harness cannot be located through the fixture's own loader: obtain it
 * from a document root loader after opening the menu —
 * `TestbedHarnessEnvironment.documentRootLoader(fixture).getHarness(TmMenuHarness)`
 * — and expect the lookup to reject while the menu is closed.
 */
export class TmMenuHarness extends ComponentHarness {
  /** The selector for the menu panel rendered in the overlay. */
  static hostSelector = '.tm-menu__panel';

  /** Gets harnesses for the actionable items, in display order (separators excluded), optionally filtered. */
  async getItems(filter?: HarnessPredicate<TmMenuItemHarness>): Promise<TmMenuItemHarness[]> {
    return this.locatorForAll(filter ?? TmMenuItemHarness)();
  }

  /** Gets the visible labels of the actionable items, in display order. */
  async getItemLabels(): Promise<string[]> {
    const items = await this.getItems();
    return Promise.all(items.map((item) => item.getText()));
  }

  /** Clicks the item with the given visible label. */
  async clickItem(label: string): Promise<void> {
    const items = await this.getItems();
    for (const item of items) {
      if ((await item.getText()) === label) {
        await item.click();
        return;
      }
    }
    throw new Error(`tm-menu has no item with label "${label}"`);
  }
}

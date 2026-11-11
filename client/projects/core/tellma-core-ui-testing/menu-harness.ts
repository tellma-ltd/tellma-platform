// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { ComponentHarness, type HarnessPredicate } from '@angular/cdk/testing';

/** Matches what sits on a menu's own level rather than inside one of its submenus. */
const OWN_LEVEL = ':not(.tm-menu__panel--sub *)';

/**
 * Harness for a single `tm-menu` row: an item, a link, or the row that
 * opens a submenu.
 */
export class TmMenuItemHarness extends ComponentHarness {
  /** The selector for a row rendered in the open menu panel. */
  static hostSelector = '.tm-menu__item';

  /** Gets the row's rendered label text, trimmed. */
  async getText(): Promise<string> {
    return (await (await this.locatorFor('.tm-menu__label')()).text()).trim();
  }

  /**
   * Gets the row's second line, trimmed: its description, or the reason it
   * is disabled. `null` when the row has one line.
   */
  async getDescription(): Promise<string | null> {
    const line = await this.locatorForOptional('.tm-menu__description')();
    return line === null ? null : (await line.text()).trim();
  }

  /** Gets the row's trailing detail text, trimmed; `null` when it has none. */
  async getDetail(): Promise<string | null> {
    const detail = await this.locatorForOptional('.tm-menu__detail')();
    return detail === null ? null : (await detail.text()).trim();
  }

  /** Whether the row is disabled (navigable, but never activates). */
  async isDisabled(): Promise<boolean> {
    return (await (await this.host()).getAttribute('aria-disabled')) === 'true';
  }

  /** Whether the row is the active (keyboard-highlighted) one. */
  async isActive(): Promise<boolean> {
    return (await (await this.host()).getAttribute('data-active')) === 'true';
  }

  /**
   * Whether a checkbox or radio row is checked; `null` when the row is not
   * checkable.
   */
  async isChecked(): Promise<boolean | null> {
    const checked = await (await this.host()).getAttribute('aria-checked');
    return checked === null ? null : checked === 'true';
  }

  /**
   * Gets where a link row goes; `null` when the row is not a link, or is a
   * disabled link (which carries no address).
   */
  async getHref(): Promise<string | null> {
    return (await this.host()).getAttribute('href');
  }

  /** Whether the row opens a submenu. */
  async hasSubmenu(): Promise<boolean> {
    return (await (await this.host()).getAttribute('aria-haspopup')) === 'true';
  }

  /** Whether the row's submenu is open. */
  async isExpanded(): Promise<boolean> {
    return (await (await this.host()).getAttribute('aria-expanded')) === 'true';
  }

  /** Clicks the row. */
  async click(): Promise<void> {
    return (await this.host()).click();
  }
}

/** Harness for one group of rows in a `tm-menu`. */
export class TmMenuGroupHarness extends ComponentHarness {
  /** The selector for a group rendered in the open menu panel. */
  static hostSelector = '.tm-menu__group';

  /** Gets the group's heading text, trimmed; `null` when it has no heading. */
  async getLabel(): Promise<string | null> {
    const heading = await this.locatorForOptional('.tm-menu__heading')();
    return heading === null ? null : (await heading.text()).trim();
  }

  /** Gets harnesses for the group's rows, in display order. */
  async getItems(): Promise<TmMenuItemHarness[]> {
    const items = await this.locatorForAll(TmMenuItemHarness)();
    // A submenu opened from a row of this group is rendered next to that
    // row, inside the group; its rows are not the group's. A group that is
    // itself in a submenu holds no deeper level.
    return (await (await this.host()).matchesSelector(OWN_LEVEL)) ? ownLevel(items) : items;
  }

  /** Gets the visible labels of the group's rows, in display order. */
  async getItemLabels(): Promise<string[]> {
    const items = await this.getItems();
    return Promise.all(items.map((item) => item.getText()));
  }
}

/**
 * Harness for a `tm-menu` panel: the menu itself, or one of its submenus
 * (see `openSubmenu`). The panel is portaled into the CDK overlay and
 * exists in the DOM only while the menu is OPEN, so this harness cannot be
 * located through the fixture's own loader: obtain it from a document root
 * loader after opening the menu —
 * `TestbedHarnessEnvironment.documentRootLoader(fixture).getHarness(TmMenuHarness)`
 * — and expect the lookup to reject while the menu is closed.
 *
 * Every method works on the panel's own level: the rows of a submenu are
 * reached through the harness `openSubmenu` returns.
 */
export class TmMenuHarness extends ComponentHarness {
  /**
   * The selector for a menu panel rendered in the overlay. An open menu's
   * submenu panels match it too, after the menu's own, so `getHarness`
   * finds the menu and `getAllHarnesses` also lists its submenus.
   */
  static hostSelector = '.tm-menu__panel';

  /** Gets harnesses for the rows of this level, in display order (separators and headings excluded), optionally filtered. */
  async getItems(filter?: HarnessPredicate<TmMenuItemHarness>): Promise<TmMenuItemHarness[]> {
    const items = await this.locatorForAll(filter ?? TmMenuItemHarness)();
    return (await this.isSubmenu()) ? items : ownLevel(items);
  }

  /** Gets the visible labels of the rows of this level, in display order. */
  async getItemLabels(): Promise<string[]> {
    const items = await this.getItems();
    return Promise.all(items.map((item) => item.getText()));
  }

  /** Gets harnesses for the groups of this level, in display order. */
  async getGroups(): Promise<TmMenuGroupHarness[]> {
    const groups = await this.locatorForAll(TmMenuGroupHarness)();
    return (await this.isSubmenu()) ? groups : ownLevel(groups);
  }

  /** Clicks the row with the given visible label. */
  async clickItem(label: string): Promise<void> {
    await (await this.getItem(label)).click();
  }

  /**
   * Opens the submenu of the row with the given visible label and returns
   * the submenu's harness. Fails when the row has no submenu, or is
   * disabled and so does not open it.
   */
  async openSubmenu(label: string): Promise<TmMenuHarness> {
    const row = await this.getItem(label);
    if (!(await row.hasSubmenu())) {
      throw new Error(`The tm-menu item "${label}" has no submenu`);
    }
    if (!(await row.isExpanded())) {
      await row.click();
    }
    if (!(await row.isExpanded())) {
      throw new Error(`The submenu of the tm-menu item "${label}" did not open`);
    }
    const listId = await (await row.host()).getAttribute('aria-controls');
    for (const panel of await this.locatorForAll(TmMenuHarness)()) {
      if ((await panel.getListId()) === listId) {
        return panel;
      }
    }
    throw new Error(`The submenu of the tm-menu item "${label}" was not found`);
  }

  /**
   * Whether the checkbox or radio row with the given visible label is
   * checked. Fails when the row is not checkable.
   */
  async isChecked(label: string): Promise<boolean> {
    const checked = await (await this.getItem(label)).isChecked();
    if (checked === null) {
      throw new Error(`The tm-menu item "${label}" is not a checkbox or radio item`);
    }
    return checked;
  }

  /**
   * Gets where the link row with the given visible label goes; `null` when
   * the row is not a link, or is a disabled link.
   */
  async getLinkHref(label: string): Promise<string | null> {
    return (await this.getItem(label)).getHref();
  }

  /**
   * Gets the reason the row with the given visible label is disabled;
   * `null` when the row is enabled, or disabled without a reason.
   */
  async getDisabledReason(label: string): Promise<string | null> {
    const row = await this.getItem(label);
    return (await row.isDisabled()) ? row.getDescription() : null;
  }

  /** Gets the header block's text, trimmed; `null` when the menu has no header. */
  async getHeaderText(): Promise<string | null> {
    const header = await this.locatorForOptional('.tm-menu__header')();
    return header === null ? null : (await header.text()).trim();
  }

  /** Gets the id of the element that carries `role="menu"` on this level. */
  async getListId(): Promise<string | null> {
    return (await this.locatorFor('.tm-menu__list')()).getAttribute('id');
  }

  /** The row of this level with the given visible label. */
  private async getItem(label: string): Promise<TmMenuItemHarness> {
    for (const item of await this.getItems()) {
      if ((await item.getText()) === label) {
        return item;
      }
    }
    throw new Error(`tm-menu has no item with label "${label}"`);
  }

  /** Whether this panel is a submenu's rather than the menu's own. */
  private async isSubmenu(): Promise<boolean> {
    return (await this.host()).hasClass('tm-menu__panel--sub');
  }
}

/**
 * Harness for a button that opens a `tm-menu` (`button[tmMenuTriggerFor]`).
 * Unlike the menu panel, the button is ordinary page content, so the
 * fixture's own loader finds it.
 */
export class TmMenuTriggerHarness extends ComponentHarness {
  /** The selector for a menu trigger button. */
  static hostSelector = '.tm-menu-trigger';

  /** Gets the button's text, trimmed. */
  async getText(): Promise<string> {
    return (await (await this.host()).text()).trim();
  }

  /** Whether the menu is open from this button. */
  async isOpen(): Promise<boolean> {
    return (await (await this.host()).getAttribute('aria-expanded')) === 'true';
  }

  /** Opens the menu from this button, if it is not open already. */
  async open(): Promise<void> {
    if (!(await this.isOpen())) {
      await (await this.host()).click();
    }
  }

  /** Closes the menu, if this button has it open. */
  async close(): Promise<void> {
    if (await this.isOpen()) {
      await (await this.host()).click();
    }
  }

  /** Clicks the button, toggling its menu. */
  async toggle(): Promise<void> {
    await (await this.host()).click();
  }

  /**
   * Gets the harness of the menu this button has open. Fails while the
   * menu is closed.
   */
  async getMenu(): Promise<TmMenuHarness> {
    const listId = await (await this.host()).getAttribute('aria-controls');
    if (listId !== null) {
      const panels = await this.documentRootLocatorFactory().locatorForAll(TmMenuHarness)();
      for (const panel of panels) {
        if ((await panel.getListId()) === listId) {
          return panel;
        }
      }
    }
    throw new Error('The tm-menu trigger has no open menu');
  }
}

/** Keeps the harnesses whose element is not inside a submenu panel. */
async function ownLevel<T extends ComponentHarness>(harnesses: readonly T[]): Promise<T[]> {
  const own = await Promise.all(
    harnesses.map(async (harness) => (await harness.host()).matchesSelector(OWN_LEVEL)),
  );
  return harnesses.filter((_, index) => own[index]);
}

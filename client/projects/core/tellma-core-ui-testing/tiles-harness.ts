// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import {
  ComponentHarness,
  HarnessPredicate,
  TestKey,
  type BaseHarnessFilters,
  type ModifierKeys,
} from '@angular/cdk/testing';

/** The keys tiles answer to. */
export type TmTilesKey =
  | 'ArrowUp'
  | 'ArrowDown'
  | 'ArrowLeft'
  | 'ArrowRight'
  | 'Home'
  | 'End'
  | 'PageUp'
  | 'PageDown'
  | 'Enter'
  | 'Space';

/** The CDK test keys for each tiles key. */
const TEST_KEYS: Record<TmTilesKey, TestKey | string> = {
  ArrowUp: TestKey.UP_ARROW,
  ArrowDown: TestKey.DOWN_ARROW,
  ArrowLeft: TestKey.LEFT_ARROW,
  ArrowRight: TestKey.RIGHT_ARROW,
  Home: TestKey.HOME,
  End: TestKey.END,
  PageUp: TestKey.PAGE_UP,
  PageDown: TestKey.PAGE_DOWN,
  Enter: TestKey.ENTER,
  Space: ' ',
};

/** A tile's text corners. */
export type TmTileCorner = 'topEnd' | 'bottomStart' | 'bottomEnd';

/** The class of each corner's element. */
const CORNER_CLASSES: Record<TmTileCorner, string> = {
  topEnd: '.tm-tiles__corner--top-end',
  bottomStart: '.tm-tiles__corner--bottom-start',
  bottomEnd: '.tm-tiles__corner--bottom-end',
};

/** Filters for {@link TmTileHarness}. */
export interface TmTileHarnessFilters extends BaseHarnessFilters {
  /** The tile's title, or a pattern it matches. */
  title?: string | RegExp;
  /** The tile's position among the shown rows, from 0. */
  index?: number;
}

/** Harness for one rendered tile of a `tm-tiles`: its corners, state, checkbox and keys. */
export class TmTileHarness extends ComponentHarness {
  /** The selector for a tile's cell (skeleton tiles excluded). */
  static hostSelector = '.tm-tiles__item[data-index]';

  /** Gets a predicate that finds tiles by title or position. */
  static with(options: TmTileHarnessFilters = {}): HarnessPredicate<TmTileHarness> {
    return new HarnessPredicate(TmTileHarness, options)
      .addOption('title', options.title, (harness, title) =>
        HarnessPredicate.stringMatches(harness.getTitle(), title),
      )
      .addOption(
        'index',
        options.index,
        async (harness, index) => (await harness.getIndex()) === index,
      );
  }

  /** The title element. */
  private readonly title = this.locatorForOptional('.tm-tiles__title');
  /** The title link, when the layout gives an `href`. */
  private readonly link = this.locatorForOptional('a.tm-tiles__title[href]');
  /** The reserved checkbox. */
  private readonly checkbox = this.locatorForOptional('[role="checkbox"]');
  /** The tree twisty. */
  private readonly twisty = this.locatorForOptional('.tm-tiles__twisty');

  /** The tile's position among the shown rows, from 0. */
  async getIndex(): Promise<number> {
    return Number(await (await this.host()).getAttribute('data-index'));
  }

  /** The title's text, trimmed; empty with a custom body. */
  async getTitle(): Promise<string> {
    const title = await this.title();
    return title === null ? '' : (await title.text()).trim();
  }

  /** A corner's text, trimmed, or `null` when the corner is empty. */
  async getCornerText(corner: TmTileCorner): Promise<string | null> {
    const element = await this.locatorForOptional(CORNER_CLASSES[corner])();
    return element === null ? null : (await element.text()).trim();
  }

  /** Whether a corner shows its text as a badge. */
  async hasCornerBadge(corner: TmTileCorner): Promise<boolean> {
    return (await this.locatorForOptional(`${CORNER_CLASSES[corner]} tm-badge`)()) !== null;
  }

  /** The title link's URL, or `null` without one. */
  async getHref(): Promise<string | null> {
    const link = await this.link();
    return link === null ? null : link.getAttribute('href');
  }

  /** Whether the tile is the active one. */
  async isActive(): Promise<boolean> {
    return (await this.host()).hasClass('tm-tiles__item--active');
  }

  /** Whether the tile is the list's tab stop. */
  async isTabStop(): Promise<boolean> {
    return (await (await this.host()).getAttribute('tabindex')) === '0';
  }

  /** Whether the tile has focus. */
  async isFocused(): Promise<boolean> {
    return (await this.host()).isFocused();
  }

  /** Whether the tile is selected. */
  async isSelected(): Promise<boolean> {
    return (await (await this.host()).getAttribute('aria-selected')) === 'true';
  }

  /** Whether the tile carries a class (a `rowClass` value). */
  async hasClass(name: string): Promise<boolean> {
    return (await this.host()).hasClass(name);
  }

  /** The checkbox's accessible name, or `null` when the tiles are not selectable. */
  async getCheckboxLabel(): Promise<string | null> {
    const checkbox = await this.checkbox();
    return checkbox === null ? null : checkbox.getAttribute('aria-label');
  }

  /** The tree depth: 1 for a root (and for every flat tile). */
  async getLevel(): Promise<number> {
    const level = Number(await (await this.host()).getCssValue('--tm-tiles-level'));
    return Number.isFinite(level) && level > 0 ? level : 1;
  }

  /** Whether the tile's row can expand (tree mode). */
  async isExpandable(): Promise<boolean> {
    return (await this.locatorForOptional('.tm-tiles__twisty tm-icon')()) !== null;
  }

  /** Whether the tile's row is expanded (tree mode). */
  async isExpanded(): Promise<boolean> {
    const twisty = await this.twisty();
    return twisty !== null && twisty.hasClass('tm-tiles__twisty--open');
  }

  /** Whether the tile's row shows the children-loading spinner. */
  async isLoading(): Promise<boolean> {
    return (await this.locatorForOptional('.tm-tiles__spinner-slot tm-spinner')()) !== null;
  }

  /** Clicks the tile's body: opens it (with Mod, in a new tab). */
  async click(modifiers: ModifierKeys = {}): Promise<void> {
    const title = await this.title();
    return (title ?? (await this.host())).click(modifiers);
  }

  /** Clicks the checkbox, toggling the tile's selection. */
  async toggleSelected(): Promise<void> {
    const checkbox = await this.checkbox();
    if (checkbox === null) {
      throw new Error('The tiles are not selectable.');
    }
    return checkbox.click();
  }

  /** Clicks the twisty, expanding or collapsing the tile's row. */
  async toggle(): Promise<void> {
    const twisty = await this.twisty();
    if (twisty === null) {
      throw new Error('The tiles are not a tree.');
    }
    return twisty.click();
  }

  /** Focuses the tile. */
  async focus(): Promise<void> {
    return (await this.host()).focus();
  }

  /** Presses a key on the tile, with modifiers. */
  async press(key: TmTilesKey, modifiers: ModifierKeys = {}): Promise<void> {
    return (await this.host()).sendKeys(modifiers, TEST_KEYS[key]);
  }
}

/** Harness for `tm-tiles`: the grid's shape and state, and its rendered tiles. */
export class TmTilesHarness extends ComponentHarness {
  /** The selector for the `tm-tiles` host element. */
  static hostSelector = 'tm-tiles';

  /** The grid (or treegrid) element. */
  private readonly grid = this.locatorFor('.tm-tiles__scroller');

  /** The list's accessible name. */
  async getLabel(): Promise<string | null> {
    return (await this.grid()).getAttribute('aria-label');
  }

  /** Whether the tiles are a tree (`role="treegrid"`). */
  async isTree(): Promise<boolean> {
    return (await (await this.grid()).getAttribute('role')) === 'treegrid';
  }

  /** The layout: `lattice`, `list` (narrow) or `tree`. */
  async getLayout(): Promise<string | null> {
    return (await this.host()).getAttribute('data-layout');
  }

  /** The tiles per lattice line (`aria-colcount`). */
  async getColumnCount(): Promise<number> {
    return Number(await (await this.grid()).getAttribute('aria-colcount'));
  }

  /** The lattice lines over every shown row (`aria-rowcount`). */
  async getLineCount(): Promise<number> {
    return Number(await (await this.grid()).getAttribute('aria-rowcount'));
  }

  /** Whether the list is `aria-busy` (loading). */
  async isBusy(): Promise<boolean> {
    return (await (await this.grid()).getAttribute('aria-busy')) === 'true';
  }

  /** Whether skeleton tiles stand in for the first data. */
  async isShowingSkeleton(): Promise<boolean> {
    return (await this.locatorForOptional('.tm-tiles__item--skeleton')()) !== null;
  }

  /** The empty layer's text, or `null` when the tiles are not empty. */
  async getEmptyText(): Promise<string | null> {
    const empty = await this.locatorForOptional('[data-tm-empty]')();
    return empty === null ? null : (await empty.text()).trim();
  }

  /** The rendered tiles, in document order, filtered by title or position. */
  async getTiles(filter: TmTileHarnessFilters = {}): Promise<TmTileHarness[]> {
    return this.locatorForAll(TmTileHarness.with(filter))();
  }

  /** The first rendered tile matching a filter. */
  async getTile(filter: TmTileHarnessFilters): Promise<TmTileHarness> {
    return this.locatorFor(TmTileHarness.with(filter))();
  }

  /** The rendered tiles' titles, in document order. */
  async getTileTitles(): Promise<string[]> {
    const tiles = await this.getTiles();
    return Promise.all(tiles.map((tile) => tile.getTitle()));
  }

  /** The active tile, if one is rendered. */
  async getActiveTile(): Promise<TmTileHarness | null> {
    for (const tile of await this.getTiles()) {
      if (await tile.isActive()) {
        return tile;
      }
    }
    return null;
  }

  /** Presses a key on the list's tab stop, focusing it first. */
  async press(key: TmTilesKey, modifiers: ModifierKeys = {}): Promise<void> {
    const tiles = await this.getTiles();
    for (const tile of tiles) {
      if (await tile.isTabStop()) {
        await tile.focus();
        return tile.press(key, modifiers);
      }
    }
    throw new Error('The tiles have no tile to take the key.');
  }
}

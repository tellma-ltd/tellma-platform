// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { type BaseHarnessFilters, ComponentHarness, HarnessPredicate } from '@angular/cdk/testing';
import { TmCardHarness, TmEmptyStateHarness, TmMenuHarness } from '@tellma/core-ui-testing';

// A dashboard grid and its tiles, as a test reads them: the grid's name and
// its tiles in reading order; each tile's title, count, status line, body
// state, list rows, header link and menu, and its place in both layouts and
// its box on the page, which a layout test compares with the skeleton's.

/** A tile's place on the grid, as its custom properties give it. */
export interface TmDashboardTilePlacement {
  /** The first column, from 1, in the wide layout. */
  readonly col: number;
  /** The columns it spans in the wide layout. */
  readonly span: number;
  /** The first row, from 1, in the wide layout. */
  readonly row: number;
  /** The rows it spans, in both layouts. */
  readonly rows: number;
  /** The first row, from 1, in the narrow layout. */
  readonly rowNarrow: number;
}

/** A box on the page, in CSS pixels. */
export interface TmDashboardBox {
  /** The distance from the viewport's top. */
  readonly top: number;
  /** The distance from the viewport's left. */
  readonly left: number;
  /** The width. */
  readonly width: number;
  /** The height. */
  readonly height: number;
}

/** One assignment row of a home list, which reads its two parts. */
class TmListRowHarness extends ComponentHarness {
  /** The selector of a row. */
  static hostSelector = '[data-tm-row]';

  /** The row's title. */
  readonly #title = this.locatorFor('.tm-attention-row__title');

  /** The row's second line, when it has one. */
  readonly #line = this.locatorForOptional('.tm-attention-row__line');

  /**
   * The row's title and second line, joined by a middle dot.
   *
   * @returns The text.
   */
  async getText(): Promise<string> {
    const fold = (text: string): string => text.replace(/\s+/g, ' ').trim();
    const line = await this.#line();
    const title = fold(await (await this.#title()).text());
    return line === null ? title : `${title} · ${fold(await line.text())}`;
  }
}

/** What a tile's harness can be filtered by. */
export interface TmDashboardTileHarnessFilters extends BaseHarnessFilters {
  /** The tile's title, exactly or as a pattern. */
  readonly title?: string | RegExp;
  /** The widget's key. */
  readonly key?: string;
}

/**
 * Harness for one `tm-dashboard-tile`.
 *
 * @example
 * ```ts
 * const tile = await grid.getTile({ title: 'My assignments' });
 * expect(await tile.getCount()).toBe('2');
 * expect(await tile.getRows()).toEqual(['HQ-01 Please review']);
 * ```
 */
export class TmDashboardTileHarness extends ComponentHarness {
  /** The selector of the `tm-dashboard-tile` element. */
  static hostSelector = 'tm-dashboard-tile';

  /** The tile's card. */
  readonly #card = this.locatorFor(TmCardHarness);

  /** The tile's section. */
  readonly #section = this.locatorFor('section');

  /** The state that stands in for the body, when one does. */
  readonly #empty = this.locatorForOptional(TmEmptyStateHarness);

  /** The status line, for a type that shows one. */
  readonly #status = this.locatorForOptional('.tm-dashboard-tile__status');

  /** The options button. */
  readonly #options = this.locatorForOptional('.tm-dashboard-tile__options');

  /** A list's "View all". */
  readonly #viewAll = this.locatorForOptional('.tm-dashboard-tile__view-all');

  /** The recent pages' links. */
  readonly #links = this.locatorForAll('.tm-recents-widget__link');

  /** The skeleton rows' bars. */
  readonly #bars = this.locatorForAll('.tm-dashboard-frame__rows .tm-skeleton__bar');

  /**
   * A predicate that finds a tile by its title or key.
   *
   * @param options - The filters.
   * @returns The predicate.
   */
  static with(options: TmDashboardTileHarnessFilters = {}): HarnessPredicate<TmDashboardTileHarness> {
    return new HarnessPredicate(TmDashboardTileHarness, options)
      .addOption('title', options.title, (harness, title) => HarnessPredicate.stringMatches(harness.getTitle(), title))
      .addOption('key', options.key, async (harness, key) => (await harness.getKey()) === key);
  }

  /**
   * The widget's key.
   *
   * @returns The key.
   */
  async getKey(): Promise<string> {
    return (await (await this.host()).getAttribute('data-tile')) ?? '';
  }

  /**
   * The tile's title.
   *
   * @returns The heading's text.
   */
  async getTitle(): Promise<string> {
    return (await (await this.#card()).getHeading()) ?? '';
  }

  /**
   * The count beside a list's title.
   *
   * @returns The count's text, or `null` when the header shows none.
   */
  getCount(): Promise<string | null> {
    return this.#card().then((card) => card.getCount());
  }

  /**
   * Whether the tile reads as busy: its body's chunk, or its first run, is loading.
   *
   * @returns Whether its section is `aria-busy`.
   */
  async isBusy(): Promise<boolean> {
    return (await (await this.#section()).getAttribute('aria-busy')) === 'true';
  }

  /**
   * The heading of the state that stands in for the body: not available, no
   * access, a failed load, or a list's empty or offline state.
   *
   * @returns The heading, or `null` while the body shows its content or its skeleton.
   */
  async getState(): Promise<string | null> {
    return (await this.#empty())?.getHeading() ?? null;
  }

  /**
   * The status line's text: the description, the time and the omitted-totals
   * note, joined by the separator the line draws between them.
   *
   * @returns The text, empty while it holds only its skeleton bar or after a failed first run; `null` for a type that shows none.
   */
  async getStatusLine(): Promise<string | null> {
    if ((await this.#status()) === null) {
      return null;
    }
    const parts = await this.locatorForAll(
      '.tm-dashboard-tile__description',
      '.tm-dashboard-tile__status time',
      '.tm-dashboard-tile__updated > span',
    )();
    return (await Promise.all(parts.map(async (part) => (await part.text()).trim()))).join(' · ');
  }

  /**
   * Whether the status line holds its place with a bar, before the first result.
   *
   * @returns Whether it shows the bar.
   */
  async isStatusPending(): Promise<boolean> {
    return (await this.locatorForOptional('.tm-dashboard-tile__status tm-skeleton')()) !== null;
  }

  /**
   * Whether the status line marks the shown numbers stale.
   *
   * @returns Whether a failed run follows the shown result.
   */
  async isStale(): Promise<boolean> {
    return (await this.locatorForOptional('.tm-dashboard-tile__stale')()) !== null;
  }

  /**
   * Where a list's "View all" leads.
   *
   * @returns The link's `href`, or `null` when the header shows none.
   */
  async getViewAllHref(): Promise<string | null> {
    return (await this.#viewAll())?.getAttribute('href') ?? null;
  }

  /**
   * Follows a list's "View all".
   */
  async viewAll(): Promise<void> {
    const link = await this.#viewAll();
    if (link === null) {
      throw new Error('The tile shows no "View all".');
    }
    await link.click();
  }

  /**
   * Whether the header shows the options menu's button.
   *
   * @returns Whether it does.
   */
  async hasMenu(): Promise<boolean> {
    return (await this.#options()) !== null;
  }

  /**
   * Opens the options menu.
   *
   * @returns The menu.
   */
  async openMenu(): Promise<TmMenuHarness> {
    const options = await this.#options();
    if (options === null) {
      throw new Error('The tile shows no options menu.');
    }
    await options.click();
    return this.documentRootLocatorFactory().locatorFor(TmMenuHarness)();
  }

  /**
   * Refreshes the tile through its menu.
   */
  async refresh(): Promise<void> {
    await (await this.openMenu()).clickItem('Refresh');
  }

  /**
   * A built-in list's rows: an assignment row's title and second line,
   * joined by a middle dot, or a recent page's title.
   *
   * @returns The rows, in order.
   */
  async getRows(): Promise<string[]> {
    const assignments = await this.locatorForAll(TmListRowHarness)();
    if (assignments.length > 0) {
      return Promise.all(assignments.map((row) => row.getText()));
    }
    return Promise.all((await this.#links()).map(async (link) => (await link.text()).replace(/\s+/g, ' ').trim()));
  }

  /**
   * The skeleton rows the tile shows, while its body or its list loads.
   *
   * @returns How many.
   */
  async getSkeletonRows(): Promise<number> {
    return (await this.#bars()).length;
  }

  /**
   * The tile's place on the grid.
   *
   * @returns Its place in both layouts.
   */
  async getPlacement(): Promise<TmDashboardTilePlacement> {
    const host = await this.host();
    const read = async (name: string): Promise<number> => Number(await host.getCssValue(name));
    return {
      col: await read('--tm-col'),
      span: await read('--tm-span'),
      row: await read('--tm-row'),
      rows: await read('--tm-rows'),
      rowNarrow: await read('--tm-row-narrow'),
    };
  }

  /**
   * The tile's box on the page.
   *
   * @returns The box.
   */
  async getBox(): Promise<TmDashboardBox> {
    const { top, left, width, height } = await (await this.host()).getDimensions();
    return { top, left, width, height };
  }
}

/**
 * Harness for `tm-dashboard-grid`: its accessible name and its tiles, in
 * reading order.
 *
 * @example
 * ```ts
 * const grid = await loader.getHarness(TmDashboardGridHarness);
 * expect(await grid.getTitles()).toEqual(['My assignments', 'Recently visited', 'Assigned by me']);
 * ```
 */
export class TmDashboardGridHarness extends ComponentHarness {
  /** The selector of the `tm-dashboard-grid` element. */
  static hostSelector = 'tm-dashboard-grid';

  /**
   * The grid's accessible name.
   *
   * @returns The region's label.
   */
  async getLabel(): Promise<string | null> {
    return (await this.host()).getAttribute('aria-label');
  }

  /**
   * The grid's tiles, in reading order.
   *
   * @param filters - What the tiles must match.
   * @returns The tiles.
   */
  getTiles(filters: TmDashboardTileHarnessFilters = {}): Promise<TmDashboardTileHarness[]> {
    return this.locatorForAll(TmDashboardTileHarness.with(filters))();
  }

  /**
   * One tile.
   *
   * @param filters - What the tile must match.
   * @returns The first tile that matches.
   */
  getTile(filters: TmDashboardTileHarnessFilters): Promise<TmDashboardTileHarness> {
    return this.locatorFor(TmDashboardTileHarness.with(filters))();
  }

  /**
   * The tiles' titles, in reading order.
   *
   * @returns The titles.
   */
  async getTitles(): Promise<string[]> {
    return Promise.all((await this.getTiles()).map((tile) => tile.getTitle()));
  }
}

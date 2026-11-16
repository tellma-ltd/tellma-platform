// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { expect, test, type Locator, type Page } from '@playwright/test';

import { expectNoAxeViolations } from '../support/axe';
import {
  activeCell,
  cell,
  centerOf,
  checkAllBox,
  colHeader,
  dragBetween,
  gotoGrid,
  gridScroller,
  liveRegion,
  rowCheckbox,
  rowHeader,
  selectedCells,
  setScrollTop,
} from '../support/grid';
import { appStable } from '../support/settle';

/**
 * The search-page battery against the
 * `grid-search-page` story: a search field driving a readonly list whose
 * rows open on click or Enter, sortable headers editing a two-way sort,
 * cross-page checkbox selection over a paged source, row classes, and a
 * list without row numbers — in both directions, with axe in both themes.
 *
 * Story shape: 5 data columns (0 code, 1 name — a real link, 2 qty,
 * 3 region, 4 manager), the first four sortable, region and manager with a
 * filter action in their headers, and a row-end column whose header holds
 * the column editor; 230 rows in pages of 50,
 * unsorted row i having id i; `selectable`, `rowActivation="row"`,
 * `[rowHeaders]="false"`. Readouts: `activated` ("<id> via <pointer|
 * keyboard>[ in a new tab]"), `activation-count`, `active-row`, `order`
 * ("<key> <asc|desc>" or "none"), `selected-count` ("N selected") and
 * `link-clicked`. Toggles under the grid (`toggle-row-numbers`,
 * `toggle-checkboxes`) switch the two chrome columns, and with them the
 * row's first painted cell; `flash-active` flashes the active row in place
 * (`scrollToRow` with `activate: false`).
 */

/** The story's search field (the `tmGridDriver` input). */
function search(page: Page): Locator {
  return page.getByTestId('search');
}

/** The rendered row element at a view-space row index. */
function row(page: Page, viewIndex: number): Locator {
  return page.locator(`.tm-grid__row[aria-rowindex="${viewIndex + 2}"]`);
}

/** The Name link inside the cell at a view-space row. */
function nameLink(page: Page, viewIndex: number): Locator {
  return cell(page, viewIndex, 1).locator('a.item-link');
}

/** The library context menu's panel. */
function menuPanel(page: Page): Locator {
  return page.locator('.tm-menu__panel');
}

/** Resolves a color custom property to its computed `rgb()` form inside the grid. */
async function resolvedColor(page: Page, property: string): Promise<string> {
  return page.locator('tm-grid').evaluate((grid, name) => {
    const probe = document.createElement('div');
    probe.style.backgroundColor = `var(${name})`;
    grid.append(probe);
    const color = getComputedStyle(probe).backgroundColor;
    probe.remove();
    return color;
  }, property);
}

/** Resolves a system color to its computed `rgb()` form under the page's current palette. */
async function systemColor(page: Page, name: string): Promise<string> {
  return page.evaluate((color) => {
    const probe = document.createElement('div');
    // Kept as authored, so the probe reads the keyword's own value.
    probe.style.forcedColorAdjust = 'none';
    probe.style.backgroundColor = color;
    document.body.append(probe);
    const resolved = getComputedStyle(probe).backgroundColor;
    probe.remove();
    return resolved;
  }, name);
}

/** The computed paint of a row's first painted cell and of the accent bar on it. */
interface BarPaint {
  /** The first cell's chrome kind: its `data-tm-*` marker. */
  readonly first: 'rowhdr' | 'checkcell' | 'cell' | 'other';
  /** The accent bar's color. */
  readonly bar: string;
  /** The accent bar's inline size. */
  readonly barWidth: string;
  /** The first cell's background color. */
  readonly background: string;
  /** The first cell's ink. */
  readonly ink: string;
}

/** Reads a row's first painted cell and its accent bar once the app is stable. */
async function barPaint(page: Page, viewIndex: number): Promise<BarPaint> {
  await appStable(page);
  return row(page, viewIndex)
    .locator(':scope > :first-child')
    .evaluate((first: HTMLElement): BarPaint => {
      const style = getComputedStyle(first);
      const bar = getComputedStyle(first, '::before');
      const data = first.dataset;
      return {
        first:
          'tmRowhdr' in data
            ? 'rowhdr'
            : 'tmCheckcell' in data
              ? 'checkcell'
              : 'tmCell' in data
                ? 'cell'
                : 'other',
        bar: bar.backgroundColor,
        barWidth: bar.width,
        background: style.backgroundColor,
        ink: style.color,
      };
    });
}

/** Lets the 400 ms same-row guard lapse, so the next activation of a row counts. */
async function guardLapse(page: Page): Promise<void> {
  await page.waitForTimeout(450);
}

for (const dir of ['ltr', 'rtl'] as const) {
  test.describe(`search page (${dir})`, () => {
    test.beforeEach(async ({ page }) => {
      await gotoGrid(page, 'grid-search-page', { dir });
    });

    test.describe('the keyboard driver', () => {
      test('arrows and paging move the active row while focus stays in the field', async ({
        page,
      }) => {
        await search(page).click();
        await expect(search(page)).toHaveAttribute(
          'aria-controls',
          (await gridScroller(page).getAttribute('id'))!,
        );
        await expect(search(page)).not.toHaveAttribute('aria-activedescendant', /./);

        // With no active row, ArrowDown starts at the first row.
        await page.keyboard.press('ArrowDown');
        await expect(page.getByTestId('active-row')).toHaveText('0');
        await expect(search(page)).toHaveAttribute(
          'aria-activedescendant',
          (await row(page, 0).getAttribute('id'))!,
        );
        await expect(row(page, 0)).toHaveClass(/tm-grid__row--active/);

        await page.keyboard.press('ArrowDown');
        await page.keyboard.press('ArrowDown');
        await page.keyboard.press('ArrowUp');
        await expect(page.getByTestId('active-row')).toHaveText('1');
        await expect(search(page)).toBeFocused();

        // PageDown moves by the visible row count and scrolls the row into view.
        await page.keyboard.press('PageDown');
        await expect(page.getByTestId('active-row')).not.toHaveText('1');
        const paged = Number(await page.getByTestId('active-row').textContent());
        expect(paged).toBeGreaterThan(5);
        await expect(row(page, paged)).toBeInViewport();
        await page.keyboard.press('PageUp');
        await expect(page.getByTestId('active-row')).toHaveText('1');
        await expect(search(page)).toBeFocused();

        // Clamped at the ends; never wraps.
        await page.keyboard.press('ArrowUp');
        await page.keyboard.press('ArrowUp');
        await expect(page.getByTestId('active-row')).toHaveText('0');
      });

      test('Enter opens the active row; Mod+Enter opens it in a new tab', async ({ page }) => {
        await search(page).click();
        // No active row: Enter is the field's own.
        await page.keyboard.press('Enter');
        await expect(page.getByTestId('activation-count')).toHaveText('0');

        await page.keyboard.press('ArrowDown');
        await page.keyboard.press('ArrowDown');
        await page.keyboard.press('Enter');
        await expect(page.getByTestId('activated')).toHaveText('1 via keyboard');

        await page.keyboard.press('ArrowDown');
        await page.keyboard.press('ControlOrMeta+Enter');
        await expect(page.getByTestId('activated')).toHaveText('2 via keyboard in a new tab');
        await expect(page.getByTestId('activation-count')).toHaveText('2');
        await expect(search(page)).toBeFocused();
      });

      test('an Enter queued right behind the arrows opens the row they reached', async ({
        page,
      }) => {
        await search(page).click();
        // Keys queued behind a busy page are dispatched in one go, before the
        // next render: the same keydowns, sent in one task.
        await search(page).evaluate((input) => {
          for (const key of ['ArrowDown', 'ArrowDown', 'Enter']) {
            input.dispatchEvent(
              new KeyboardEvent('keydown', { key, bubbles: true, cancelable: true }),
            );
          }
        });
        await expect(page.getByTestId('activated')).toHaveText('1 via keyboard');
        await expect(page.getByTestId('activation-count')).toHaveText('1');
        await expect(page.getByTestId('active-row')).toHaveText('1');
      });

      test('characters, Home, End and the horizontal arrows stay with the field', async ({
        page,
      }) => {
        await search(page).click();
        await page.keyboard.press('ArrowDown');
        await page.keyboard.type('Item 12');
        await expect(search(page)).toHaveValue('Item 12');
        // The search re-ran: Item 12 and Item 120–129.
        await expect(page.getByTestId('page')).toHaveText('Page 1 of 1');
        await expect(gridScroller(page)).toHaveAttribute('aria-rowcount', '12');

        await page.keyboard.press('Home');
        await page.keyboard.press('ArrowRight');
        await page.keyboard.press('End');
        expect(await search(page).evaluate((input: HTMLInputElement) => input.selectionStart)).toBe(
          7,
        );
        await expect(search(page)).toBeFocused();
      });
    });

    test.describe('pointer activation', () => {
      test('a click opens the row on release; a double-click opens it once', async ({ page }) => {
        await cell(page, 3, 0).click();
        await expect(page.getByTestId('activated')).toHaveText('3 via pointer');
        await expect(page.getByTestId('active-row')).toHaveText('3');
        await expect(activeCell(page)).toHaveAttribute('data-row', '3');

        await guardLapse(page);
        await cell(page, 5, 2).dblclick();
        await expect(page.getByTestId('activated')).toHaveText('5 via pointer');
        await expect(page.getByTestId('activation-count')).toHaveText('2');
      });

      test('Mod+click and a middle click open in a new tab', async ({ page }) => {
        await cell(page, 2, 3).click({ modifiers: ['ControlOrMeta'] });
        await expect(page.getByTestId('activated')).toHaveText('2 via pointer in a new tab');

        await cell(page, 4, 0).click({ button: 'middle' });
        await expect(page.getByTestId('activated')).toHaveText('4 via pointer in a new tab');
        await expect(page.getByTestId('activation-count')).toHaveText('2');
      });

      test('a drag, a Shift+click, the checkbox and a cell link never open the row', async ({
        page,
      }) => {
        await dragBetween(page, cell(page, 1, 0), cell(page, 3, 2));
        await expect(page.locator('[data-tm-cell][aria-selected="true"]')).toHaveCount(9);
        await cell(page, 6, 0).click({ modifiers: ['Shift'] });
        await rowCheckbox(page, 2).click();
        await expect(page.getByTestId('selected-count')).toHaveText('1 selected');

        await nameLink(page, 7).click();
        await expect(page.getByTestId('link-clicked')).toHaveText('7');
        await expect(page.getByTestId('activation-count')).toHaveText('0');
      });

      test('the active-row class lands in the press frame', async ({ page }) => {
        // A rAF scheduled from the press (after the grid's own handler) runs
        // before that frame paints: the row must already carry the class.
        await page.evaluate(() => {
          document.addEventListener(
            'pointerdown',
            () => {
              requestAnimationFrame(() => {
                const active = document.querySelector('.tm-grid__row--active');
                (window as unknown as { probe: string | null }).probe =
                  active?.getAttribute('aria-rowindex') ?? null;
              });
            },
            { once: true },
          );
        });
        const point = await centerOf(cell(page, 4, 2));
        await page.mouse.move(point.x, point.y);
        await page.mouse.down();
        await expect
          .poll(() => page.evaluate(() => (window as unknown as { probe?: string | null }).probe))
          .toBe('6');
        await page.mouse.up();
        await expect(page.getByTestId('activated')).toHaveText('4 via pointer');
      });

      test('the active row paints a tint and an inline-start accent bar', async ({ page }) => {
        await cell(page, 2, 1).click({ position: { x: 4, y: 4 } });
        const tint = await resolvedColor(page, '--grid-active-row-bg');
        await expect(cell(page, 2, 3)).toHaveCSS('background-color', tint);
        await expect(cell(page, 2, 3)).toHaveCSS('cursor', 'pointer');
        await expect(cell(page, 1, 3)).not.toHaveCSS('background-color', tint);

        // The bar sits on the first painted cell (the checkbox column here),
        // at the inline start: the left in LTR, the right in RTL.
        const bar = await row(page, 2)
          .locator(':scope > :first-child')
          .evaluate((first) => {
            const style = getComputedStyle(first, '::before');
            return { width: style.width, left: style.left, right: style.right };
          });
        expect(bar.width).toBe('2px');
        expect(dir === 'ltr' ? bar.left : bar.right).toBe('0px');

        // The focus ring stays on the active cell.
        await expect(activeCell(page)).toHaveAttribute('data-col', '1');
        await expect(activeCell(page)).toBeFocused();
      });

      test('the scroller describes the row-activation keys', async ({ page }) => {
        const hintId = await gridScroller(page).getAttribute('aria-describedby');
        expect(hintId).toBeTruthy();
        await expect(page.locator(`[id="${hintId}"]`)).toHaveText(
          'Press Enter to open a row. Press F2 to reach a link in a cell.',
        );
      });
    });

    test.describe('the consumer-written active row', () => {
      test('scrolls the row into view without moving focus', async ({ page }) => {
        await page.getByTestId('jump').click();
        await expect(page.getByTestId('active-row')).toHaveText('40');
        await expect(row(page, 40)).toHaveClass(/tm-grid__row--active/);
        await expect(cell(page, 40, 0)).toBeInViewport();
        await expect(page.getByTestId('jump')).toBeFocused();
      });

      test('keys queued right behind the write start from the written row', async ({ page }) => {
        await search(page).click();
        // The page seeds the active row and the user's keys arrive before the
        // render that would apply it: the write and the keydowns, in one task.
        await search(page).evaluate((input) => {
          document.querySelector<HTMLButtonElement>('[data-testid="jump"]')!.click();
          for (const key of ['ArrowDown', 'Enter']) {
            input.dispatchEvent(
              new KeyboardEvent('keydown', { key, bubbles: true, cancelable: true }),
            );
          }
        });
        await expect(page.getByTestId('activated')).toHaveText('41 via keyboard');
        await expect(page.getByTestId('activation-count')).toHaveText('1');
        await expect(page.getByTestId('active-row')).toHaveText('41');
        await expect(row(page, 41)).toHaveClass(/tm-grid__row--active/);
        await expect(search(page)).toBeFocused();
      });
    });

    test.describe('links keep the browser menu', () => {
      test('a right-click on a cell link is left to the browser; elsewhere the grid menu opens', async ({
        page,
      }) => {
        // Records whether anything prevented the native menu, after every
        // handler had its turn.
        await page.evaluate(() => {
          window.addEventListener('contextmenu', (event) => {
            (window as unknown as { prevented: boolean }).prevented = event.defaultPrevented;
          });
        });
        const prevented = (): Promise<boolean> =>
          page.evaluate(() => (window as unknown as { prevented: boolean }).prevented);

        await nameLink(page, 3).click({ button: 'right' });
        expect(await prevented()).toBe(false);
        await expect(activeCell(page)).toHaveAttribute('data-row', '3');
        await expect(activeCell(page)).toHaveAttribute('data-col', '1');
        await expect(menuPanel(page)).toHaveCount(0);
        await page.keyboard.press('Escape');

        await cell(page, 5, 3).click({ button: 'right' });
        await expect(menuPanel(page)).toBeVisible();
        expect(await prevented()).toBe(true);
        await page.keyboard.press('Escape');
        await expect(page.getByTestId('activation-count')).toHaveText('0');
      });
    });

    test.describe('sorting', () => {
      test('a header click cycles ascending, descending and none, and announces it', async ({
        page,
      }) => {
        const name = colHeader(page, 1);
        await expect(name).not.toHaveAttribute('aria-sort', /./);

        await name.click();
        await expect(page.getByTestId('order')).toHaveText('name asc');
        await expect(name).toHaveAttribute('aria-sort', 'ascending');
        await expect(liveRegion(page)).toContainText('Sorted by Name, ascending');
        // The host ordered its rows.
        await expect(cell(page, 0, 0)).toHaveText('PRD-0');
        await expect(cell(page, 1, 0)).toHaveText('PRD-1');
        await expect(cell(page, 2, 0)).toHaveText('PRD-10');

        await name.click();
        await expect(page.getByTestId('order')).toHaveText('name desc');
        await expect(name).toHaveAttribute('aria-sort', 'descending');
        await expect(liveRegion(page)).toContainText('Sorted by Name, descending');

        await name.click();
        await expect(page.getByTestId('order')).toHaveText('none');
        await expect(name).not.toHaveAttribute('aria-sort', /./);
        await expect(liveRegion(page)).toContainText('Sort cleared');

        // Another column starts ascending; the old one loses its aria-sort.
        await name.click();
        await colHeader(page, 2).click();
        await expect(page.getByTestId('order')).toHaveText('qty asc');
        await expect(name).not.toHaveAttribute('aria-sort', /./);
        await expect(colHeader(page, 2)).toHaveAttribute('aria-sort', 'ascending');

        // A non-sortable header keeps press-to-select; no row opened.
        await colHeader(page, 4).click();
        await expect(page.getByTestId('order')).toHaveText('qty asc');
        await expect(page.getByTestId('activation-count')).toHaveText('0');
      });

      test('the indicator slot follows the label and never shifts it', async ({ page }) => {
        const header = colHeader(page, 2);
        const label = header.locator('.tm-grid__colhdr-label');
        const slot = header.locator('.tm-grid__sort');
        // Park the pointer off the headers so no hover preview shows.
        const park = async (): Promise<void> => {
          const box = (await page.getByTestId('order').boundingBox())!;
          await page.mouse.move(box.x + 1, box.y + 1);
        };

        const before = (await label.boundingBox())!;
        const slotBox = (await slot.boundingBox())!;
        expect(Math.round(slotBox.width)).toBe(14);
        // The slot follows the label in reading order.
        if (dir === 'ltr') {
          expect(slotBox.x).toBeGreaterThanOrEqual(before.x + before.width - 1);
        } else {
          expect(slotBox.x + slotBox.width).toBeLessThanOrEqual(before.x + 1);
        }

        for (const order of ['qty asc', 'qty desc', 'none']) {
          await header.click();
          await park();
          await expect(page.getByTestId('order')).toHaveText(order);
          const after = (await label.boundingBox())!;
          expect([after.x, after.width]).toEqual([before.x, before.width]);
          expect((await slot.boundingBox())!.x).toBe(slotBox.x);
        }
      });

      test('hover previews the next direction faintly', async ({ page }) => {
        const header = colHeader(page, 3);
        const preview = header.locator('.tm-grid__sort-glyph--next');
        await expect(preview).toBeHidden();
        await header.hover();
        await expect(preview).toBeVisible();
        await expect(preview).toHaveCSS('color', await resolvedColor(page, '--text-muted'));
      });
    });

    test.describe('selection across pages', () => {
      test('select-all adds the page to the rows checked elsewhere, then removes only them', async ({
        page,
      }) => {
        for (const i of [0, 1, 2]) {
          await rowCheckbox(page, i).click();
        }
        await expect(page.getByTestId('selected-count')).toHaveText('3 selected');

        await page.getByTestId('page-next').click();
        await expect(page.getByTestId('page')).toHaveText('Page 2 of 5');
        await expect(checkAllBox(page)).toHaveAttribute('aria-checked', 'false');
        await checkAllBox(page).click();
        await expect(page.getByTestId('selected-count')).toHaveText('53 selected');
        await expect(liveRegion(page)).toContainText('50 of 50 selected here, 53 in all');

        await checkAllBox(page).click();
        await expect(page.getByTestId('selected-count')).toHaveText('3 selected');

        await page.getByTestId('page-prev').click();
        await expect(checkAllBox(page)).toHaveAttribute('aria-checked', 'mixed');
        await expect(row(page, 1)).toHaveAttribute('aria-selected', 'true');
      });

      test('keyboard only: Space checks rows, Ctrl+Shift+Space toggles the page', async ({
        page,
      }) => {
        // From the last toolbar control, Tab enters the grid; an arrow lands
        // on the first row.
        await page.getByTestId('jump').focus();
        await page.keyboard.press('Tab');
        await expect(gridScroller(page)).toBeFocused();
        await page.keyboard.press('ArrowDown');
        await expect(cell(page, 0, 0)).toBeFocused();
        await page.keyboard.press('Space');
        await page.keyboard.press('ArrowDown');
        await page.keyboard.press('Space');
        await expect(page.getByTestId('selected-count')).toHaveText('2 selected');
        await expect(page.getByTestId('activation-count')).toHaveText('0');

        // Back out to the pager, turn the page, and return.
        await page.keyboard.press('Shift+Tab');
        await expect(page.getByTestId('jump')).toBeFocused();
        for (let i = 0; i < 3; i++) {
          await page.keyboard.press('Shift+Tab');
        }
        await expect(page.getByTestId('page-next')).toBeFocused();
        await page.keyboard.press('Enter');
        await expect(page.getByTestId('page')).toHaveText('Page 2 of 5');
        for (let i = 0; i < 4; i++) {
          await page.keyboard.press('Tab');
        }
        await expect(activeCell(page)).toBeFocused();
        await expect(page.getByTestId('active-row')).not.toHaveText('none');
        await page.keyboard.press('Control+Shift+Space');
        await expect(page.getByTestId('selected-count')).toHaveText('52 selected');
        await expect(liveRegion(page)).toContainText('50 of 50 selected here, 52 in all');
      });
    });

    test.describe('row classes and no row headers', () => {
      test('muted rows take the secondary ink; the totals row is emphasized', async ({ page }) => {
        const muted = page.locator('.tm-grid__row--muted > [data-tm-cell]').first();
        const plain = page
          .locator('.tm-grid__row:not(.tm-grid__row--muted) > [data-tm-cell][data-col="3"]')
          .first();
        const secondary = await resolvedColor(page, '--text-secondary');
        await expect(muted).toHaveCSS('color', secondary);
        await expect(plain).not.toHaveCSS('color', secondary);

        // The totals row ends the page; no row is active, so no tint paints over it.
        await page.getByTestId('toggle-totals').click();
        await setScrollTop(page, 1_000_000);
        const total = cell(page, 50, 2);
        await expect(total).toHaveText(/\d/);
        await expect(total).toHaveCSS('font-weight', '600');
        await expect(total).toHaveCSS(
          'background-color',
          await resolvedColor(page, '--surface-subtle'),
        );
      });

      test('no row-number column and no corner; the checkbox column comes first', async ({
        page,
      }) => {
        await expect(page.locator('[data-tm-corner]')).toHaveCount(0);
        await expect(page.locator('[data-tm-rowhdr]')).toHaveCount(0);
        // 5 data columns + the checkbox column + the row-end column.
        await expect(gridScroller(page)).toHaveAttribute('aria-colcount', '7');
        await expect(page.locator('[data-tm-checkhdr]')).toHaveAttribute('aria-colindex', '1');
        await expect(colHeader(page, 0)).toHaveAttribute('aria-colindex', '2');
        const check = (await page.locator('[data-tm-checkhdr]').boundingBox())!;
        const scroller = (await gridScroller(page).boundingBox())!;
        // The first track sits at the inline start: the left in LTR, the right in RTL.
        const start =
          dir === 'ltr'
            ? check.x - scroller.x
            : scroller.x + scroller.width - (check.x + check.width);
        expect(start).toBeGreaterThanOrEqual(0);
        expect(start).toBeLessThanOrEqual(2);
      });
    });

    for (const theme of ['light', 'dark'] as const) {
      test(`the story is axe-clean with an active row and a sort (${theme})`, async ({ page }) => {
        await gotoGrid(page, 'grid-search-page', { dir, theme });
        await search(page).click();
        await page.keyboard.press('ArrowDown');
        await colHeader(page, 1).click();
        await rowCheckbox(page, 1).click();
        await expect(page.getByTestId('order')).toHaveText('name asc');
        await expectNoAxeViolations(page);
      });
    }
  });
}

// Forced colors: every mark the grid draws over a highlighted surface takes
// that surface's paired ink. Some platforms (Windows) give SelectedItem and
// Highlight one colour, so a Highlight mark on a SelectedItem surface
// vanishes there even where the emulated palette tells the two apart; the
// tests therefore pin the pairing, not just a difference.
test.describe('the active row under forced colors', () => {
  test.beforeEach(async ({ page }) => {
    await page.emulateMedia({ forcedColors: 'active' });
    await gotoGrid(page, 'grid-search-page');
  });

  test('the accent bar takes the ink of whichever cell starts the row', async ({ page }) => {
    const highlight = await systemColor(page, 'Highlight');
    const highlightText = await systemColor(page, 'HighlightText');
    const selectedItem = await systemColor(page, 'SelectedItem');
    const selectedItemText = await systemColor(page, 'SelectedItemText');

    // The checkbox column starts the row: a plain chrome cell carries a
    // Highlight bar.
    await cell(page, 2, 0).click();
    await expect(row(page, 2)).toHaveClass(/tm-grid__row--active/);
    let paint = await barPaint(page, 2);
    expect(paint.first).toBe('checkcell');
    expect(paint.barWidth).toBe('2px');
    expect(paint.bar).toBe(highlight);
    expect(paint.background).not.toBe(highlight);

    // Checked, the checkbox cell paints SelectedItem: the bar turns to the
    // paired ink.
    await rowCheckbox(page, 2).click();
    await expect(row(page, 2)).toHaveAttribute('aria-selected', 'true');
    await expect(row(page, 2)).toHaveClass(/tm-grid__row--active/);
    paint = await barPaint(page, 2);
    expect(paint.background).toBe(selectedItem);
    expect(paint.bar).toBe(selectedItemText);
    expect(paint.bar).toBe(paint.ink);

    // Row numbers start the row: the active cell's row header is hit, so it
    // paints SelectedItem under the bar as well.
    await page.getByTestId('toggle-row-numbers').click();
    await expect(rowHeader(page, 2)).toHaveClass(/tm-grid__rowhdr--hit/);
    paint = await barPaint(page, 2);
    expect(paint.first).toBe('rowhdr');
    expect(paint.background).toBe(selectedItem);
    expect(paint.bar).toBe(selectedItemText);
    expect(paint.bar).toBe(paint.ink);

    // No chrome column: the first data cell starts the row. Alone it is
    // plain; inside the range fill it paints Highlight, and the bar its ink.
    await page.getByTestId('toggle-row-numbers').click();
    await page.getByTestId('toggle-checkboxes').click();
    await expect(page.locator('[data-tm-rowhdr], [data-tm-checkcell]')).toHaveCount(0);
    paint = await barPaint(page, 2);
    expect(paint.first).toBe('cell');
    expect(paint.bar).toBe(highlight);
    expect(paint.background).not.toBe(highlight);

    await cell(page, 3, 2).click({ modifiers: ['Shift'] });
    await expect(selectedCells(page)).toHaveCount(6);
    await expect(row(page, 2)).toHaveClass(/tm-grid__row--active/);
    paint = await barPaint(page, 2);
    expect(paint.background).toBe(highlight);
    expect(paint.bar).toBe(highlightText);
    expect(paint.bar).toBe(paint.ink);
  });

  test('a focused header keeps a visible ring over its selected column', async ({ page }) => {
    await cell(page, 0, 0).click();
    await expect(cell(page, 0, 0)).toBeFocused();
    await page.keyboard.press('ArrowUp');
    await expect(colHeader(page, 0)).toBeFocused();
    await expect(colHeader(page, 0)).toHaveClass(/tm-grid__colhdr--hit/);
    await appStable(page);

    const header = await colHeader(page, 0).evaluate((el) => {
      const style = getComputedStyle(el);
      return {
        background: style.backgroundColor,
        outline: style.outlineStyle,
        ring: style.outlineColor,
      };
    });
    expect(header.background).toBe(await systemColor(page, 'SelectedItem'));
    expect(header.outline).toBe('solid');
    expect(header.ring).toBe(await systemColor(page, 'SelectedItemText'));
  });

  test('a header action keeps its glyph, on state and ring over its selected column', async ({
    page,
  }) => {
    const highlight = await systemColor(page, 'Highlight');
    const selectedItem = await systemColor(page, 'SelectedItem');
    const selectedItemText = await systemColor(page, 'SelectedItemText');
    const region = page.getByTestId('region-filter');
    const manager = page.getByTestId('manager-filter');
    // The action's computed paint, read once the app is stable.
    const actionPaint = async (
      action: Locator,
    ): Promise<{ ink: string; border: string; background: string; ring: string }> => {
      await appStable(page);
      return action.evaluate((el) => {
        const style = getComputedStyle(el);
        return {
          ink: style.color,
          border: style.borderTopColor,
          background: style.backgroundColor,
          ring: style.outlineColor,
        };
      });
    };

    // Filter Region; Escape returns keyboard focus to its action.
    await region.click();
    await page.getByTestId('region-filter-select').selectOption('North');
    await expect(region).toHaveClass(/tm-grid-header-action--active/);
    await page.keyboard.press('Escape');
    await expect(region).toBeFocused();

    // On a plain header the on state is Highlight, ring included.
    await expect(colHeader(page, 3)).not.toHaveClass(/tm-grid__colhdr--hit/);
    let paint = await actionPaint(region);
    expect(paint.ink).toBe(highlight);
    expect(paint.border).toBe(highlight);
    expect(paint.ring).toBe(highlight);

    // A cell of the column hits its header (SelectedItem): every mark of the
    // action turns to the paired ink, over the header's own surface.
    await cell(page, 0, 3).click();
    await expect(cell(page, 0, 3)).toBeFocused();
    await expect(colHeader(page, 3)).toHaveClass(/tm-grid__colhdr--hit/);
    await expect(colHeader(page, 3)).toHaveCSS('background-color', selectedItem);
    paint = await actionPaint(region);
    // Transparent (forced colors keep the alpha), so the header shows through.
    expect(paint.background).toMatch(/^rgba\(\d+, \d+, \d+, 0\)$/);
    expect(paint.ink).toBe(selectedItemText);
    expect(paint.border).toBe(selectedItemText);

    // Its focus ring too, with the column still selected.
    await page.keyboard.press('ArrowUp');
    await expect(colHeader(page, 3)).toBeFocused();
    await page.keyboard.press('Alt+ArrowDown');
    await expect(page.getByTestId('region-filter-select')).toBeFocused();
    await page.keyboard.press('Escape');
    await expect(region).toBeFocused();
    await expect(colHeader(page, 3)).toHaveClass(/tm-grid__colhdr--hit/);
    paint = await actionPaint(region);
    expect(paint.ring).toBe(selectedItemText);

    // An off action in a hit header draws its glyph in the paired ink as well.
    await cell(page, 0, 4).click();
    await expect(colHeader(page, 4)).toHaveClass(/tm-grid__colhdr--hit/);
    await expect(manager).not.toHaveClass(/tm-grid-header-action--active/);
    paint = await actionPaint(manager);
    expect(paint.ink).toBe(selectedItemText);
    expect(paint.border).not.toBe(selectedItemText);
  });
});

/** One of the flash ring's bands read off the screen at one point. */
interface BandSample {
  /** Where the band was read: the cell or row side, the edge and the band. */
  readonly at: string;
  /** The screen pixel with the ring on. */
  readonly actual: string;
  /**
   * The band's system colour composited over the same pixel before the
   * flash, as the screen paints it: a translucent system colour (some
   * platforms' Highlight) blends with the cell beneath, an opaque one
   * replaces it.
   */
  readonly expected: string;
  /** Whether the two agree within compositing rounding. */
  readonly close: boolean;
}

/** One cell of a flashing row: its surface, read from its styles. */
interface RingCell {
  /** The cell's chrome kind: its `data-tm-*` marker, or its tag. */
  readonly kind: string;
  /** The cell's computed background. */
  readonly background: string;
  /** Whether the cell runs an animation (the flash's tint). */
  readonly animated: boolean;
}

/** The flash ring read off the screen: over every cell of a row, and at the row's two ends. */
interface RingPaint {
  /** One entry per cell of the row, in DOM order. */
  readonly cells: readonly RingCell[];
  /**
   * The outer (Highlight) and inner (HighlightText) bands on the top and
   * bottom edges over every cell, and at mid-height on the row's two sides.
   */
  readonly bands: readonly BandSample[];
}

/**
 * Reads the flash ring's two bands back from the screen: the pixels in the
 * middle of each band with the ring on, against the same pixels in `before`
 * (a screenshot taken before the flash, nothing else changed) with the
 * band's system colour composited over them. A ring the cells paint over
 * leaves the pixels as they were and fails, so the check covers what the
 * screen shows, not only what the styles say. Each band is sampled at its
 * middle, clear of the snapping of either edge.
 */
async function ringPaint(page: Page, viewIndex: number, before: Buffer): Promise<RingPaint> {
  await appStable(page);
  const after = await page.screenshot();
  const shots = { before: before.toString('base64'), after: after.toString('base64') };
  return row(page, viewIndex).evaluate(async (rowElement, data): Promise<RingPaint> => {
    // Decodes a screenshot into a canvas to read its pixels.
    const decode = async (png: string): Promise<CanvasRenderingContext2D> => {
      const bytes = Uint8Array.from(atob(png), (char) => char.charCodeAt(0));
      const bitmap = await createImageBitmap(new Blob([bytes], { type: 'image/png' }));
      const canvas = document.createElement('canvas');
      canvas.width = bitmap.width;
      canvas.height = bitmap.height;
      const context = canvas.getContext('2d', { willReadFrequently: true })!;
      context.drawImage(bitmap, 0, 0);
      return context;
    };
    const [ringOff, ringOn] = await Promise.all([decode(data.before), decode(data.after)]);
    const scale = ringOn.canvas.width / window.innerWidth;
    const pixel = (context: CanvasRenderingContext2D, x: number, y: number): number[] => [
      ...context.getImageData(Math.floor(x * scale), Math.floor(y * scale), 1, 1).data.slice(0, 3),
    ];

    // The bands' system colours with their alpha, as the page resolves
    // them; kept as authored, so the probe reads the keyword's own value.
    const channels = (name: string): number[] => {
      const probe = document.createElement('div');
      probe.style.forcedColorAdjust = 'none';
      probe.style.backgroundColor = name;
      document.body.append(probe);
      const resolved = getComputedStyle(probe).backgroundColor;
      probe.remove();
      const [r, g, b, a = 1] = (resolved.match(/[0-9.]+/g) ?? []).map(Number);
      return [r, g, b, a];
    };
    const outer = channels('Highlight');
    const inner = channels('HighlightText');
    const rgb = ([r, g, b]: number[]): string => `rgb(${r}, ${g}, ${b})`;

    // One band read: the ring-on pixel against the band's colour composited
    // (source-over) on the ring-off pixel, within a step of rounding.
    const bands: BandSample[] = [];
    const read = (at: string, colour: number[], x: number, y: number): void => {
      const base = pixel(ringOff, x, y);
      const actual = pixel(ringOn, x, y);
      const alpha = colour[3];
      const expected = base.map((channel, i) =>
        Math.round(colour[i] * alpha + channel * (1 - alpha)),
      );
      bands.push({
        at,
        actual: rgb(actual),
        expected: rgb(expected),
        close: actual.every((channel, i) => Math.abs(channel - expected[i]) <= 2),
      });
    };

    // Each band is a focus ring wide, read from the token rather than from
    // the ring, so a missing ring is sampled where it should be; the ring
    // insets nothing from the row box.
    const band = Number.parseFloat(
      getComputedStyle(rowElement).getPropertyValue('--focus-ring-width'),
    );
    if (!(band > 0)) {
      throw new Error('--focus-ring-width did not resolve to a width');
    }
    const box = rowElement.getBoundingClientRect();
    const middle = box.top + box.height / 2;
    const cells = [...rowElement.children].map((child, index): RingCell => {
      const rect = child.getBoundingClientRect();
      const x = rect.left + rect.width / 2;
      read(`cell ${index} top outer`, outer, x, box.top + band / 2);
      read(`cell ${index} top inner`, inner, x, box.top + band * 1.5);
      read(`cell ${index} bottom outer`, outer, x, box.bottom - band / 2);
      read(`cell ${index} bottom inner`, inner, x, box.bottom - band * 1.5);
      const markers = (child as HTMLElement).dataset;
      return {
        kind:
          'tmRowhdr' in markers
            ? 'rowhdr'
            : 'tmCheckcell' in markers
              ? 'checkcell'
              : 'tmCell' in markers
                ? 'cell'
                : child.localName,
        background: getComputedStyle(child).backgroundColor,
        animated: child.getAnimations().length > 0,
      };
    });
    // The inline ends in screen terms: the row's left and right.
    read('left outer', outer, box.left + band / 2, middle);
    read('left inner', inner, box.left + band * 1.5, middle);
    read('right outer', outer, box.right - band / 2, middle);
    read('right inner', inner, box.right - band * 1.5, middle);
    return { cells, bands };
  }, shots);
}

/**
 * Asserts that every band read shows its system colour over what lay
 * beneath it. A missing ring cannot pass both bands at one point: that
 * needs the pixel beneath to look like Highlight and like HighlightText at
 * once, and the two are a contrasting pair.
 */
function expectRinged(paint: RingPaint): void {
  for (const band of paint.bands) {
    expect(band.close, `${band.at}: ${band.actual}, expected ${band.expected}`).toBe(true);
  }
}

/**
 * Takes the screenshot a ring is read against, with the pointer on the
 * flash button and the focus in it, as they will be once the button is
 * clicked, so nothing but the flash differs between the two.
 */
async function beforeFlash(page: Page): Promise<Buffer> {
  const button = page.getByTestId('flash-active');
  await button.hover();
  await button.focus();
  await appStable(page);
  return page.screenshot();
}

// Forced colors: scrollToRow's flash rings the row instead of tinting it, and
// a row mixes Canvas, SelectedItem (checked row, hit row header) and
// Highlight (range fill) cells, so no one colour shows on all of them. The
// ring is a Highlight band with a HighlightText band inside: the two pin a
// pair that contrasts with every one of those surfaces, even where
// SelectedItem and Highlight are one colour (Windows).
test.describe('the row flash and the row checkbox under forced colors', () => {
  test.beforeEach(async ({ page }) => {
    await page.emulateMedia({ forcedColors: 'active' });
    await gotoGrid(page, 'grid-search-page');
    // A long flash, so the ring is still on screen when the test reads it.
    await page
      .locator('tm-grid')
      .evaluate((grid: HTMLElement) => grid.style.setProperty('--grid-flash-duration', '60s'));
  });

  test('the flash rings a mixed row above every cell and keeps the cells painted', async ({
    page,
  }) => {
    const highlight = await systemColor(page, 'Highlight');
    const selectedItem = await systemColor(page, 'SelectedItem');

    // Row numbers, the checkbox column and row 2 checked; a 2×3 range from
    // (2,0): row 2 then mixes the hit row header, the checked cells and the
    // range fill.
    await page.getByTestId('toggle-row-numbers').click();
    await rowCheckbox(page, 2).click();
    await expect(row(page, 2)).toHaveAttribute('aria-selected', 'true');
    await cell(page, 2, 0).click();
    await cell(page, 3, 2).click({ modifiers: ['Shift'] });
    await expect(selectedCells(page)).toHaveCount(6);
    await expect(row(page, 2)).toHaveClass(/tm-grid__row--active/);

    let before = await beforeFlash(page);
    let box = await row(page, 2).boundingBox();
    await page.getByTestId('flash-active').click();
    await expect(row(page, 2)).toHaveClass(/tm-grid__row--flash/);
    // In place: the range and the scroll position survive the flash, so the
    // screenshot before it shows what lies under the ring.
    await expect(selectedCells(page)).toHaveCount(6);
    expect(await row(page, 2).boundingBox()).toEqual(box);
    let paint = await ringPaint(page, 2, before);
    expect(paint.cells.map((sample) => sample.kind)).toEqual([
      'rowhdr',
      'checkcell',
      'cell',
      'cell',
      'cell',
      'cell',
      'cell',
      'tm-grid-end-cell',
    ]);
    // No tint runs, so every cell keeps its own surface while it flashes.
    expect(paint.cells.map((sample) => sample.background)).toEqual([
      selectedItem,
      selectedItem,
      highlight,
      highlight,
      highlight,
      selectedItem,
      selectedItem,
      selectedItem,
    ]);
    expect(paint.cells.some((sample) => sample.animated)).toBe(false);
    expectRinged(paint);

    // An unchecked row: plain cells beside the fill and the hit row header.
    await cell(page, 4, 3).click();
    await cell(page, 4, 4).click({ modifiers: ['Shift'] });
    await expect(selectedCells(page)).toHaveCount(2);
    before = await beforeFlash(page);
    box = await row(page, 4).boundingBox();
    await page.getByTestId('flash-active').click();
    await expect(row(page, 4)).toHaveClass(/tm-grid__row--flash/);
    await expect(row(page, 2)).not.toHaveClass(/tm-grid__row--flash/);
    expect(await row(page, 4).boundingBox()).toEqual(box);
    paint = await ringPaint(page, 4, before);
    const backgrounds = paint.cells.map((sample) => sample.background);
    expect(backgrounds[0]).toBe(selectedItem); // the hit row header
    expect(backgrounds.slice(5, 7)).toEqual([highlight, highlight]);
    expect(backgrounds[2]).not.toBe(highlight);
    expect(backgrounds[2]).not.toBe(selectedItem);
    expectRinged(paint);
  });

  test("a checked row's checkbox keeps its edge over the checked cell", async ({ page }) => {
    const highlight = await systemColor(page, 'Highlight');
    const selectedItem = await systemColor(page, 'SelectedItem');
    const selectedItemText = await systemColor(page, 'SelectedItemText');
    // The box's and its cell's computed paint.
    const boxPaint = async (
      viewIndex: number,
    ): Promise<{ cell: string; box: string; edge: string; glyph: string }> => {
      await appStable(page);
      return rowCheckbox(page, viewIndex).evaluate((box) => ({
        cell: getComputedStyle(box.parentElement!).backgroundColor,
        box: getComputedStyle(box).backgroundColor,
        edge: getComputedStyle(box).borderTopColor,
        glyph: getComputedStyle(box, '::after').backgroundColor,
      }));
    };

    await rowCheckbox(page, 2).click();
    await expect(row(page, 2)).toHaveAttribute('aria-selected', 'true');
    // The box is Highlight on a SelectedItem cell, which some platforms
    // make one colour: its edge takes the cell's paired ink, so the box
    // keeps its outline there too, beside the HighlightText glyph.
    const checked = await boxPaint(2);
    expect(checked.cell).toBe(selectedItem);
    expect(checked.box).toBe(highlight);
    expect(checked.edge).toBe(selectedItemText);
    expect(checked.glyph).toBe(await systemColor(page, 'HighlightText'));

    // The mixed select-all box sits on the plain header: a Highlight edge.
    await expect(checkAllBox(page)).toHaveClass(/tm-grid__check--mixed/);
    const all = await checkAllBox(page).evaluate((box) => ({
      cell: getComputedStyle(box.parentElement!).backgroundColor,
      edge: getComputedStyle(box).borderTopColor,
    }));
    expect(all.edge).toBe(highlight);
    expect(all.cell).not.toBe(highlight);
  });
});

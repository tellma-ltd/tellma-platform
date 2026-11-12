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
  setScrollTop,
} from '../support/grid';

/**
 * The search-page battery against the
 * `grid-search-page` story: a search field driving a readonly list whose
 * rows open on click or Enter, sortable headers editing a two-way sort,
 * cross-page checkbox selection over a paged source, row classes, and a
 * list without row numbers — in both directions, with axe in both themes.
 *
 * Story shape: 5 data columns (0 code, 1 name — a real link, 2 qty,
 * 3 region, 4 manager), the first four sortable; 230 rows in pages of 50,
 * unsorted row i having id i; `selectable`, `rowActivation="row"`,
 * `[rowHeaders]="false"`. Readouts: `activated` ("<id> via <pointer|
 * keyboard>[ in a new tab]"), `activation-count`, `active-row`, `order`
 * ("<key> <asc|desc>" or "none"), `selected-count` ("N selected") and
 * `link-clicked`.
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
        // 5 data columns + the checkbox column.
        await expect(gridScroller(page)).toHaveAttribute('aria-colcount', '6');
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

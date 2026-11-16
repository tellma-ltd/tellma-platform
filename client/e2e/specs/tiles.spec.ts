// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { expect, test, type Locator, type Page } from '@playwright/test';

import { expectNoAxeViolations } from '../support/axe';
import { heldResponses, holdResponses, releaseResponses } from '../support/held-responses';
import { appStable } from '../support/settle';
import { storyUrl } from '../support/story-map';

/**
 * Browser battery for tm-tiles: the axe floor over the flat, empty and tree
 * stories in both themes and directions; the keyboard in both directions
 * and across a live switch to Arabic; the search-field driver; pointer
 * activation and the checkbox; an external activeRowId write and a flashed
 * scroll; skeleton tiles that hold the data's geometry; the narrow list;
 * tree mode in RTL with lazy loads, a failure and its announcement; the
 * selected tile under forced colors; and the 10,000-row virtualization,
 * with its reused tiles kept true to their rows under focus and selection,
 * and its long-task probe in the nightly tier.
 */

/** The main story's tiles. */
function tiles(page: Page, testId = 'tiles'): Locator {
  return page.getByTestId(testId);
}

/** The flat story's tile for a customer id (the data order is the id order). */
function tile(page: Page, id: number, testId = 'tiles'): Locator {
  return tiles(page, testId).locator(`.tm-tiles__item[data-index="${id - 1}"]`);
}

/** The tree story's tile for an account name. */
function account(page: Page, name: string): Locator {
  return (
    tiles(page, 'tiles-tree')
      .locator('.tm-tiles__item')
      // The whole title, whatever whitespace the template leaves around it.
      .filter({
        has: page.locator('.tm-tiles__title', { hasText: new RegExp(`^\\s*${name}\\s*$`) }),
      })
  );
}

/** The tiles per line, as the grid reports them. */
async function columns(page: Page, testId = 'tiles'): Promise<number> {
  return Number(await tiles(page, testId).locator('[role="grid"]').getAttribute('aria-colcount'));
}

test.describe('axe floor', () => {
  for (const theme of ['light', 'dark'] as const) {
    for (const dir of ['ltr', 'rtl'] as const) {
      test(`tiles stories are axe-clean in every state (${theme}, ${dir})`, async ({ page }) => {
        await page.goto(storyUrl('tiles', { theme, dir }));
        await expect(tile(page, 1)).toBeVisible();
        await expectNoAxeViolations(page);
        // Selected and active tiles, then the empty state.
        await tile(page, 2).locator('[role="checkbox"]').click();
        await page.keyboard.press('ArrowDown');
        await expect(page.getByTestId('selected')).toHaveText('1');
        await expectNoAxeViolations(page);
        await page.getByTestId('empty').click();
        await expect(tiles(page).locator('[data-tm-empty]')).toBeVisible();
        await expectNoAxeViolations(page);

        await page.goto(storyUrl('tiles-tree', { theme, dir }));
        await expect(account(page, 'Equity')).toBeVisible();
        await account(page, 'Equity').locator('.tm-tiles__twisty').click();
        await expect(account(page, 'Capital')).toBeVisible();
        await expectNoAxeViolations(page);
      });
    }
  }
});

test('the tiles are a named grid of lines and cells with one tab stop', async ({ page }) => {
  await page.goto(storyUrl('tiles'));
  const grid = tiles(page).getByRole('grid', { name: 'Customers' });
  await expect(grid).toHaveAttribute('aria-multiselectable', 'true');
  const perLine = await columns(page);
  expect(perLine).toBeGreaterThan(1);
  await expect(grid).toHaveAttribute('aria-rowcount', String(Math.ceil(120 / perLine)));
  await expect(grid.locator('[role="row"]').first().locator('[role="gridcell"]')).toHaveCount(
    perLine,
  );
  await expect(grid.locator('[role="gridcell"][tabindex="0"]')).toHaveCount(1);
  await expect(tile(page, 2)).toHaveAttribute('aria-colindex', '2');
  await expect(tile(page, 1).locator('a')).toHaveAttribute('tabindex', '-1');
  await expect(tile(page, 1).locator('[role="checkbox"]')).toHaveAccessibleName(
    'Select Customer 1',
  );
});

for (const dir of ['ltr', 'rtl'] as const) {
  test(`the arrows follow the reading direction and Up and Down keep the column (${dir})`, async ({
    page,
  }) => {
    await page.goto(storyUrl('tiles', { dir }));
    const perLine = await columns(page);
    const forward = dir === 'ltr' ? 'ArrowRight' : 'ArrowLeft';
    const back = dir === 'ltr' ? 'ArrowLeft' : 'ArrowRight';
    await tile(page, 1).focus();
    await page.keyboard.press(forward);
    await expect(tile(page, 2)).toBeFocused();
    await page.keyboard.press('ArrowDown');
    await expect(tile(page, 2 + perLine)).toBeFocused();
    await page.keyboard.press(back);
    await expect(tile(page, 1 + perLine)).toBeFocused();
    await page.keyboard.press('End');
    await expect(tile(page, 2 * perLine)).toBeFocused();
    await page.keyboard.press('Home');
    await expect(tile(page, 1 + perLine)).toBeFocused();
    await page.keyboard.press('Control+End');
    await expect(tile(page, 120)).toBeFocused();
    await page.keyboard.press('Control+Home');
    await expect(tile(page, 1)).toBeFocused();
    await page.keyboard.press('PageDown');
    await expect(page.getByTestId('active')).not.toHaveText('1');
    await page.keyboard.press('Control+Home');
    await page.keyboard.press('Space');
    await expect(page.getByTestId('selected')).toHaveText('1');
    await expect(tile(page, 1)).toHaveAttribute('aria-selected', 'true');
    await page.keyboard.press('Enter');
    await expect(page.getByTestId('activated')).toHaveText('1 (keyboard)');
  });
}

// The app switches English to Arabic while the tiles are on screen: the
// arrows read the live direction at keydown, so they remap without a reload.
test('a live English to Arabic switch remaps the arrows and the strings', async ({ page }) => {
  await page.goto(storyUrl('tiles'));
  await tile(page, 1).focus();
  await page.keyboard.press('ArrowRight');
  await expect(tile(page, 2)).toBeFocused();

  await page.getByTestId('lang-ar').click();
  await expect(page.locator('html')).toHaveAttribute('dir', 'rtl');
  await tile(page, 2).focus();
  await page.keyboard.press('ArrowLeft');
  await expect(tile(page, 3)).toBeFocused();
  await expect(tile(page, 3).locator('[role="checkbox"]')).toHaveAccessibleName('تحديد Customer 3');
  await page.keyboard.press('Space');
  await expect(page.locator('.cdk-live-announcer-element')).toHaveText(
    /تم تحديد ١ من ١٢٠|تم تحديد 1 من 120/,
  );
});

test('the search field drives the active tile while focus stays in it', async ({ page }) => {
  await page.goto(storyUrl('tiles'));
  const perLine = await columns(page);
  const search = page.getByTestId('search');
  await search.focus();
  await page.keyboard.press('ArrowDown');
  await expect(page.getByTestId('active')).toHaveText('1');
  await page.keyboard.press('ArrowDown');
  await expect(page.getByTestId('active')).toHaveText(String(1 + perLine));
  const activeId = await tile(page, 1 + perLine).getAttribute('id');
  await expect(search).toHaveAttribute('aria-activedescendant', activeId!);
  await expect(search).toHaveAttribute(
    'aria-controls',
    (await tiles(page).locator('[role="grid"]').getAttribute('id'))!,
  );
  await expect(tile(page, 1 + perLine)).toHaveClass(/tm-tiles__item--active/);
  await expect(search).toBeFocused();
  await page.keyboard.press('Enter');
  await expect(page.getByTestId('activated')).toHaveText(`${1 + perLine} (keyboard)`);
});

test('a click opens a tile, Mod-click in a new tab, and the checkbox only selects', async ({
  page,
}) => {
  await page.goto(storyUrl('tiles'));
  await tile(page, 3).locator('.tm-tiles__corner--bottom-start').click();
  await expect(page.getByTestId('activated')).toHaveText('3 (pointer)');
  await tile(page, 4)
    .locator('.tm-tiles__corner--bottom-start')
    .click({ modifiers: ['ControlOrMeta'] });
  await expect(page.getByTestId('activated')).toHaveText('4 (pointer, new tab)');
  await tile(page, 5).locator('[role="checkbox"]').click();
  await expect(page.getByTestId('selected')).toHaveText('1');
  await expect(tile(page, 5).locator('[role="checkbox"]')).toHaveAttribute('aria-checked', 'true');
  await expect(page.getByTestId('activated')).toHaveText('4 (pointer, new tab)');
  await expect(page.locator('.cdk-live-announcer-element')).toHaveText('1 of 120 selected');
});

test('an activeRowId write scrolls to the tile and leaves focus; scrollToRow flashes', async ({
  page,
}) => {
  await page.goto(storyUrl('tiles'));
  const jump = page.getByTestId('jump');
  await jump.click();
  await expect(tile(page, 41)).toHaveClass(/tm-tiles__item--active/);
  await expect(tile(page, 41)).toBeInViewport();
  await expect(jump).toBeFocused();
  await page.getByTestId('flash').click();
  await expect(tile(page, 57)).toHaveClass(/tm-tiles__item--flash/);
  await expect(tile(page, 57)).toBeInViewport();
  await expect(tile(page, 57)).not.toHaveClass(/tm-tiles__item--flash/, { timeout: 5000 });
});

test('skeleton tiles hold the geometry the data lands in', async ({ page }) => {
  await page.goto(storyUrl('tiles'));
  const loaded = await tile(page, 2).boundingBox();
  await holdResponses(page, 'rows');
  await page.getByTestId('load').click();
  const skeletons = tiles(page).locator('.tm-tiles__item--skeleton');
  await expect(skeletons.first()).toBeVisible();
  await expect(tiles(page).locator('[role="grid"]')).toHaveAttribute('aria-busy', 'true');
  await appStable(page);
  const skeleton = await skeletons.nth(1).boundingBox();
  expect(skeleton).toEqual(loaded);
  await releaseResponses(page, 'rows');
  await expect(skeletons).toHaveCount(0);
  await expect(tiles(page).locator('[role="grid"]')).not.toHaveAttribute('aria-busy');
  expect(await tile(page, 2).boundingBox()).toEqual(skeleton);
});

test('loading over data is busy; no data says so or shows the template', async ({ page }) => {
  await page.goto(storyUrl('tiles'));
  await page.getByTestId('busy').click();
  await expect(tiles(page).locator('[role="grid"]')).toHaveAttribute('aria-busy', 'true');
  await expect(tile(page, 1)).toBeVisible();
  await page.getByTestId('empty').click();
  await expect(tiles(page).locator('[data-tm-empty]')).toHaveText('No items to display');
  await page.getByTestId('custom-empty').click();
  await expect(page.getByTestId('custom-empty-text')).toBeVisible();
});

test('a narrow container stacks the tiles as a divided list', async ({ page }) => {
  await page.goto(storyUrl('tiles'));
  const narrow = tiles(page, 'tiles-narrow');
  await expect(narrow).toHaveAttribute('data-layout', 'list');
  expect(await columns(page, 'tiles-narrow')).toBe(1);
  const first = (await tile(page, 1, 'tiles-narrow').boundingBox())!;
  const second = (await tile(page, 2, 'tiles-narrow').boundingBox())!;
  expect(second.y - first.y).toBe(first.height);
});

test('the selected tile keeps a system outline under forced colors', async ({ page }) => {
  await page.emulateMedia({ forcedColors: 'active' });
  await page.goto(storyUrl('tiles'));
  await tile(page, 2).locator('[role="checkbox"]').click();
  await expect(tile(page, 2)).toHaveAttribute('aria-selected', 'true');
  const outline = (id: number) =>
    tile(page, id).evaluate((element) => getComputedStyle(element).outlineStyle);
  expect(await outline(2)).toBe('solid');
  expect(await outline(3)).toBe('none');
});

test.describe('tree mode', () => {
  test('a right-to-left tree loads lazily, enters and leaves with the mapped arrows, and announces a failure', async ({
    page,
  }) => {
    await page.goto(storyUrl('tiles-tree', { dir: 'rtl' }));
    const grid = tiles(page, 'tiles-tree').getByRole('treegrid', { name: 'Accounts' });
    await expect(grid).toHaveAttribute('aria-colcount', '1');
    await expect(tiles(page, 'tiles-tree')).toHaveAttribute('data-layout', 'tree');

    // Section: expand with the forward arrow (Left in RTL); the spinner sits
    // in its reserved slot and nothing moves.
    await holdResponses(page, 'children');
    const assets = account(page, 'Assets');
    const title = assets.locator('.tm-tiles__title');
    const before = await title.boundingBox();
    await assets.focus();
    await page.keyboard.press('ArrowLeft');
    await expect(page.getByTestId('batches')).toHaveText('1');
    const row = grid.locator('[role="row"]').first();
    await expect(row).toHaveAttribute('aria-expanded', 'true');
    await expect(row).toHaveAttribute('aria-busy', 'true');
    await expect(assets.locator('tm-spinner')).toBeVisible();
    await appStable(page);
    expect(await title.boundingBox()).toEqual(before);
    await releaseResponses(page, 'children');
    await expect(account(page, 'Assets 1')).toBeVisible();
    await expect(row).not.toHaveAttribute('aria-busy');
    expect(await title.boundingBox()).toEqual(before);
    await expect(grid.locator('[role="row"]').nth(1)).toHaveAttribute('aria-level', '2');

    // Section: forward again enters the first child; back leaves it, then collapses.
    await page.keyboard.press('ArrowLeft');
    await expect(account(page, 'Assets 1')).toBeFocused();
    await page.keyboard.press('ArrowRight');
    await expect(assets).toBeFocused();
    await page.keyboard.press('ArrowRight');
    await expect(account(page, 'Assets 1')).toHaveCount(0);

    // Section: a failed load collapses its row and says so once.
    await account(page, 'Archive').locator('.tm-tiles__twisty').click();
    await expect(heldResponses(page, 'children')).toHaveText('1');
    await releaseResponses(page, 'children');
    await expect(grid.locator('[role="row"]').nth(2)).toHaveAttribute('aria-expanded', 'false');
    await expect(page.locator('.cdk-live-announcer-element')).toHaveText("Couldn't load Archive");
  });
});

test.describe('10,000 rows', () => {
  test('only the windowed lines render, and scrolling keeps the window small', async ({ page }) => {
    await page.goto(storyUrl('tiles-large'));
    const perLine = await columns(page, 'tiles-large');
    const grid = tiles(page, 'tiles-large').locator('[role="grid"]');
    await expect(grid).toHaveAttribute('aria-rowcount', String(Math.ceil(10_000 / perLine)));
    const cells = grid.locator('[role="gridcell"]');
    expect(await cells.count()).toBeLessThan(20 * perLine);
    await grid.evaluate((element) => {
      element.scrollTop = element.scrollHeight / 2;
    });
    await expect(grid.locator('[aria-rowindex="1"]')).toHaveCount(0);
    expect(await cells.count()).toBeLessThan(20 * perLine);
  });

  // Scrolling re-binds the elements of the lines that leave to the lines
  // that enter: a focused tile must keep its row and focus, and every tile
  // must show its own row's title, code and selection after each step.
  test('wheel scrolling keeps focus on its row and every tile true to its own row', async ({
    page,
  }) => {
    await page.goto(storyUrl('tiles-large'));
    const grid = tiles(page, 'tiles-large').locator('[role="grid"]');
    await expect(grid.locator('[role="gridcell"]').first()).toBeVisible();

    // Section: select two customers and focus a third, near the top.
    await tile(page, 2, 'tiles-large').locator('[role="checkbox"]').click();
    await tile(page, 7, 'tiles-large').locator('[role="checkbox"]').click();
    await tile(page, 3, 'tiles-large').focus();
    await appStable(page);

    // Section: wheel away and back a screen at a time, checking every rendered tile.
    const box = (await grid.boundingBox())!;
    await page.mouse.move(box.x + box.width / 2, box.y + box.height / 2);
    for (const direction of [1, -1]) {
      for (let step = 0; step < 12; step++) {
        await page.mouse.wheel(0, direction * box.height);
        await appStable(page);
        const report = await grid.evaluate((element) => {
          const wrong: string[] = [];
          for (const cell of element.querySelectorAll<HTMLElement>('[role="gridcell"]')) {
            const id = Number(cell.dataset['index']) + 1;
            const title = cell.querySelector('.tm-tiles__title')!.textContent!.trim();
            const code = cell.querySelector('.tm-tiles__corner--bottom-start')!.textContent!.trim();
            const selected = cell.getAttribute('aria-selected') === 'true';
            if (
              title !== `Customer ${id}` ||
              code !== `C${String(id).padStart(5, '0')}` ||
              selected !== (id === 2 || id === 7)
            ) {
              wrong.push(`${id}: ${title}, ${code}, selected ${selected}`);
            }
          }
          const focused = document.activeElement as HTMLElement | null;
          return {
            wrong,
            focused: focused?.querySelector('.tm-tiles__title')?.textContent?.trim() ?? null,
          };
        });
        expect(report).toEqual({ wrong: [], focused: 'Customer 3' });
      }
    }
    await expect(tile(page, 3, 'tiles-large')).toBeFocused();
    await page.keyboard.press('ArrowRight');
    await expect(tile(page, 4, 'tiles-large')).toBeFocused();
  });

  // An arrow move from a focused tile scrolled out of view can re-bind that
  // very element to the new row: the move must still fire focus on it (so a
  // screen reader reads the new tile), and a page's later activeRowId write
  // must leave focus where it is.
  test('an arrow move that re-binds the focused tile fires focus, and a page write leaves it', async ({
    page,
  }) => {
    await page.goto(storyUrl('tiles'));
    const grid = tiles(page).locator('[role="grid"]');
    await tile(page, 33).focus();
    await appStable(page);

    // Section: wheel to the end, so the focused line is kept off the window.
    const box = (await grid.boundingBox())!;
    await page.mouse.move(box.x + box.width / 2, box.y + box.height / 2);
    for (let step = 0; step < 8; step++) {
      await page.mouse.wheel(0, box.height);
      await appStable(page);
    }
    await expect(tile(page, 33)).toBeFocused();

    // Section: record focus entering tiles, then move down a line.
    await grid.evaluate((element) => {
      const entered: string[] = [];
      (window as unknown as { tmFocusIns: string[] }).tmFocusIns = entered;
      element.addEventListener('focusin', (event) =>
        entered.push((event.target as HTMLElement).dataset['index'] ?? ''),
      );
    });
    await page.keyboard.press('ArrowDown');
    await appStable(page);
    const index = await page.evaluate(
      () => (document.activeElement as HTMLElement).dataset['index'] ?? '',
    );
    const id = Number(index) + 1;
    expect(id).toBeGreaterThan(33);
    await expect(tile(page, id)).toBeFocused();
    await expect(tile(page, id).locator('.tm-tiles__title')).toHaveText(`Customer ${id}`);
    expect(
      await page.evaluate(() => (window as unknown as { tmFocusIns: string[] }).tmFocusIns),
    ).toContain(index);

    // Section: a script's activeRowId write (the jump button, clicked
    // without moving focus) scrolls the list but leaves focus on the tile.
    await page.getByTestId('jump').evaluate((button: HTMLElement) => button.click());
    await appStable(page);
    await expect(page.getByTestId('active')).toHaveText('41');
    if (id !== 41) {
      await expect(tile(page, id)).toBeFocused();
    }
  });

  // Spec 0023 §12's bar, in the nightly tier: each screen-sized wheel step
  // re-binds most rendered tiles to new rows, and no step may cost a task
  // over 50 ms.
  test('@perf scrolling 10,000 tiles produces no long task over 50 ms', async ({
    page,
    browserName,
  }) => {
    test.skip(browserName !== 'chromium', 'Long-task timing is a Chromium API.');
    await page.goto(storyUrl('tiles-large'));
    const grid = tiles(page, 'tiles-large').locator('[role="grid"]');
    await expect(grid.locator('[role="gridcell"]').first()).toBeVisible();
    await appStable(page);

    // Section: record long tasks from here on.
    await page.evaluate(() => {
      const record = window as unknown as { tmLongTasks: number[] };
      record.tmLongTasks = [];
      new PerformanceObserver((list) => {
        for (const entry of list.getEntries()) {
          record.tmLongTasks.push(entry.duration);
        }
      }).observe({ type: 'longtask' });
    });

    // Section: wheel down through the tiles a screen at a time, then back.
    const box = (await grid.boundingBox())!;
    await page.mouse.move(box.x + box.width / 2, box.y + box.height / 2);
    for (const direction of [1, -1]) {
      for (let step = 0; step < 40; step++) {
        await page.mouse.wheel(0, direction * box.height);
        await page.evaluate(() => new Promise((resolve) => requestAnimationFrame(resolve)));
      }
    }
    await appStable(page);

    const longTasks = await page.evaluate(
      () => (window as unknown as { tmLongTasks: number[] }).tmLongTasks,
    );
    expect(longTasks.filter((duration) => duration > 50)).toEqual([]);
  });
});

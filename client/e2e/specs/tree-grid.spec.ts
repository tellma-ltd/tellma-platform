// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { expect, test, type Locator, type Page } from '@playwright/test';

import { expectNoAxeViolations } from '../support/axe';
import { pressUndo, syntheticPaste } from '../support/clipboard';
import {
  activateCell,
  activeCell,
  cell,
  cellText,
  gotoGrid,
  gridScroller,
  liveRegion,
  modelJson,
  renderedRows,
  rowHeader,
  setScrollTop,
} from '../support/grid';

/**
 * The tree grid (spec 0004 §13) against the accounts-tree story: DFS
 * flattening with the treegrid ARIA contract, expander + Alt+Arrow
 * expand/collapse, lazy child loading (reserved-slot spinner, re-collapse
 * during load, failure restore), insert-child and subtree delete through
 * the menu, full-row subtree moves with re-parenting, tree paste mapping,
 * expansion-state persistence across contentKey switches and remounts,
 * and the axe floor over both themes.
 *
 * Story shape (fully expanded): 58 data rows + the new-row placeholder →
 * aria-rowcount 60; 8 roots (5 real + lazy + orphan + cycle-break).
 */

interface Account {
  readonly id: number;
  readonly parentId: number | null;
  readonly name: string | null;
  readonly code: string | null;
  readonly balance: number | null;
  readonly active: boolean;
}

/** The rendered row element at a view-space row index. */
function row(page: Page, viewIndex: number): Locator {
  return page.locator(`.tm-grid__row[aria-rowindex="${viewIndex + 2}"]`);
}

/** The expander button inside a row's hierarchy cell. */
function expander(page: Page, viewIndex: number): Locator {
  return cell(page, viewIndex, 0).locator('[data-tm-expander]');
}

/** The lazy-loading spinner inside a row's reserved slot. */
function childSpinner(page: Page, viewIndex: number): Locator {
  return cell(page, viewIndex, 0).locator('[data-tm-childspin]');
}

/** Collapses the tree to roots only via the depth select (contentKey a:0). */
async function selectDepthZero(page: Page): Promise<void> {
  await page.getByTestId('depth-select').selectOption('0');
  await expect(gridScroller(page)).toHaveAttribute('aria-rowcount', '10');
}

test.beforeEach(async ({ page }) => {
  await gotoGrid(page, 'tree-grid');
});

test.describe('rendering & ARIA', () => {
  test('@cross-engine role=treegrid, DFS order, and aria-level/posinset/setsize', async ({
    page,
  }) => {
    const scroller = gridScroller(page);
    await expect(scroller).toHaveAttribute('role', 'treegrid');
    await expect(scroller).toHaveAttribute('aria-rowcount', '60');

    // Depth-first flattening: parents immediately followed by their kids.
    expect(await cellText(page, 0, 0)).toBe('Assets');
    expect(await cellText(page, 1, 0)).toBe('Current assets');
    expect(await cellText(page, 2, 0)).toBe('Cash and equivalents');
    expect(await cellText(page, 3, 0)).toBe('Petty cash');
    expect(await cellText(page, 13, 0)).toBe('Non-current assets');

    // aria-level is 1-based depth; posinset/setsize count SIBLINGS.
    await expect(row(page, 0)).toHaveAttribute('aria-level', '1');
    await expect(row(page, 0)).toHaveAttribute('aria-posinset', '1');
    await expect(row(page, 0)).toHaveAttribute('aria-setsize', '8');
    await expect(row(page, 1)).toHaveAttribute('aria-level', '2');
    await expect(row(page, 1)).toHaveAttribute('aria-posinset', '1');
    await expect(row(page, 1)).toHaveAttribute('aria-setsize', '2');
    await expect(row(page, 3)).toHaveAttribute('aria-level', '4');
    await expect(row(page, 3)).toHaveAttribute('aria-posinset', '1');
    await expect(row(page, 3)).toHaveAttribute('aria-setsize', '3');

    // Expandable rows expose aria-expanded; leaves carry none.
    await expect(row(page, 0)).toHaveAttribute('aria-expanded', 'true');
    await expect(row(page, 3)).not.toHaveAttribute('aria-expanded', /.*/);
  });

  test('@cross-engine the expander is pointer-only: tabindex -1 inside an aria-hidden slot', async ({
    page,
  }) => {
    await expect(expander(page, 0)).toHaveAttribute('tabindex', '-1');
    const hidden = await expander(page, 0).evaluate(
      (el) => el.closest('[aria-hidden="true"]') !== null,
    );
    expect(hidden).toBe(true);
  });

  test('@cross-engine the orphan and the cycle rows render as roots instead of vanishing', async ({
    page,
  }) => {
    await selectDepthZero(page); // 8 roots + placeholder
    expect(await cellText(page, 5, 0)).toBe('Loaded lazily');
    expect(await cellText(page, 6, 0)).toBe('Orphan account');
    expect(await cellText(page, 7, 0)).toBe('Cycle account A');
    await expect(row(page, 6)).toHaveAttribute('aria-level', '1');
    await expect(row(page, 7)).toHaveAttribute('aria-level', '1');

    // The cycle broke at its first member; the partner renders under it.
    await expander(page, 7).click();
    expect(await cellText(page, 8, 0)).toBe('Cycle account B');
    await expect(row(page, 8)).toHaveAttribute('aria-level', '2');
  });

  test('@cross-engine virtualization stays windowed over the expanded tree', async ({ page }) => {
    await expect(gridScroller(page)).toHaveAttribute('aria-rowcount', '60');
    const rendered = await renderedRows(page).count();
    expect(rendered).toBeGreaterThan(10);
    expect(rendered).toBeLessThan(30); // the window, never the model
  });

  test('@cross-engine readonly keeps the tree rendering with a uniform background (no zebra)', async ({
    page,
  }) => {
    await page.getByTestId('toggle-readonly').check();
    await expect(gridScroller(page)).toHaveAttribute('aria-rowcount', '59'); // placeholder gone
    const background = (locator: Locator): Promise<string> =>
      locator.evaluate((el) => getComputedStyle(el).backgroundColor);
    expect(await background(cell(page, 0, 1))).toBe(await background(cell(page, 1, 1)));
    expect(await background(cell(page, 1, 1))).toBe(await background(cell(page, 2, 1)));
  });
});

test.describe('expand & collapse', () => {
  test('@cross-engine expander click and Alt+ArrowRight/Left toggle the active row', async ({
    page,
  }) => {
    await expander(page, 0).click(); // collapse Assets (20-row subtree)
    await expect(gridScroller(page)).toHaveAttribute('aria-rowcount', '40');
    expect(await cellText(page, 1, 0)).toBe('Liabilities');
    await expect(row(page, 0)).toHaveAttribute('aria-expanded', 'false');

    // The expander press activated the row's hierarchy cell — Alt+Arrows
    // act on the active row from any column.
    await page.keyboard.press('Alt+ArrowRight');
    await expect(gridScroller(page)).toHaveAttribute('aria-rowcount', '60');
    await expect(row(page, 0)).toHaveAttribute('aria-expanded', 'true');

    await page.keyboard.press('Alt+ArrowLeft');
    await expect(gridScroller(page)).toHaveAttribute('aria-rowcount', '40');
  });

  test('@cross-engine collapsing an ancestor of the active cell moves activation to it', async ({
    page,
  }) => {
    await activateCell(page, 3, 2); // Petty cash, Balance column
    await expander(page, 0).click(); // collapse the Assets root above it

    await expect(activeCell(page)).toHaveAttribute('data-row', '0');
    await expect(cell(page, 0, 0)).toBeFocused();
  });

  test('@cross-engine expansion state survives a contentKey switch and a remount', async ({
    page,
  }) => {
    await expander(page, 0).click(); // content a: Assets collapsed
    await expect(gridScroller(page)).toHaveAttribute('aria-rowcount', '40');

    await page.getByTestId('switch-content').click(); // content b: fresh seed
    await expect(gridScroller(page)).toHaveAttribute('aria-rowcount', '60');

    await page.getByTestId('switch-content').click(); // back to a: restored
    await expect(gridScroller(page)).toHaveAttribute('aria-rowcount', '40');
    await expect(row(page, 0)).toHaveAttribute('aria-expanded', 'false');

    await page.getByTestId('toggle-mounted').uncheck(); // destroy…
    await expect(gridScroller(page)).toHaveCount(0);
    await page.getByTestId('toggle-mounted').check(); // …and remount
    await expect(gridScroller(page)).toHaveAttribute('aria-rowcount', '40');
    await expect(row(page, 0)).toHaveAttribute('aria-expanded', 'false');
  });
});

test.describe('lazy loading (§13.3)', () => {
  test('@cross-engine expanding the lazy root shows the reserved-slot spinner without layout shift, then the children', async ({
    page,
  }) => {
    await selectDepthZero(page);
    await page.getByTestId('lazy-delay').fill('800');

    const textSpan = cell(page, 5, 0).locator('.tm-grid__text');
    const before = await textSpan.boundingBox();
    expect(before).not.toBeNull();

    await expander(page, 5).click();
    await expect(childSpinner(page, 5)).toBeVisible();
    // The spinner filled RESERVED space: the cell content did not move.
    expect(await textSpan.boundingBox()).toEqual(before);
    // Not expanded while the load is in flight.
    await expect(row(page, 5)).toHaveAttribute('aria-expanded', 'false');
    await expect(gridScroller(page)).toHaveAttribute('aria-rowcount', '10');

    // The load lands: children render, expanded, spinner gone.
    await expect(gridScroller(page)).toHaveAttribute('aria-rowcount', '13');
    await expect(row(page, 5)).toHaveAttribute('aria-expanded', 'true');
    await expect(childSpinner(page, 5)).toHaveCount(0);
    expect(await cellText(page, 6, 0)).toBe('Lazy child one');
    await expect(row(page, 6)).toHaveAttribute('aria-level', '2');
    expect(await textSpan.boundingBox()).toEqual(before);
  });

  test('@cross-engine re-collapsing during the load wins; the next expand is instant', async ({
    page,
  }) => {
    await selectDepthZero(page);
    await page.getByTestId('lazy-delay').fill('600');

    await expander(page, 5).click(); // expand → load starts
    await expect(childSpinner(page, 5)).toBeVisible();
    await expander(page, 5).click(); // re-collapse while loading

    // The load continues and lands in the model…
    await expect
      .poll(async () =>
        (await modelJson<Account[]>(page)).some((account) => account.name === 'Lazy child one'),
      )
      .toBe(true);
    await expect(childSpinner(page, 5)).toHaveCount(0);
    // …but the node honors the collapse: nothing rendered beneath.
    await expect(gridScroller(page)).toHaveAttribute('aria-rowcount', '10');
    await expect(row(page, 5)).toHaveAttribute('aria-expanded', 'false');

    // Children are loaded now — expanding again renders them instantly.
    await expander(page, 5).click();
    await expect(gridScroller(page)).toHaveAttribute('aria-rowcount', '13');
    expect(await cellText(page, 6, 0)).toBe('Lazy child one');
  });

  test('@cross-engine a failed load announces and restores the collapsed state', async ({
    page,
  }) => {
    await selectDepthZero(page);
    await page.getByTestId('lazy-delay').fill('100');
    await page.getByTestId('lazy-fail-toggle').check();

    await expander(page, 5).click();
    await expect(liveRegion(page)).toContainText('Could not load child rows');
    await expect(childSpinner(page, 5)).toHaveCount(0);
    await expect(row(page, 5)).toHaveAttribute('aria-expanded', 'false');
    await expect(gridScroller(page)).toHaveAttribute('aria-rowcount', '10');
  });
});

test.describe('tree row operations', () => {
  test('@cross-engine menu Insert child row appends the last child, expanded and activated', async ({
    page,
  }) => {
    await activateCell(page, 6, 0); // Receivables (id 101, two children)
    await page.keyboard.press('Shift+F10');
    await page.getByRole('menuitem', { name: 'Insert child row', exact: true }).click();

    await expect.poll(async () => (await modelJson<Account[]>(page)).length).toBe(59);
    const accounts = await modelJson<Account[]>(page);
    const created = accounts[accounts.length - 1];
    expect(created.id).toBeLessThan(0); // minted temp id…
    expect(created.parentId).toBe(101); // …with the parent stamped

    // Last child of Receivables: after its two children in view order.
    await expect(activeCell(page)).toHaveAttribute('data-row', '9');
    await expect(activeCell(page)).toHaveAttribute('data-col', '0');
    await expect(row(page, 9)).toHaveAttribute('aria-level', '4');
    await expect(row(page, 9)).toHaveAttribute('aria-posinset', '3');
    await expect(row(page, 9)).toHaveAttribute('aria-setsize', '3');
    await expect(liveRegion(page)).toContainText('1 row inserted');
  });

  test('@cross-engine menu Delete rows removes the whole subtree', async ({ page }) => {
    const before = await modelJson<Account[]>(page);
    await setScrollTop(page, 320); // bring the Liabilities branch into the window
    await rowHeader(page, 23).click(); // Payables (id 200 + two children)
    await expect(cell(page, 23, 0)).toBeFocused(); // row select activates its first cell
    await page.keyboard.press('Shift+F10');
    await page.getByRole('menuitem', { name: 'Delete 1 row', exact: true }).click();

    await expect.poll(async () => (await modelJson<Account[]>(page)).length).toBe(
      before.length - 3,
    );
    const remaining = new Set((await modelJson<Account[]>(page)).map((account) => account.id));
    expect(remaining.has(200)).toBe(false);
    expect(remaining.has(2000)).toBe(false);
    expect(remaining.has(2001)).toBe(false);
    await expect(liveRegion(page)).toContainText('3 rows deleted');
    await expect(gridScroller(page)).toHaveAttribute('aria-rowcount', '57');
  });
});

test.describe('subtree moves & tree paste', () => {
  test.use({ permissions: ['clipboard-read', 'clipboard-write'] });

  test('a full-row cut/paste moves the whole subtree and re-parents it; one undo restores', async ({
    page,
  }) => {
    const before = await modelJson<Account[]>(page);

    await setScrollTop(page, 640); // rows ~16–38: source and target both rendered
    await rowHeader(page, 23).click(); // Payables subtree [200, 2000, 2001]
    await page.keyboard.press('Control+x');
    await expect(cell(page, 23, 0)).toHaveClass(/tm-grid__cell--cut/);

    await activateCell(page, 32, 0); // Share capital (id 30, a child of Equity)
    await page.keyboard.press('Control+v');

    // The move announces the CUT rows (the subtree travels implicitly).
    await expect(liveRegion(page)).toContainText('1 row moved');
    const moved = await modelJson<Account[]>(page);
    // The subtree re-spliced above the paste target, in DFS order…
    const targetIndex = moved.findIndex((account) => account.id === 30);
    expect(moved.slice(targetIndex - 3, targetIndex).map((account) => account.id)).toEqual([
      200, 2000, 2001,
    ]);
    // …the root re-parented to the target's parent, the children untouched.
    expect(moved.find((account) => account.id === 200)?.parentId).toBe(3);
    expect(moved.find((account) => account.id === 2000)?.parentId).toBe(200);
    expect(moved.find((account) => account.id === 2001)?.parentId).toBe(200);

    await pressUndo(page);
    await expect
      .poll(async () => JSON.stringify(await modelJson<Account[]>(page)))
      .toBe(JSON.stringify(before));
  });

  test('a move into the row own descendant is rejected with an announcement and no change', async ({
    page,
  }) => {
    const before = await modelJson<Account[]>(page);

    await rowHeader(page, 1).click(); // Current assets (id 10)
    await page.keyboard.press('Control+x');
    await activateCell(page, 3, 0); // Petty cash — inside the cut subtree
    await page.keyboard.press('Control+v');

    await expect(liveRegion(page)).toContainText('Cannot move a row into its own subtree');
    expect(JSON.stringify(await modelJson<Account[]>(page))).toBe(JSON.stringify(before));
  });
});

// Synthetic ClipboardEvents need no OS clipboard and no permissions, so
// this group runs on every engine (the Chromium-only clipboard permissions
// above would fail Firefox/WebKit context creation).
test.describe('tree paste (synthetic events)', () => {
  test('@cross-engine a cell paste maps flat onto visible rows across a parent/child boundary', async ({
    page,
  }) => {
    await activateCell(page, 0, 0); // Assets (level 1); the next row is its child
    await syntheticPaste(page, { text: 'PastedA\r\nPastedB' });

    await expect
      .poll(async () => {
        const accounts = await modelJson<Account[]>(page);
        return [
          accounts.find((account) => account.id === 1)?.name,
          accounts.find((account) => account.id === 10)?.name,
        ];
      })
      .toEqual(['PastedA', 'PastedB']);
  });

  test('@cross-engine an overflow paste at the tree end materializes SIBLINGS of the last row', async ({
    page,
  }) => {
    await setScrollTop(page, 5000); // clamped to the extent — the tree's end
    await activateCell(page, 57, 0); // Cycle account B (id 911, child of 910)
    await syntheticPaste(page, { text: 'S1\r\nS2\r\nS3' });

    await expect.poll(async () => (await modelJson<Account[]>(page)).length).toBe(60);
    const accounts = await modelJson<Account[]>(page);
    expect(accounts.find((account) => account.id === 911)?.name).toBe('S1');
    const s2 = accounts.find((account) => account.name === 'S2');
    const s3 = accounts.find((account) => account.name === 'S3');
    // Overflow rows continue the pasted list as SIBLINGS of the last
    // target row — same parent, factory-minted ids.
    expect(s2?.parentId).toBe(910);
    expect(s3?.parentId).toBe(910);
    expect(s2!.id).toBeLessThan(0);
    await expect(gridScroller(page)).toHaveAttribute('aria-rowcount', '62');
  });
});

test.describe('axe floor', () => {
  for (const theme of ['light', 'dark'] as const) {
    test(`@cross-engine the expanded tree is axe-clean (${theme})`, async ({ page }) => {
      await gotoGrid(page, 'tree-grid', { theme });
      await cell(page, 1, 1).click(); // active + selected state in the scan
      // Scoped to the component under test: WebKit resolves stale
      // custom-property colors on one STORY-TOOLBAR label under a cold
      // dark load (an engine style-recalc quirk in the showcase chrome,
      // reproducible with plain getComputedStyle — no library code
      // involved); the grid's own subtree scans clean everywhere.
      await expectNoAxeViolations(page, 'tm-tree-grid');
    });
  }
});

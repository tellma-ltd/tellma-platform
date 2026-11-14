// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { expect, test, type Page } from '@playwright/test';

import { expectNoAxeViolations } from '../support/axe';
import { appStable } from '../support/settle';
import { storyUrl } from '../support/story-map';

/**
 * Browser battery for the drag-drop layer, with native drags in every
 * engine (`@cross-engine`): a tree node dragged into a text area lands at
 * the drop point, and dragged onto a reorder list becomes a column there;
 * a drag never moves focus or the tree's active row. Plus the axe floor,
 * and the drop target's over state surviving forced colors.
 */

/** A leaf of the story's tree by its name. */
function treeNode(page: Page, name: string) {
  return page.getByTestId('drag-tree').getByRole('treeitem', { name, exact: true });
}

/** The draggable row of a tree node. */
function treeRow(page: Page, name: string) {
  return treeNode(page, name).locator(':scope > .tm-tree__row');
}

test.describe('axe floor', () => {
  for (const theme of ['light', 'dark'] as const) {
    for (const dir of ['ltr', 'rtl'] as const) {
      test(`drag-drop story is axe-clean (${theme}, ${dir})`, async ({ page }) => {
        await page.goto(storyUrl('drag-drop', { theme, dir }));
        await expect(treeNode(page, 'Amount')).toBeVisible();
        await expectNoAxeViolations(page);
      });
    }
  }
});

test('@cross-engine a tree node dragged into a text area lands at the drop point', async ({
  page,
}) => {
  await page.goto(storyUrl('drag-drop'));
  const textarea = page.getByTestId('drop-textarea');
  await expect(textarea).toHaveValue('Sum() + ');
  await expect(treeRow(page, 'Amount')).toHaveAttribute('draggable', 'true');
  await appStable(page);
  // Drop on the text's start: the path goes in front of what is there.
  await treeRow(page, 'Amount').dragTo(textarea, { targetPosition: { x: 2, y: 4 } });
  await expect(textarea).toHaveValue(/^Amount ?Sum\(\) \+ $/);
});

test('@cross-engine a tree node dragged onto a reorder list becomes a column there', async ({
  page,
}) => {
  await page.goto(storyUrl('drag-drop'));
  const list = page.getByTestId('drop-columns');
  await expect(page.getByTestId('drop-columns-order')).toHaveText('Columns: Name, Date');
  const rows = list.locator('.tm-reorder-list__row');
  await appStable(page);
  const second = (await rows.nth(1).boundingBox())!;
  // The upper half of the second row: the line falls between the two.
  await treeRow(page, 'Code')
    .first()
    .dragTo(rows.nth(1), {
      targetPosition: { x: second.width / 2, y: 2 },
    });
  await expect(page.getByTestId('drop-columns-order')).toHaveText(
    'Columns: Name, Agent.Code, Date',
  );
});

test('@cross-engine a drag changes neither focus nor the tree’s active row', async ({ page }) => {
  await page.goto(storyUrl('drag-drop'));
  const textarea = page.getByTestId('drop-textarea');
  await textarea.focus();
  await appStable(page);
  await treeRow(page, 'Amount').dragTo(page.getByTestId('drop-well'));
  await expect(page.getByTestId('drop-well-log')).toHaveText('Dropped: Amount');
  await expect(textarea).toBeFocused();
  await expect(treeNode(page, 'Agent')).toHaveAttribute('tabindex', '0');
  await expect(treeNode(page, 'Amount')).toHaveAttribute('tabindex', '-1');
});

test('the drop target keeps an outline under forced colors while a drag hovers', async ({
  page,
}) => {
  await page.emulateMedia({ forcedColors: 'active' });
  await page.goto(storyUrl('drag-drop'));
  const well = page.getByTestId('drop-well');
  await appStable(page);
  // A synthetic drag holds the hover state still, which a real one cannot.
  await page.evaluate(() => {
    const row = document.querySelector('[data-key="Amount"] > .tm-tree__row')!;
    const target = document.querySelector('[data-testid="drop-well"]')!;
    const data = new DataTransfer();
    row.dispatchEvent(new DragEvent('dragstart', { bubbles: true, dataTransfer: data }));
    target.dispatchEvent(
      new DragEvent('dragenter', { bubbles: true, cancelable: true, dataTransfer: data }),
    );
  });
  await expect(well).toHaveClass(/tm-drop-target--over/);
  const outline = await well.evaluate((element) => {
    const style = getComputedStyle(element);
    return { style: style.outlineStyle, width: style.outlineWidth };
  });
  expect(outline).toEqual({ style: 'dashed', width: '2px' });
});

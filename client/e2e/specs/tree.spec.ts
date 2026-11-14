// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { expect, test, type Page } from '@playwright/test';

import { expectNoAxeViolations } from '../support/axe';
import { heldResponses, holdResponses, releaseResponses } from '../support/held-responses';
import { appStable } from '../support/settle';
import { storyUrl } from '../support/story-map';

/**
 * Browser battery for tm-tree: the axe floor collapsed and expanded in
 * both themes and directions, the keyboard in both directions and across a
 * live switch to Arabic, activation, a lazy branch's spinner in its
 * reserved slot (nothing moves), and a failed load that collapses its node
 * and says so.
 */

/** The first tree: no selection, lazy branches. */
function fieldsTree(page: Page) {
  return page.getByTestId('tree-fields');
}

/** A node of the first tree by its key, whatever its name reads while it loads. */
function nodeByKey(page: Page, key: string) {
  return fieldsTree(page).locator(`[role="treeitem"][data-key="${key}"]`);
}

/** A node of a tree by its name. */
function node(page: Page, name: string, testId = 'tree-fields') {
  return page.getByTestId(testId).getByRole('treeitem', { name, exact: true });
}

test.describe('axe floor', () => {
  for (const theme of ['light', 'dark'] as const) {
    for (const dir of ['ltr', 'rtl'] as const) {
      test(`tree story is axe-clean, collapsed and expanded (${theme}, ${dir})`, async ({
        page,
      }) => {
        await page.goto(storyUrl('tree', { theme, dir }));
        await expect(node(page, 'Agent')).toBeVisible();
        await expectNoAxeViolations(page);
        await node(page, 'Agent').locator('.tm-tree__twisty').click();
        await expect(node(page, 'Parent')).toBeVisible();
        await expectNoAxeViolations(page);
      });
    }
  }
});

test('the tree is named, with one tab stop and nested levels', async ({ page }) => {
  await page.goto(storyUrl('tree'));
  const tree = fieldsTree(page).getByRole('tree', { name: 'Fields' });
  await expect(tree).toBeVisible();
  await expect(tree.locator('[role="treeitem"][tabindex="0"]')).toHaveCount(1);
  await expect(node(page, 'Agent')).toHaveAttribute('aria-expanded', 'false');
  await expect(node(page, 'Amount')).not.toHaveAttribute('aria-expanded');
  await node(page, 'Agent').locator('.tm-tree__twisty').click();
  await expect(node(page, 'Parent')).toHaveAttribute('aria-level', '2');
});

for (const dir of ['ltr', 'rtl'] as const) {
  test(`the arrows follow the reading direction (${dir})`, async ({ page }) => {
    await page.goto(storyUrl('tree', { dir }));
    const expand = dir === 'ltr' ? 'ArrowRight' : 'ArrowLeft';
    const collapse = dir === 'ltr' ? 'ArrowLeft' : 'ArrowRight';
    await node(page, 'Agent').focus();
    await page.keyboard.press(expand);
    await expect(page.getByTestId('tree-expanded')).toHaveText('Expanded: Agent');
    await page.keyboard.press(expand);
    await expect(node(page, 'Code').first()).toBeFocused();
    await page.keyboard.press(collapse);
    await expect(node(page, 'Agent')).toBeFocused();
    await page.keyboard.press(collapse);
    await expect(page.getByTestId('tree-expanded')).toHaveText('Expanded: none');
    await page.keyboard.press('End');
    await expect(node(page, 'Date')).toBeFocused();
  });
}

// The app switches English to Arabic while the tree is on screen: aria's
// direction is live, so the arrows remap without a reload.
test('a live English to Arabic switch remaps the arrows', async ({ page }) => {
  await page.goto(storyUrl('tree'));
  await node(page, 'Agent').focus();
  await page.keyboard.press('ArrowRight');
  await expect(page.getByTestId('tree-expanded')).toHaveText('Expanded: Agent');

  await page.getByTestId('lang-ar').click();
  await expect(page.locator('html')).toHaveAttribute('dir', 'rtl');
  await node(page, 'Agent').focus();
  await page.keyboard.press('ArrowRight');
  await expect(page.getByTestId('tree-expanded')).toHaveText('Expanded: none');
  await page.keyboard.press('ArrowLeft');
  await expect(page.getByTestId('tree-expanded')).toHaveText('Expanded: Agent');
});

test('Enter and a double-click on a leaf activate; a double-click on a branch expands', async ({
  page,
}) => {
  await page.goto(storyUrl('tree'));
  await node(page, 'Amount').focus();
  await page.keyboard.press('Enter');
  await expect(page.getByTestId('tree-activated')).toHaveText('Activated: Amount');
  await node(page, 'Code').dblclick();
  await expect(page.getByTestId('tree-activated')).toHaveText('Activated: Code');
  await node(page, 'Agent').locator('.tm-tree__content').first().dblclick();
  await expect(page.getByTestId('tree-expanded')).toHaveText('Expanded: Agent');
  await expect(page.getByTestId('tree-activated')).toHaveText('Activated: Code');
});

test('a click selects in single mode and toggles in multiple mode', async ({ page }) => {
  await page.goto(storyUrl('tree'));
  await node(page, 'Amount', 'tree-single').click();
  await node(page, 'Date', 'tree-single').click();
  await expect(page.getByTestId('tree-single-selected')).toHaveText('Selected: Date');
  await expect(node(page, 'Date', 'tree-single')).toHaveAttribute('aria-selected', 'true');

  await node(page, 'Amount', 'tree-multiple').click();
  await expect(page.getByTestId('tree-multiple-selected')).toHaveText('Selected: Code, Amount');
  await node(page, 'Code', 'tree-multiple').last().click();
  await expect(page.getByTestId('tree-multiple-selected')).toHaveText('Selected: Amount');
});

test('a lazy branch spins in its reserved slot while it loads, and nothing moves', async ({
  page,
}) => {
  await page.goto(storyUrl('tree'));
  await holdResponses(page, 'children');
  await node(page, 'Lines').locator('.tm-tree__twisty').click();
  const entries = nodeByKey(page, 'Lines.Entries');
  await expect(entries).toBeVisible();
  await expect(heldResponses(page, 'children')).not.toHaveText('0');
  await appStable(page);
  const content = entries.locator('.tm-tree__content').first();
  const before = await content.boundingBox();
  await entries.locator('.tm-tree__twisty').first().click();
  await expect(entries).toHaveAttribute('aria-busy', 'true');
  await expect(entries.locator('tm-spinner')).toBeVisible();
  await expect(entries).toHaveAccessibleName('Entries Loading');
  await appStable(page);
  expect(await content.boundingBox()).toEqual(before);

  await releaseResponses(page, 'children');
  await expect(entries).not.toHaveAttribute('aria-busy');
  await expect(entries.getByRole('treeitem', { name: 'Code' })).toBeVisible();
  await expect(entries.locator('tm-spinner')).toHaveCount(0);
  expect(await content.boundingBox()).toEqual(before);
});

test('a failed load collapses its node and announces it', async ({ page }) => {
  await page.goto(storyUrl('tree'));
  await holdResponses(page, 'children');
  await node(page, 'Lines').locator('.tm-tree__twisty').click();
  const archive = nodeByKey(page, 'Lines.Archive');
  await expect(archive).toBeVisible();
  await archive.locator('.tm-tree__twisty').click();
  await expect(archive).toHaveAttribute('aria-expanded', 'true');
  await releaseResponses(page, 'children');
  await expect(archive).toHaveAttribute('aria-expanded', 'false');
  await expect(page.locator('.cdk-live-announcer-element')).toHaveText("Couldn't load Archive");
});

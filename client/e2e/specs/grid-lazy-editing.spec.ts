// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { expect, test, type Locator, type Page } from '@playwright/test';

import { activateCell, cell, editorInput, gridScroller } from '../support/grid';
import { storyUrl } from '../support/story-map';

// The window between an editable grid asking for its on-demand editing code
// and that code arriving, with real browser input. The story holds the code
// back until the test releases it, so the window stays open as long as each
// test needs. The app is not stable while the code's load is pending, so the
// page is opened without waiting for it to settle, and the model dump is read
// straight from the page.

/** The draft the editing cell shows while the code is on its way. */
function draft(page: Page): Locator {
  return page.locator('[data-tm-draft]');
}

/** The story's model dump. */
function model(page: Page): Locator {
  return page.getByTestId('model-json');
}

/** The story's release hook on the page's window. */
interface ReleaseHookWindow {
  /** Lets the story grid's editing code arrive. */
  readonly tmShowcaseReleaseGridEditing?: () => void;
}

/** Lets the editing code arrive. */
async function release(page: Page): Promise<void> {
  await page.evaluate(() => (window as ReleaseHookWindow).tmShowcaseReleaseGridEditing?.());
}

test.beforeEach(async ({ page }) => {
  await page.goto(storyUrl('grid-lazy-editing'));
  await expect(gridScroller(page)).toBeVisible();
  await activateCell(page, 0, 0);
});

test('typing while the code loads shows a draft, and the editor opens with it, caret at the end', async ({
  page,
}) => {
  await page.keyboard.type('Repairs');
  await expect(draft(page)).toHaveText('Repairs');
  await expect(cell(page, 0, 0)).toHaveClass(/tm-grid__cell--editing/);
  await expect(editorInput(page)).toHaveCount(0);

  await release(page);
  await expect(editorInput(page)).toHaveValue('Repairs');
  await expect(editorInput(page)).toBeFocused();
  await expect(draft(page)).toHaveCount(0);
  // Typing carries on in the editor.
  await page.keyboard.type(' done');
  await page.keyboard.press('Enter');
  await expect(model(page)).toContainText('"description": "Repairs done"');
});

test('Enter while the code loads commits at once and moves down; the next key opens the next draft', async ({
  page,
}) => {
  await page.keyboard.type('Rent');
  await page.keyboard.press('Enter');
  await expect(model(page)).toContainText('"description": "Rent"');
  await expect(cell(page, 1, 0)).toBeFocused();
  await expect(draft(page)).toHaveCount(0);

  await page.keyboard.type('X');
  await expect(draft(page)).toHaveText('X');
  await release(page);
  await expect(editorInput(page)).toHaveValue('X');
});

test('Escape while the code loads drops the draft at once', async ({ page }) => {
  await page.keyboard.type('Oops');
  await page.keyboard.press('Escape');
  await expect(draft(page)).toHaveCount(0);
  await expect(cell(page, 0, 0)).not.toHaveClass(/tm-grid__cell--editing/);
  await expect(cell(page, 0, 0)).toBeFocused();
  await expect(model(page)).toContainText('"description": "Consulting"');
});

test('a click outside the grid while the code loads commits the draft', async ({ page }) => {
  await page.keyboard.type('Travel');
  await page.getByTestId('outside-button').click();
  await expect(model(page)).toContainText('"description": "Travel"');
  await expect(draft(page)).toHaveCount(0);

  // The arrival leaves focus where the user put it.
  await release(page);
  await expect(page.getByTestId('outside-button')).toBeFocused();
});

test('a click on another cell while the code loads commits the draft and focuses that cell', async ({
  page,
}) => {
  await page.keyboard.type('7');
  await cell(page, 2, 1).click();
  await expect(model(page)).toContainText('"description": "7"');
  await expect(cell(page, 2, 1)).toBeFocused();
  // The press really lands: the next key types into that cell's draft.
  await page.keyboard.type('4');
  await expect(draft(page)).toHaveText('4');
  await page.keyboard.press('Enter');
  await expect(model(page)).toContainText('"quantity": 4');
});

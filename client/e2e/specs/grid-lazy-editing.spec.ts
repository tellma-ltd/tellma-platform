// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { expect, test, type Locator, type Page } from '@playwright/test';

import { activateCell, cell, draftInput, editorInput } from '../support/grid';
import { failEditingCode, gotoLazyEditing, releaseEditingCode } from '../support/lazy-editing';

// The window between an editable grid asking for its on-demand editing code
// and that code arriving, with trusted browser input: the keydowns a real
// keyboard sends around a character (Shift, Control, AltGraph) included.
// The story holds the code back until the test releases or fails it, so the
// window stays open as long as each test needs. The app is not stable while
// the request is open, so the model dump is read straight from the page.

/** The story's model dump. */
function model(page: Page): Locator {
  return page.getByTestId('model-json');
}

test.beforeEach(async ({ page }) => {
  await gotoLazyEditing(page);
  await activateCell(page, 0, 0);
});

test('Shift, AltGr and accented characters typed while the code loads all land, and the editor takes them over', async ({
  page,
}) => {
  await page.keyboard.type('ab');
  await page.keyboard.down('Shift');
  await page.keyboard.press('KeyC');
  await page.keyboard.up('Shift');
  // Windows sends Control, then AltGraph, before an AltGr character.
  await page.keyboard.down('Control');
  await page.keyboard.down('AltGraph');
  await page.keyboard.insertText('@');
  await page.keyboard.up('AltGraph');
  await page.keyboard.up('Control');
  await page.keyboard.type('dé');
  await expect(draftInput(page)).toHaveValue('abC@dé');
  await expect(draftInput(page)).toBeFocused();
  await expect(cell(page, 0, 0)).toHaveClass(/tm-grid__cell--editing/);
  await expect(editorInput(page)).toHaveCount(0);

  // A slow network: the code takes its time, and the draft keeps typing.
  await page.waitForTimeout(500);
  await page.keyboard.type(' ok');
  await releaseEditingCode(page);
  await expect(editorInput(page)).toHaveValue('abC@dé ok');
  await expect(editorInput(page)).toBeFocused();
  await expect(draftInput(page)).toHaveCount(0);
  // Typing carries on in the editor, at the caret the draft left.
  await page.keyboard.type('!');
  await page.keyboard.press('Enter');
  await expect(model(page)).toContainText('"description": "abC@dé ok!"');
});

test('in an F2 draft the arrows move the caret, as in the editor', async ({ page }) => {
  await page.keyboard.press('F2');
  await expect(draftInput(page)).toHaveValue('Consulting');
  await page.keyboard.press('ArrowLeft');
  await page.keyboard.press('ArrowLeft');
  await page.keyboard.type('X');
  await expect(draftInput(page)).toHaveValue('ConsultiXng');
  await page.keyboard.press('Enter');
  await expect(model(page)).toContainText('"description": "ConsultiXng"');
  await expect(cell(page, 1, 0)).toBeFocused();
});

test('Enter while the code loads commits at once and moves down; the next key opens the next draft', async ({
  page,
}) => {
  await page.keyboard.type('Rent');
  await page.keyboard.press('Enter');
  await expect(model(page)).toContainText('"description": "Rent"');
  await expect(cell(page, 1, 0)).toBeFocused();
  await expect(draftInput(page)).toHaveCount(0);

  await page.keyboard.type('X');
  await expect(draftInput(page)).toHaveValue('X');
  await releaseEditingCode(page);
  await expect(editorInput(page)).toHaveValue('X');
});

test('Escape while the code loads drops the draft at once', async ({ page }) => {
  await page.keyboard.type('Oops');
  await page.keyboard.press('Escape');
  await expect(draftInput(page)).toHaveCount(0);
  await expect(cell(page, 0, 0)).not.toHaveClass(/tm-grid__cell--editing/);
  await expect(cell(page, 0, 0)).toBeFocused();
  await expect(model(page)).toContainText('"description": "Consulting"');
});

test("undo in a draft is the input's own: it never reverts an earlier edit", async ({ page }) => {
  await page.keyboard.type('Rent');
  await page.keyboard.press('Enter'); // an edit on the grid's history
  await expect(model(page)).toContainText('"description": "Rent"');

  await page.keyboard.type('xyz');
  await page.keyboard.press('ControlOrMeta+z');
  await page.keyboard.press('ControlOrMeta+z');
  await expect(draftInput(page)).toHaveCount(1);
  await expect(model(page)).toContainText('"description": "Rent"');
  await expect(model(page)).toContainText('"description": "Hardware"');
});

test('a click outside the grid while the code loads commits the draft', async ({ page }) => {
  await page.keyboard.type('Travel');
  await page.getByTestId('outside-button').click();
  await expect(model(page)).toContainText('"description": "Travel"');
  await expect(draftInput(page)).toHaveCount(0);

  // The arrival leaves focus where the user put it.
  await releaseEditingCode(page);
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
  await expect(draftInput(page)).toHaveValue('4');
  await page.keyboard.press('Enter');
  await expect(model(page)).toContainText('"quantity": 4');
});

test("an entity draft committed before the code arrives resolves through the column's search", async ({
  page,
}) => {
  // The agent column has a search and no resolver.
  await activateCell(page, 1, 2);
  await page.keyboard.type('Bob Stone');
  await page.keyboard.press('Enter');
  // A name two agents share is ambiguous, as the editor would find it.
  await page.keyboard.type('Dana Reed');
  await page.keyboard.press('Enter');
  await expect(cell(page, 1, 2)).not.toHaveClass(/tm-grid__cell--error/);

  await releaseEditingCode(page);
  await expect(model(page)).toContainText('"agentId": 2');
  await expect(cell(page, 1, 2)).not.toHaveClass(/tm-grid__cell--error/);
  await expect(cell(page, 2, 2)).toHaveClass(/tm-grid__cell--error/);
});

test('a failed load leaves the draft whole, reports once, and the next gesture asks again', async ({
  page,
}) => {
  const errors: string[] = [];
  page.on('console', (message) => {
    if (message.type() === 'error') {
      errors.push(message.text());
    }
  });
  await page.keyboard.type('Lon');
  await failEditingCode(page);
  await expect.poll(() => errors.filter((text) => text.includes('unreachable')).length).toBe(1);

  // The draft is untouched and still takes typing; each key asks again.
  await page.keyboard.type('g');
  await failEditingCode(page);
  await page.keyboard.type(' run');
  await expect(draftInput(page)).toHaveValue('Long run');
  await expect(draftInput(page)).toBeFocused();
  await page.keyboard.press('Enter');
  await expect(model(page)).toContainText('"description": "Long run"');
  expect(errors.filter((text) => text.includes('unreachable'))).toHaveLength(1);

  // This time the code arrives, and the next open is an editor.
  await releaseEditingCode(page);
  await page.keyboard.press('F2');
  await expect(editorInput(page)).toHaveValue('Hardware');
  await expect(editorInput(page)).toBeFocused();
});

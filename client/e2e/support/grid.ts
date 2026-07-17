// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { expect, type Locator, type Page } from '@playwright/test';

import { storyUrl, type StoryUrlOptions } from './story-map';

/**
 * Shared locators and gestures for the tm-grid Playwright battery. All
 * positions are the grid's own view-space coordinates (`data-row` /
 * `data-col` on cells and headers), which are model indices — stable under
 * virtualization regardless of what happens to be rendered.
 */

/** Navigates to a grid story and waits for the grid chrome to render. */
export async function gotoGrid(page: Page, id: string, options: StoryUrlOptions = {}): Promise<void> {
  await page.goto(storyUrl(id, options));
  await expect(page.locator('[role="grid"]')).toBeVisible();
}

/** The grid's scroll container (the `role="grid"` element). */
export function gridScroller(page: Page): Locator {
  return page.locator('[role="grid"]');
}

/** The cell at a view-space row × data-column position. */
export function cell(page: Page, row: number, col: number): Locator {
  return page.locator(`[data-tm-cell][data-row="${row}"][data-col="${col}"]`);
}

/** The one roving-tabindex cell (the active cell, while focus is not escaped). */
export function activeCell(page: Page): Locator {
  return page.locator('[data-tm-cell][tabindex="0"]');
}

/** The row header at a view-space row. */
export function rowHeader(page: Page, row: number): Locator {
  return page.locator(`[data-tm-rowhdr][data-row="${row}"]`);
}

/** The column header at a data-column index. */
export function colHeader(page: Page, col: number): Locator {
  return page.locator(`[data-tm-colhdr][data-col="${col}"]`);
}

/** Every rendered body row (the window slice plus any active-row outlier). */
export function renderedRows(page: Page): Locator {
  return page.locator('.tm-grid__row');
}

/** Every rendered cell inside a selection range (`aria-selected="true"`). */
export function selectedCells(page: Page): Locator {
  return page.locator('[data-tm-cell][aria-selected="true"]');
}

/** The CDK live region the grid announces through. */
export function liveRegion(page: Page): Locator {
  return page.locator('.cdk-live-announcer-element');
}

/** A cell's display text as the user sees it (visually-hidden text included). */
export async function cellText(page: Page, row: number, col: number): Promise<string> {
  return (await cell(page, row, col).innerText()).trim();
}

/** The resolved `--grid-row-height` in px, measured from the sticky header band. */
export async function rowHeightOf(page: Page): Promise<number> {
  const box = await page.locator('.tm-grid__header').boundingBox();
  if (box === null) {
    throw new Error('grid header not rendered');
  }
  return box.height;
}

/** Sets the scroller's scrollTop directly (the scroll event drives the window). */
export async function setScrollTop(page: Page, top: number): Promise<void> {
  await gridScroller(page).evaluate((el, y) => {
    el.scrollTop = y;
  }, top);
}

/** The scroller's current scrollTop. */
export async function scrollTopOf(page: Page): Promise<number> {
  return gridScroller(page).evaluate((el) => el.scrollTop);
}

/** The scroller's inner viewport height (what the page-size math uses). */
export async function clientHeightOf(page: Page): Promise<number> {
  return gridScroller(page).evaluate((el) => el.clientHeight);
}

/** The center point of a locator's bounding box. */
export async function centerOf(locator: Locator): Promise<{ x: number; y: number }> {
  const box = await locator.boundingBox();
  if (box === null) {
    throw new Error('element has no bounding box');
  }
  return { x: box.x + box.width / 2, y: box.y + box.height / 2 };
}

/** Drags with real pointer events from one locator's center to another's. */
export async function dragBetween(page: Page, from: Locator, to: Locator): Promise<void> {
  const start = await centerOf(from);
  const end = await centerOf(to);
  await page.mouse.move(start.x, start.y);
  await page.mouse.down();
  await page.mouse.move(end.x, end.y, { steps: 8 });
  await page.mouse.up();
}

/**
 * Reads both flavors off the real system clipboard through the async
 * Clipboard API. Chromium-only: requires `clipboard-read` permission
 * (granted per test file via `test.use`). Note that Chromium SANITIZES the
 * `text/html` flavor on read (custom `data-*` attributes are stripped), so
 * HTML-metadata assertions belong to the synthetic-event tests; this
 * reader is for `text/plain` fidelity and flavor presence.
 */
export async function readClipboard(page: Page): Promise<{ text: string; html: string }> {
  return page.evaluate(async () => {
    const items = await navigator.clipboard.read();
    let text = '';
    let html = '';
    for (const item of items) {
      if (item.types.includes('text/plain')) {
        text = await (await item.getType('text/plain')).text();
      }
      if (item.types.includes('text/html')) {
        html = await (await item.getType('text/html')).text();
      }
    }
    return { text, html };
  });
}

/**
 * Dispatches a synthetic `copy` ClipboardEvent at the grid and returns what
 * the grid wrote into its DataTransfer — full-fidelity flavor capture that
 * works in every engine (no system clipboard, no permissions).
 */
export async function syntheticCopy(page: Page): Promise<{ text: string; html: string }> {
  return gridScroller(page).evaluate((el) => {
    const data = new DataTransfer();
    const event = new ClipboardEvent('copy', { clipboardData: data, bubbles: true, cancelable: true });
    el.dispatchEvent(event);
    return { text: data.getData('text/plain'), html: data.getData('text/html') };
  });
}

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
  await expect(gridScroller(page)).toBeVisible();
}

/** The grid's scroll container (`role="grid"`, or `treegrid` on trees). */
export function gridScroller(page: Page): Locator {
  return page.locator('[role="grid"], [role="treegrid"]');
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

/** The select-all tri-state checkbox in the checkbox column's header (§8.8). */
export function checkAllBox(page: Page): Locator {
  return page.locator('[data-tm-checkall]');
}

/** The checkbox chrome cell at a view-space row (`selectable` grids). */
export function checkCell(page: Page, row: number): Locator {
  return page.locator(`[data-tm-checkcell][data-row="${row}"]`);
}

/** The row checkbox widget inside the chrome cell at a view-space row. */
export function rowCheckbox(page: Page, row: number): Locator {
  return checkCell(page, row).locator('.tm-grid__check');
}

/** The find bar (`searchable` grids, rendered only while open). */
export function findBar(page: Page): Locator {
  return page.locator('.tm-grid-find-bar');
}

/** The find bar's text input. */
export function findInput(page: Page): Locator {
  return page.locator('[data-tm-find-input]');
}

/** The find bar's match counter ('3 of 41' / 'No matches'). */
export function findCounter(page: Page): Locator {
  return page.locator('[data-tm-find-counter]');
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
 * Clicks a cell and waits for the roving focus to land on it. The focus
 * handoff happens on the render after the activating pointerdown, so a key
 * pressed immediately after a bare `click()` can still target the
 * previously-focused element (the page body on a fresh load) and never
 * reach the grid — always settle before typing.
 */
export async function activateCell(page: Page, row: number, col: number): Promise<void> {
  await cell(page, row, col).click();
  await expect(cell(page, row, col)).toBeFocused();
}

/** The open editor's container inside the editing cell. */
export function editor(page: Page): Locator {
  return page.locator('[data-tm-editor]');
}

/**
 * The open editor's input (the built-in text editor, or any input-hosting
 * registered editor).
 */
export function editorInput(page: Page): Locator {
  return page.locator('[data-tm-editor] input');
}

/** The editable status bar's error tally chip. */
export function statusChip(page: Page): Locator {
  return page.locator('[data-tm-status-chip]');
}

/**
 * Parses the story's live model dump (`data-testid="model-json"` — the
 * reactive JSON.stringify of the bound rows array).
 */
export async function modelJson<T>(page: Page): Promise<T> {
  const text = await page.getByTestId('model-json').textContent();
  return JSON.parse(text ?? 'null') as T;
}

/** The caret position (`selectionStart`) of the open editor's input. */
export async function editorCaret(page: Page): Promise<number | null> {
  return editorInput(page).evaluate((el) => (el as HTMLInputElement).selectionStart);
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
 *
 * Engine note: the flavors are captured by intercepting `setData` DURING
 * dispatch rather than read back afterwards — Firefox flips a synthetic
 * DataTransfer to protected mode once the copy event finishes, so a
 * post-dispatch `getData` returns `''` there (Chromium/WebKit read fine
 * either way).
 */
export async function syntheticCopy(page: Page): Promise<{ text: string; html: string }> {
  return gridScroller(page).evaluate((el) => {
    const data = new DataTransfer();
    const event = new ClipboardEvent('copy', { clipboardData: data, bubbles: true, cancelable: true });
    const captured: Record<string, string> = {};
    const target = event.clipboardData ?? data;
    const original = target.setData.bind(target);
    Object.defineProperty(target, 'setData', {
      configurable: true,
      value: (type: string, value: string) => {
        captured[type] = value;
        original(type, value);
      },
    });
    el.dispatchEvent(event);
    return {
      text: captured['text/plain'] ?? data.getData('text/plain'),
      html: captured['text/html'] ?? data.getData('text/html'),
    };
  });
}

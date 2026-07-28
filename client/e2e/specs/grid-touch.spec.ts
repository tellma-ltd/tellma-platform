// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { expect, test, type Locator, type Page } from '@playwright/test';

import {
  activeCell,
  cell,
  centerOf,
  editor,
  editorInput,
  gotoGrid,
  gridScroller,
  scrollTopOf,
  selectedCells,
} from '../support/grid';

/**
 * The touch battery (spec 0004 §8.6, DoD 16) — runs ONLY on the `touch`
 * project (Pixel 7 descriptor: chromium engine, coarse pointer, hasTouch;
 * the desktop projects exclude /grid-touch/). Tap-to-activate through the
 * synthesized click, double-tap editing, the long-press context menu,
 * native pan scrolling that never selects, and the coarse-pointer range
 * handles: rendering, ≥24px hit areas, extend-by-drag with pan-keeps-
 * scrolling, and hiding while an editor is open.
 *
 * Gestures are honest touch — Playwright's high-level API has no touch
 * drag or long-press, so beyond `locator.tap()` (touchstart/touchend →
 * synthesized click) the file drives CDP `Input.dispatchTouchEvent`:
 *
 * - touchStart/touchMove/touchEnd sequences for pans (they drive native
 *   scrolling — but only from anchors INSIDE the visual viewport, touches
 *   below the fold land on nothing) and for handle drags (the handle is
 *   `touch-action: none`, so pointermoves keep firing under capture).
 * - A dispatched `touchStart` + held finger for the long-press, released
 *   with `touchCancel`: a real gesture recognizer cancels the pending tap
 *   once the long-press fires (no trailing click), while a raw `touchEnd`
 *   synthesizes an artifact click that no real device produces after a
 *   consumed long-press (it would immediately outside-click the menu).
 */

interface Point {
  readonly x: number;
  readonly y: number;
}

/** A slow single-finger drag (no fling) through CDP touch events. */
async function touchDrag(page: Page, from: Point, to: Point, steps = 12): Promise<void> {
  const client = await page.context().newCDPSession(page);
  try {
    await client.send('Input.dispatchTouchEvent', {
      type: 'touchStart',
      touchPoints: [{ x: from.x, y: from.y, id: 1 }],
    });
    for (let i = 1; i <= steps; i++) {
      await client.send('Input.dispatchTouchEvent', {
        type: 'touchMove',
        touchPoints: [
          {
            x: from.x + ((to.x - from.x) * i) / steps,
            y: from.y + ((to.y - from.y) * i) / steps,
            id: 1,
          },
        ],
      });
    }
    await client.send('Input.dispatchTouchEvent', { type: 'touchEnd', touchPoints: [] });
  } finally {
    await client.detach();
  }
}

/**
 * A finger pan over the grid: an upward swipe (scrolls the content down)
 * dragged from an anchor at a height fraction of the grid, scrolled into
 * view first and clamped inside the visual viewport — the story page is
 * taller than the phone viewport, and touches dispatched below the fold
 * land on nothing.
 */
async function touchPan(page: Page, fraction: number, distanceY: number): Promise<void> {
  await gridScroller(page).scrollIntoViewIfNeeded();
  const box = (await gridScroller(page).boundingBox())!;
  const viewportHeight = await page.evaluate(() => window.innerHeight);
  const y = Math.min(box.y + box.height * fraction, viewportHeight - 24, box.y + box.height - 24);
  const from = { x: box.x + box.width / 2, y: Math.max(y, box.y + 60) };
  await touchDrag(page, from, { x: from.x, y: from.y - distanceY });
}

/**
 * Presses a finger down and HOLDS it until `until` resolves (the grid's
 * 500ms long-press timer fires while the finger is still on the glass —
 * the expectation doubles as the hold, no sleeps), then lifts it as a
 * CANCELLED tap, the way a real gesture recognizer ends a consumed
 * long-press (no synthesized click).
 */
async function touchHold(page: Page, point: Point, until: () => Promise<void>): Promise<void> {
  const client = await page.context().newCDPSession(page);
  try {
    await client.send('Input.dispatchTouchEvent', {
      type: 'touchStart',
      touchPoints: [{ x: point.x, y: point.y, id: 1 }],
    });
    await until();
    await client.send('Input.dispatchTouchEvent', { type: 'touchCancel', touchPoints: [] });
  } finally {
    await client.detach();
  }
}

/** The coarse-pointer range handles. */
function handles(page: Page): Locator {
  return page.locator('[data-tm-handle]');
}

function endHandle(page: Page): Locator {
  return page.locator('[data-tm-handle="end"]');
}

function menuPanel(page: Page): Locator {
  return page.locator('.tm-menu__panel');
}

/** The gap between a point and a rectangle (0 while the point is inside). */
function distanceToBox(
  point: Point,
  box: { x: number; y: number; width: number; height: number },
): number {
  const dx = Math.max(box.x - point.x, 0, point.x - (box.x + box.width));
  const dy = Math.max(box.y - point.y, 0, point.y - (box.y + box.height));
  return Math.hypot(dx, dy);
}

test.describe('tap, pan, and the selection handles (grid-list-screen)', () => {
  test.beforeEach(async ({ page }) => {
    await gotoGrid(page, 'grid-list-screen');
  });

  test('the device presents a coarse pointer; no handles render before a selection', async ({
    page,
  }) => {
    expect(await page.evaluate(() => matchMedia('(pointer: coarse)').matches)).toBe(true);
    await expect(handles(page)).toHaveCount(0);
  });

  test('a tap activates and selects the cell through the synthesized click', async ({ page }) => {
    await cell(page, 2, 1).tap();

    await expect(cell(page, 2, 1)).toBeFocused();
    await expect(cell(page, 2, 1)).toHaveAttribute('aria-selected', 'true');
    await expect(selectedCells(page)).toHaveCount(1);
    await expect(editor(page)).toHaveCount(0); // a single tap never edits
  });

  test('a vertical pan scrolls the grid and creates no selection', async ({ page }) => {
    await touchPan(page, 0.6, 250);

    await expect
      .poll(() => scrollTopOf(page), { message: 'the pan never scrolled the grid' })
      .toBeGreaterThan(50);
    await expect(selectedCells(page)).toHaveCount(0); // finger-drag on cells ≠ range select
  });

  test('a tapped selection shows two drag handles with ≥24px hit areas', async ({ page }) => {
    await cell(page, 2, 0).tap();

    await expect(handles(page)).toHaveCount(2);
    for (const edge of ['start', 'end'] as const) {
      const box = (await page.locator(`[data-tm-handle="${edge}"]`).boundingBox())!;
      expect(box.width).toBeGreaterThanOrEqual(24);
      expect(box.height).toBeGreaterThanOrEqual(24);
    }
  });

  test('dragging the end handle extends the range; a pan elsewhere still scrolls', async ({
    page,
  }) => {
    await cell(page, 1, 0).tap();
    await expect(handles(page)).toHaveCount(2);

    // Down three rows and into the next column: the range grows under the
    // finger through the handle's pointer-capture drag pipeline.
    const grip = await centerOf(endHandle(page));
    await touchDrag(page, grip, await centerOf(cell(page, 4, 1)));

    await expect(selectedCells(page)).toHaveCount(8); // rows 1–4 × cols 0–1
    await expect(cell(page, 1, 0)).toHaveAttribute('aria-selected', 'true');
    await expect(cell(page, 4, 1)).toHaveAttribute('aria-selected', 'true');

    // A plain pan away from the handles still scrolls (small and flingless
    // so the selected rows stay inside the rendered window).
    await touchPan(page, 0.8, 80);
    await expect
      .poll(() => scrollTopOf(page), { message: 'the pan never scrolled the grid' })
      .toBeGreaterThan(20);
    await expect(selectedCells(page)).toHaveCount(8); // the pan did not re-select
  });
});

test.describe('touch editing (grid-editable)', () => {
  test.beforeEach(async ({ page }) => {
    await gotoGrid(page, 'grid-editable');
  });

  test('a double-tap opens the editor; the handles hide while it is open', async ({ page }) => {
    await cell(page, 2, 0).tap(); // activate (and summon the handles)
    await expect(handles(page)).toHaveCount(2);

    await cell(page, 2, 0).tap(); // the second tap of the pair → dblclick → edit
    await expect(editorInput(page)).toBeVisible();
    await expect(editorInput(page)).toBeFocused();
    await expect(handles(page)).toHaveCount(0); // hidden behind the editor

    await page.keyboard.press('Escape'); // cancel: selection intact, handles back
    await expect(editor(page)).toHaveCount(0);
    await expect(handles(page)).toHaveCount(2);
  });

  test('a long-press opens the context menu at the press point', async ({ page }) => {
    const point = await centerOf(cell(page, 5, 1));
    await touchHold(page, point, async () => {
      // The 500ms hold fires the grid's long-press (native menu suppressed;
      // the library menu opening IS what proves the gesture landed).
      await expect(menuPanel(page)).toBeVisible();
    });

    await expect(menuPanel(page)).toBeVisible();
    const box = (await menuPanel(page).boundingBox())!;
    expect(distanceToBox(point, box)).toBeLessThan(60); // anchored at the finger
    // The pressed cell became the selection target (Excel semantics).
    await expect(activeCell(page)).toHaveAttribute('data-row', '5');
    await expect(activeCell(page)).toHaveAttribute('data-col', '1');

    await page.keyboard.press('Escape');
    await expect(menuPanel(page)).toHaveCount(0);
  });
});

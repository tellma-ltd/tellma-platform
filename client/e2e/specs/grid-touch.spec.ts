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
 * - Timestamped touchStart/touchEnd pairs for the double-tap: the browser
 *   counts taps by event time, so the pair repeats (or not) by
 *   construction, however slowly the runner dispatches it.
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

/**
 * The gap, in seconds of event time, between the two taps of a double-tap:
 * well inside the browser's double-tap window (a few hundred ms).
 */
const DOUBLE_TAP_GAP_S = 0.12;

/**
 * A gap, in seconds of event time, that no double-tap window spans: two
 * taps this far apart are two single taps.
 */
const SEPARATE_TAPS_GAP_S = 2;

/** How long, in seconds of event time, each finger stays on the glass. */
const TAP_PRESS_S = 0.03;

/**
 * Two taps on the centre of `target`, `gapS` seconds apart in EVENT time.
 *
 * The browser's gesture recognizer decides whether a tap repeats the one
 * before it from the timestamps the touch events carry, not from when they
 * arrive. Each event is therefore dispatched over CDP with an explicit
 * timestamp: the pair is a double-tap (or two single taps) by construction,
 * however long the runner takes between the dispatches. Two `tap()` calls
 * would instead each stamp their events with the wall clock, after an
 * actionability check of their own, so a loaded runner could stretch a
 * double-tap past the window. The events still go through the real touch
 * pipeline: the browser synthesizes the clicks and the dblclick itself.
 */
async function tapTwice(page: Page, target: Locator, gapS: number): Promise<void> {
  await target.scrollIntoViewIfNeeded();
  const point = await centerOf(target);
  const client = await page.context().newCDPSession(page);
  try {
    // Stamped from now: event time must not run behind the input the page
    // has already received.
    const start = Date.now() / 1000;
    for (const downAt of [start, start + gapS]) {
      await client.send('Input.dispatchTouchEvent', {
        type: 'touchStart',
        touchPoints: [{ x: point.x, y: point.y, id: 1 }],
        timestamp: downAt,
      });
      await client.send('Input.dispatchTouchEvent', {
        type: 'touchEnd',
        touchPoints: [],
        timestamp: downAt + TAP_PRESS_S,
      });
    }
  } finally {
    await client.detach();
  }
}

/**
 * Starts recording the `detail` (the click count) of every click the page
 * receives, readable through {@link recordedClickCounts}.
 */
async function recordClickCounts(page: Page): Promise<void> {
  await page.evaluate(() => {
    const counts: number[] = [];
    (window as ClickLogWindow).tmClickCounts = counts;
    document.addEventListener('click', (event) => counts.push(event.detail), true);
  });
}

/** The click counts recorded since {@link recordClickCounts}. */
async function recordedClickCounts(page: Page): Promise<readonly number[]> {
  return page.evaluate(() => (window as ClickLogWindow).tmClickCounts ?? []);
}

/** The page-side shape of the click log {@link recordClickCounts} installs. */
interface ClickLogWindow {
  /** The `detail` of each click, in order. */
  tmClickCounts?: number[];
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
    // Handles are proven on a NEIGHBOURING cell: a third tap on the target
    // would close what the pair opens.
    await cell(page, 1, 0).tap();
    await expect(handles(page)).toHaveCount(2);

    // The first tap activates the cell, the second makes the pair a
    // dblclick, which opens the editor.
    await tapTwice(page, cell(page, 2, 0), DOUBLE_TAP_GAP_S);
    await expect(editorInput(page)).toBeVisible();
    await expect(editorInput(page)).toBeFocused();
    await expect(handles(page)).toHaveCount(0); // hidden behind the editor

    await page.keyboard.press('Escape'); // cancel: selection intact, handles back
    await expect(editor(page)).toHaveCount(0);
    await expect(handles(page)).toHaveCount(2);
  });

  test('two taps further apart than the double-tap window never open the editor', async ({
    page,
  }) => {
    const target = cell(page, 2, 0);
    await recordClickCounts(page);
    await tapTwice(page, target, SEPARATE_TAPS_GAP_S);

    // The browser read the pair as two single taps. A dblclick would follow
    // the second click in the same dispatch, so once that click has landed
    // the editor's absence is final, not merely early.
    await expect.poll(() => recordedClickCounts(page)).toEqual([1, 1]);
    await expect(target).toBeFocused();
    await expect(editor(page)).toHaveCount(0);
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

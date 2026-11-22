// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import type { Locator, Page } from '@playwright/test';

import { appStable } from './settle';

/** The window the long-task recorder writes to. */
interface LongTaskWindow {
  /** The durations (ms) of the long tasks observed since recording began. */
  tmLongTasks: number[];
}

/**
 * Wheels a scroller down a screen at a time and back, `steps` screens each
 * way with an animation frame between steps, and returns the duration of
 * every long task (over 50 ms, the browser's own threshold) observed from
 * the first step until the page settles. The scroll probes of the windowed
 * components assert this list holds no task over their budget.
 *
 * Long-task timing is a Chromium API: callers skip other engines.
 *
 * @param page The page under test, already settled.
 * @param scroller The element that scrolls, under the pointer for the wheel.
 * @param steps The screens to wheel in each direction.
 * @returns The observed long tasks' durations, in milliseconds.
 */
export async function wheelScreensForLongTasks(
  page: Page,
  scroller: Locator,
  steps: number,
): Promise<number[]> {
  // Section: record long tasks from here on.
  await page.evaluate(() => {
    const record = window as unknown as LongTaskWindow;
    record.tmLongTasks = [];
    new PerformanceObserver((list) => {
      for (const entry of list.getEntries()) {
        record.tmLongTasks.push(entry.duration);
      }
    }).observe({ type: 'longtask' });
  });

  // Section: wheel down a screen at a time, then back.
  const box = (await scroller.boundingBox())!;
  await page.mouse.move(box.x + box.width / 2, box.y + box.height / 2);
  for (const direction of [1, -1]) {
    for (let step = 0; step < steps; step++) {
      await page.mouse.wheel(0, direction * box.height);
      await page.evaluate(() => new Promise((resolve) => requestAnimationFrame(resolve)));
    }
  }
  await appStable(page);

  return page.evaluate(() => (window as unknown as LongTaskWindow).tmLongTasks);
}

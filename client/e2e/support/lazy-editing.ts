// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { expect, type Page } from '@playwright/test';

import { gridScroller } from './grid';
import { storyUrl } from './story-map';

/**
 * Helpers for the lazy-editing story, whose grid's editing code arrives only
 * when a test releases it (or fails when a test fails it). The app is not
 * stable while that request is open, so nothing here waits for it to settle.
 */

/** The story's request hooks on the page's window. */
interface RequestHookWindow {
  /** Lets the open request for the editing code succeed. */
  readonly tmShowcaseReleaseGridEditing?: () => void;
  /** Fails the open request for the editing code. */
  readonly tmShowcaseFailGridEditing?: () => void;
}

/** Opens the lazy-editing story and waits for its grid to render. */
export async function gotoLazyEditing(page: Page): Promise<void> {
  await page.goto(storyUrl('grid-lazy-editing'));
  await expect(gridScroller(page)).toBeVisible();
}

/**
 * Waits for the grid's request for its editing code (the hooks are
 * one-shot, so a later call waits for the next request), then settles it.
 */
async function settleRequest(page: Page, outcome: 'release' | 'fail'): Promise<void> {
  await page.waitForFunction(
    () => (window as RequestHookWindow).tmShowcaseReleaseGridEditing !== undefined,
  );
  await page.evaluate((fail) => {
    const hooks = window as RequestHookWindow;
    (fail ? hooks.tmShowcaseFailGridEditing : hooks.tmShowcaseReleaseGridEditing)?.();
  }, outcome === 'fail');
}

/** Lets the editing code arrive. */
export async function releaseEditingCode(page: Page): Promise<void> {
  await settleRequest(page, 'release');
}

/** Fails the grid's open request for its editing code. */
export async function failEditingCode(page: Page): Promise<void> {
  await settleRequest(page, 'fail');
}

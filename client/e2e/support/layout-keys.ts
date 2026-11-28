// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import type { Page } from '@playwright/test';

/**
 * Whether the page under test reports an Apple platform, read the way the
 * library's `tmIsApplePlatform` reads it: `userAgentData.platform` where the
 * browser offers it, else `navigator.platform`. Playwright's WebKit reports
 * one even on Windows and Linux hosts.
 */
export async function pageIsApple(page: Page): Promise<boolean> {
  return page.evaluate(() => {
    const uaData = (navigator as Navigator & { userAgentData?: { platform?: string } })
      .userAgentData;
    return uaData?.platform
      ? /^(macos|ios)$/i.test(uaData.platform)
      : /^(mac|iphone|ipad|ipod)/i.test(navigator.platform);
  });
}

/** One key as a keyboard layout sends it. */
export interface LayoutKey {
  /** What the layout types: `'ي'` for the D key on an Arabic layout. */
  readonly key: string;
  /** The physical key, named by its US position: `'KeyD'`. */
  readonly code: string;
}

/**
 * Presses Mod plus a key as a non-Latin layout sends it, where `key` is
 * the layout's character and `code` the physical key. Playwright's keyboard
 * types only the US layout, so the pair is dispatched to the focused
 * element as a synthetic keydown and keyup.
 */
export async function pressModOnLayout(page: Page, layoutKey: LayoutKey): Promise<void> {
  const apple = await pageIsApple(page);
  await page.evaluate(
    ({ key, code, apple: isApple }) => {
      const target = document.activeElement ?? document.body;
      const init: KeyboardEventInit = {
        key,
        code,
        ctrlKey: !isApple,
        metaKey: isApple,
        bubbles: true,
        cancelable: true,
        composed: true,
      };
      target.dispatchEvent(new KeyboardEvent('keydown', init));
      target.dispatchEvent(new KeyboardEvent('keyup', init));
    },
    { key: layoutKey.key, code: layoutKey.code, apple },
  );
}

/** The Arabic (101) layout's characters on the keys the grid binds. */
export const ARABIC_KEYS = {
  A: { key: 'ش', code: 'KeyA' },
  D: { key: 'ي', code: 'KeyD' },
  F: { key: 'ب', code: 'KeyF' },
  Y: { key: 'غ', code: 'KeyY' },
  Z: { key: 'ئ', code: 'KeyZ' },
} as const satisfies Record<string, LayoutKey>;

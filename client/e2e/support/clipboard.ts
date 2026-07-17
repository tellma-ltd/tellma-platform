// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { readFileSync } from 'node:fs';
import { join } from 'node:path';
import { fileURLToPath } from 'node:url';

import type { Page } from '@playwright/test';

/**
 * Clipboard-payload plumbing for the paste battery.
 *
 * - {@link readFixture} loads an authored payload from `e2e/fixtures/clipboard`
 *   byte-exactly (the files are `.gitattributes`-pinned `-text`, so CRLF and
 *   quoted-LF bytes survive checkout).
 * - {@link seedClipboard} writes both flavors to the REAL system clipboard via
 *   the async Clipboard API — Chromium only, needs the `clipboard-write`
 *   permission (`test.use({ permissions: [...] })`).
 * - {@link syntheticPaste} dispatches a `ClipboardEvent('paste')` carrying a
 *   DataTransfer at the grid — no OS clipboard, no permissions, so it runs on
 *   every engine (the `@cross-engine` battery rides it).
 */

const FIXTURES_DIR = fileURLToPath(new URL('../fixtures/clipboard', import.meta.url));

/** A clipboard payload: one or both of the two flavors the grid consumes. */
export interface ClipboardFlavors {
  readonly text?: string;
  readonly html?: string;
}

/**
 * Reads a fixture payload (path relative to `e2e/fixtures/clipboard`,
 * e.g. `'excel/simple-2x2.txt'`) byte-exactly as a UTF-8 string.
 */
export function readFixture(name: string): string {
  return readFileSync(join(FIXTURES_DIR, name), 'utf8');
}

/**
 * Seeds the REAL system clipboard with the given flavors through
 * `navigator.clipboard.write` (promise-backed ClipboardItem). Chromium only:
 * requires the `clipboard-write` permission.
 */
export async function seedClipboard(page: Page, flavors: ClipboardFlavors): Promise<void> {
  await page.evaluate(async ({ text, html }) => {
    const item: Record<string, Blob> = {};
    if (text !== undefined) {
      item['text/plain'] = new Blob([text], { type: 'text/plain' });
    }
    if (html !== undefined) {
      item['text/html'] = new Blob([html], { type: 'text/html' });
    }
    await navigator.clipboard.write([new ClipboardItem(item)]);
  }, flavors);
}

/**
 * Dispatches a synthetic `paste` ClipboardEvent carrying `flavors` at the
 * grid (the active cell's `[role="grid"]` ancestor, falling back to the
 * page's grid). Engine notes:
 *
 * - Chromium/Firefox/WebKit all construct `DataTransfer` and honor the
 *   `clipboardData` member of the `ClipboardEvent` constructor init.
 * - Defensively, if an engine ever accepts the constructor but drops the
 *   payload (returns `clipboardData: null` or empty flavors), the helper
 *   falls back to a plain `Event('paste')` with a DataTransfer-shaped
 *   `clipboardData` grafted on — the grid only calls `getData`, which the
 *   shim serves identically. Never silently drops an engine.
 */
export async function syntheticPaste(page: Page, flavors: ClipboardFlavors): Promise<void> {
  await page.evaluate(({ text, html }) => {
    const grid =
      document.activeElement?.closest('[role="grid"]') ?? document.querySelector('[role="grid"]');
    if (grid === null || grid === undefined) {
      throw new Error('syntheticPaste: no [role="grid"] element on the page');
    }

    let event: ClipboardEvent | null = null;
    try {
      const data = new DataTransfer();
      if (text !== undefined) {
        data.setData('text/plain', text);
      }
      if (html !== undefined) {
        data.setData('text/html', html);
      }
      const candidate = new ClipboardEvent('paste', {
        clipboardData: data,
        bubbles: true,
        cancelable: true,
      });
      const intact =
        candidate.clipboardData !== null &&
        (text === undefined || candidate.clipboardData.getData('text/plain') === text) &&
        (html === undefined || candidate.clipboardData.getData('text/html') === html);
      if (intact) {
        event = candidate;
      }
    } catch {
      // DataTransfer or ClipboardEvent not constructible — use the shim.
    }

    if (event === null) {
      // Documented fallback: the grid's paste handler only touches
      // `clipboardData.getData`, so a minimal shim is behaviorally exact.
      const store = new Map<string, string>();
      if (text !== undefined) {
        store.set('text/plain', text);
      }
      if (html !== undefined) {
        store.set('text/html', html);
      }
      const shim = new Event('paste', { bubbles: true, cancelable: true }) as ClipboardEvent;
      Object.defineProperty(shim, 'clipboardData', {
        value: {
          getData: (type: string) => store.get(type) ?? '',
          setData: () => undefined,
          types: [...store.keys()],
        },
      });
      event = shim;
    }

    grid.dispatchEvent(event);
  }, flavors);
}

/**
 * Presses the platform undo chord (⌘Z where the page reports an Apple
 * platform — Playwright's WebKit does — Ctrl+Z everywhere else), matching
 * the grid's own `IS_MAC_PLATFORM` modifier resolution.
 */
export async function pressUndo(page: Page): Promise<void> {
  const isMac = await page.evaluate(() => /Mac|iPod|iPhone|iPad/.test(navigator.platform));
  await page.keyboard.press(isMac ? 'Meta+z' : 'Control+z');
}

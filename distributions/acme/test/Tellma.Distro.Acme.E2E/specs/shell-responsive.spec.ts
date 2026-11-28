// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import type { Locator, Page } from '@playwright/test';
import { signInAs, warmUp } from '@tellma/core-testing/e2e';

import { type AcmeLanguage, expect, test } from '../support/acme';
import {
  SHELL_TEXT,
  launcherField,
  openSessionMenu,
  pinnedBar,
  sessionButton,
} from '../support/shell';

// The shell at every breakpoint (spec 0027 §7.1, §13): on the sandbox, so
// its marker shows, the app bar's items are checked cell by cell at 390,
// 576, 768, 992, 1200 and 1440 px: the mark or the wordmark, Menu alone or
// grouped with its word, search as an icon after the spacer, in the group,
// or as a field of at most 280, 440 or 560 px with its keycap from xl, the
// 30 px bell and tray, the marker as a circle or a chip, and the session
// button's avatar, tenant name and user name. The pinned bar shows at every
// width, the launcher opens as the sheet below md and as the panel from md,
// the session menu opens inside the viewport, and the page never scrolls
// sideways. A phone width and a desktop width run on each pull request; the
// rest run nightly.

/** The widths, each with whether it runs on pull requests. */
const WIDTHS = [
  { width: 390, pr: true },
  { width: 576, pr: false },
  { width: 768, pr: false },
  { width: 992, pr: false },
  { width: 1200, pr: true },
  { width: 1440, pr: false },
] as const;

/** The height every width runs at. */
const HEIGHT = 800;

/**
 * Whether an element is drawn: in the layout, with a size.
 *
 * @param locator - The element.
 * @returns Whether it is visible.
 */
function drawn(locator: Locator): Promise<boolean> {
  return locator.isVisible();
}

/**
 * A box's width and height, rounded to the pixel; an element that is not
 * rendered takes none.
 *
 * @param locator - The element.
 * @returns Its size.
 */
async function size(locator: Locator): Promise<{ width: number; height: number }> {
  const box = await locator.boundingBox();
  return box === null
    ? { width: 0, height: 0 }
    : { width: Math.round(box.width), height: Math.round(box.height) };
}

/**
 * Checks that an open overlay lies inside the viewport.
 *
 * @param overlay - The overlay.
 * @param width - The viewport's width.
 */
async function insideViewport(overlay: Locator, width: number): Promise<void> {
  const box = (await overlay.boundingBox())!;
  expect(box.x, 'the overlay starts inside the viewport').toBeGreaterThanOrEqual(0);
  expect(Math.round(box.x + box.width), 'the overlay ends inside the viewport').toBeLessThanOrEqual(
    width,
  );
}

/**
 * Checks the app bar's items at a width against §7.1's row for it.
 *
 * @param page - The page, on the sandbox.
 * @param lang - The project's language.
 * @param width - The viewport's width.
 */
async function checkAppBar(page: Page, lang: AcmeLanguage, width: number): Promise<void> {
  const bar = page.locator('tm-app-bar');
  const md = width >= 768;
  const lg = width >= 992;
  const xl = width >= 1200;

  // --- The wordmark: the 18 px mark below md, the 14 px wordmark from md ---
  expect((await size(bar.locator('.tm-app-bar__wordmark img'))).height).toBe(md ? 14 : 18);

  // --- Menu: an icon button below md, grouped with its word from md ---
  await expect(bar.locator('.tm-app-bar__menu')).toBeVisible();
  expect(await drawn(bar.locator('.tm-app-bar__menu .tm-app-bar__wide'))).toBe(md);

  // --- Search: after the spacer below md, the group's icon at md, the field from lg ---
  expect(await drawn(bar.locator('.tm-app-bar__narrow-search'))).toBe(!md);
  expect(await drawn(bar.locator('.tm-app-bar__group-search'))).toBe(md && !lg);
  expect(await drawn(bar.locator('.tm-app-bar__field'))).toBe(lg);
  expect(await drawn(bar.locator('.tm-app-bar__keycap'))).toBe(xl);
  if (lg) {
    const most = width >= 1440 ? 560 : xl ? 440 : 280;
    expect((await size(bar.locator('.tm-app-bar__group'))).width).toBeLessThanOrEqual(most);
  }

  // --- Back and forward show in installed mode alone ---
  await expect(bar.locator('.tm-app-bar__history')).toHaveCount(0);

  // --- The bell and the tray: 30 px icon buttons at every width ---
  for (const slot of ['bell', 'tray']) {
    expect(await size(bar.locator(`[data-tm-slot="${slot}"] > button`))).toEqual({
      width: 30,
      height: 30,
    });
  }
  // The reserved slots take no width.
  for (const slot of await bar.locator('[data-tm-slot="help"], [data-tm-slot="assistant"]').all()) {
    expect((await size(slot)).width).toBe(0);
  }

  // --- The sandbox marker: a 24 px circle below md, a chip with its word from md ---
  const marker = bar.locator('.tm-app-bar__marker[data-tm-marker="sandbox"]');
  await expect(marker).toBeVisible();
  await expect(marker).toHaveAccessibleName(new RegExp(SHELL_TEXT[lang].sandbox));
  if (md) {
    expect((await size(marker.locator('.tm-app-bar__marker-label'))).width).toBeGreaterThan(1);
  } else {
    expect(await size(marker)).toEqual({ width: 24, height: 24 });
  }

  // --- The session button: the avatar, then the tenant's name from md and the user's from xl ---
  await expect(sessionButton(page, lang)).toBeVisible();
  expect(await drawn(bar.locator('.tm-app-bar__session .tm-app-bar__tenant'))).toBe(md);
  expect(await drawn(bar.locator('.tm-app-bar__session .tm-app-bar__user'))).toBe(xl);
  if (md) {
    expect((await size(bar.locator('.tm-app-bar__session'))).width).toBe(xl ? 200 : 160);
  }
}

test.describe('the shell at every width', () => {
  test.beforeEach(async ({ page, cast }) => {
    await signInAs(page, cast.user);
    await warmUp(page, '/2');
  });

  for (const { width, pr } of WIDTHS) {
    test(`the app bar, the pinned bar, the launcher and the session menu at ${width} px${pr ? '' : ' @nightly'}`, async ({
      page,
      lang,
    }) => {
      await page.setViewportSize({ width, height: HEIGHT });
      await page.goto('/2');
      await expect(sessionButton(page, lang)).toBeVisible();

      await checkAppBar(page, lang, width);
      await expect(pinnedBar(page)).toBeVisible();
      expect(
        await page.evaluate(() => document.documentElement.scrollWidth),
        'the page never scrolls sideways',
      ).toBeLessThanOrEqual(width);

      // --- The launcher: the sheet below md, the panel from md ---
      if (width < 768) {
        await page.locator('tm-app-bar .tm-app-bar__narrow-search').click();
        await expect(page.locator('.tm-launcher__sheet')).toBeVisible();
      } else if (width < 992) {
        await page.locator('tm-app-bar .tm-app-bar__group-search').click();
      } else {
        await page.keyboard.press('ControlOrMeta+K');
        await expect(launcherField(page)).toBeFocused();
      }
      const listbox = page.getByRole('listbox');
      await expect(listbox).toBeVisible();
      await insideViewport(listbox, width);
      await page.keyboard.press('Escape');
      await expect(listbox).toHaveCount(0);

      // --- The session menu, inside the viewport ---
      const menu = await openSessionMenu(page, lang);
      await insideViewport(menu, width);
      await page.keyboard.press('Escape');
      await expect(menu).toBeHidden();
    });
  }
});

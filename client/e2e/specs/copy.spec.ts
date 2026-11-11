// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { expect, test, type Page } from '@playwright/test';

import { expectNoAxeViolations } from '../support/axe';
import { useExclusiveClipboard } from '../support/clipboard-lock';
import { storyUrl } from '../support/story-map';

/**
 * Browser battery for tm-copy-button and tm-reference: the axe floor in
 * both themes and directions, a copy that changes the label without moving
 * anything, the manual fallback when the clipboard permission is denied,
 * and the reference chip cut short but copied whole.
 */

/** Replaces the async clipboard with a recorder, so no test touches the OS clipboard. */
async function stubClipboard(page: Page): Promise<void> {
  await page.addInitScript(() => {
    const written: string[] = [];
    (window as unknown as { tmWritten: string[] }).tmWritten = written;
    Object.defineProperty(navigator, 'clipboard', {
      configurable: true,
      value: {
        writeText: (text: string) => {
          written.push(text);
          return Promise.resolve();
        },
        write: async (items: ClipboardItem[]) => {
          for (const item of items) {
            written.push(await (await item.getType('text/plain')).text());
          }
        },
      },
    });
  });
}

/** Denies the async clipboard's write permission to the story's origin. */
async function denyClipboard(page: Page): Promise<void> {
  const session = await page.context().newCDPSession(page);
  await session.send('Browser.setPermission', {
    permission: { name: 'clipboard-write' },
    setting: 'denied',
    origin: new URL(page.url()).origin,
  });
}

/** What the stubbed clipboard received. */
function written(page: Page): Promise<string[]> {
  return page.evaluate(() => (window as unknown as { tmWritten: string[] }).tmWritten);
}

test.describe('axe floor', () => {
  for (const theme of ['light', 'dark'] as const) {
    for (const dir of ['ltr', 'rtl'] as const) {
      test(`copy story is axe-clean (${theme}, ${dir})`, async ({ page }) => {
        await page.goto(storyUrl('copy', { theme, dir }));
        await expect(page.getByTestId('reference')).toBeVisible();
        await expectNoAxeViolations(page);
      });
    }
  }

  test('copy story is axe-clean with the copied state showing', async ({ page }) => {
    await stubClipboard(page);
    await page.goto(storyUrl('copy'));
    await page.getByTestId('copy-label').getByRole('button').click();
    await expect(page.getByTestId('copy-label')).toHaveClass(/tm-copy-button--copied/);
    await expectNoAxeViolations(page);
  });
});

test.describe('success', () => {
  test('the label turns to "Copied" without moving anything, then back', async ({ page }) => {
    await stubClipboard(page);
    await page.clock.install();
    await page.goto(storyUrl('copy'));
    const labelled = page.getByTestId('copy-label');
    const button = labelled.getByRole('button');
    const before = (await button.boundingBox())!;
    const iconOnly = (await page.getByTestId('copy-icon').boundingBox())!;

    await button.click();
    await expect(labelled.locator('[role="status"]')).toHaveText('Copied');
    await expect(labelled).toHaveClass(/tm-copy-button--copied/);
    await expect(button.locator('.tm-copy-button__label:not(.tm-copy-button__label--off)')).toHaveText(
      'Copied',
    );
    expect(await button.boundingBox()).toEqual(before);
    expect(await written(page)).toEqual(['https://example.com/invoices/42']);

    await page.getByTestId('copy-icon').getByRole('button').click();
    await expect(page.getByTestId('copies')).toHaveText('1');
    expect(await page.getByTestId('copy-icon').boundingBox()).toEqual(iconOnly);

    await page.clock.runFor(2000);
    await expect(labelled.locator('[role="status"]')).toHaveText('');
    await expect(labelled).not.toHaveClass(/tm-copy-button--copied/);
    expect(await button.boundingBox()).toEqual(before);
  });

  test('a value built on the click reaches the clipboard', async ({ page }) => {
    await stubClipboard(page);
    await page.goto(storyUrl('copy'));
    await page.getByTestId('copy-built').getByRole('button').click();
    await expect(page.getByTestId('copy-built')).toHaveClass(/tm-copy-button--copied/);
    expect(await written(page)).toEqual(['Report 2026-11 (built on demand)']);
  });

  test('the reference copies its full value, however narrow its chip', async ({ page }) => {
    await stubClipboard(page);
    await page.goto(storyUrl('copy'));
    const narrow = page.getByTestId('reference-narrow');
    const chip = narrow.locator('.tm-reference__value');
    const cut = await chip.evaluate((el) => el.scrollWidth > el.clientWidth);
    expect(cut).toBe(true);
    await expect(chip).toHaveAttribute('dir', 'ltr');
    await narrow.getByRole('button', { name: 'Copy reference' }).click();
    await expect(narrow.locator('tm-copy-button')).toHaveClass(/tm-copy-button--copied/);
    expect(await written(page)).toEqual(['4bf92f3577b34da6a3ce929d0e0e4736']);
  });
});

test.describe('manual fallback (clipboard permission denied)', () => {
  useExclusiveClipboard();

  test('selects the text in a read-only input and copies it from the keyboard', async ({
    page,
    browserName,
  }) => {
    test.skip(browserName !== 'chromium', 'Denies the permission through Chromium DevTools.');
    await page.goto(storyUrl('copy'));
    await denyClipboard(page);

    const host = page.getByTestId('copy-icon');
    const button = host.getByRole('button', { name: 'Copy invoice number' });
    const before = (await button.boundingBox())!;
    await button.click();

    const manual = host.locator('.tm-copy-button__manual');
    await expect(manual).toBeFocused();
    await expect(manual).toHaveValue('INV-0042');
    await expect(manual).toHaveAttribute('readonly', '');
    const prompt = (await manual.getAttribute('aria-label'))!;
    expect(prompt).toMatch(/^Press (Ctrl\+C|⌘C) to copy$/);
    await expect(page.locator('.tm-tooltip__panel')).toHaveText(prompt);
    const selected = await manual.evaluate(
      (el: HTMLInputElement) => el.value.slice(el.selectionStart!, el.selectionEnd!),
    );
    expect(selected).toBe('INV-0042');
    expect(await button.boundingBox()).toEqual(before);

    await page.keyboard.press('ControlOrMeta+KeyC');
    await expect(page.getByTestId('copies')).toHaveText('1');
    await expect(manual).toHaveCount(0);
    await expect(button).toBeFocused();
    await expect(host.locator('[role="status"]')).toHaveText('Copied');
  });

  test('Escape takes the fallback down and returns focus to the button', async ({
    page,
    browserName,
  }) => {
    test.skip(browserName !== 'chromium', 'Denies the permission through Chromium DevTools.');
    await page.goto(storyUrl('copy'));
    await denyClipboard(page);
    const host = page.getByTestId('copy-icon');
    const button = host.getByRole('button', { name: 'Copy invoice number' });
    await button.click();
    await expect(host.locator('.tm-copy-button__manual')).toBeFocused();
    await page.keyboard.press('Escape');
    await expect(host.locator('.tm-copy-button__manual')).toHaveCount(0);
    await expect(button).toBeFocused();
    await expect(page.getByTestId('copies')).toHaveText('0');
  });
});

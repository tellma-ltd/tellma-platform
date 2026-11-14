// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { join } from 'node:path';

import { expect, test, type Page, type Route } from '@playwright/test';

import { expectNoAxeViolations } from '../support/axe';
import { storyUrl } from '../support/story-map';

/**
 * Browser battery for tm-image (DoD 10/11): caching behaviors observed at
 * the network (N instances → one request, reload → zero, etag flip → one
 * refetch, clearAll → sweep), the deferred near-viewport fetch, the
 * aborts of requests a box gives up on (navigation, scrolling away), the
 * no-CLS error glyph, edit-mode pick/fit/delete flows, and the
 * axe/RTL/forced-colors gates.
 */

// Relative to the Playwright cwd (client/).
const FIXTURE = join('projects', 'internal', 'showcase', 'public', 'test-image.png');

/** Counts requests for the shared test image, keyed by the query string. */
async function countRequests(page: Page): Promise<(search: string) => number> {
  const counts = new Map<string, number>();
  await page.route('**/test-image.png*', async (route) => {
    const search = new URL(route.request().url()).search;
    counts.set(search, (counts.get(search) ?? 0) + 1);
    await route.continue();
  });
  return (search) => counts.get(search) ?? 0;
}

async function expectLoaded(page: Page, testid: string): Promise<void> {
  await expect(page.getByTestId(testid).locator('.tm-image__img')).toBeVisible();
}

test.describe('caching at the network (DoD 10)', () => {
  test('three instances → one request; reload → zero; etag flip → one; clearAll → one', async ({
    page,
  }) => {
    // The three 96px view instances share the ?size=128 rendition; the
    // 160px edit instance uses ?size=256 — counted separately.
    const requests = await countRequests(page);
    await page.goto(storyUrl('image'));
    await expectLoaded(page, 'view-1');
    await expectLoaded(page, 'view-2');
    await expectLoaded(page, 'view-circle');
    expect(requests('?size=128')).toBe(1); // coalesced across instances

    // A reload under the same etag serves purely from CacheStorage.
    await page.reload();
    await expectLoaded(page, 'view-1');
    expect(requests('?size=128')).toBe(1);

    // A new etag invalidates every variant of the src: exactly one refetch
    // per displayed rendition.
    await page.getByTestId('flip-etag').click();
    await expect(page.getByTestId('etag-value')).toHaveText('v2');
    await expect
      .poll(() => requests('?size=128'), { message: 'etag flip refetches once' })
      .toBe(2);
    await expectLoaded(page, 'view-1');

    // clearAll sweeps tm-* caches: the next reload fetches again.
    await page.getByTestId('clear-caches').click();
    await page.reload();
    await expectLoaded(page, 'view-1');
    expect(requests('?size=128')).toBe(3);
  });

  test('a deferred instance fetches only near the viewport', async ({ page }) => {
    let deferredRequests = 0;
    await page.route('**/test-image.png?v=deferred*', async (route) => {
      deferredRequests += 1;
      await route.continue();
    });
    await page.goto(storyUrl('image'));
    await expectLoaded(page, 'view-1');
    expect(deferredRequests).toBe(0); // below the fold + margin

    await page.getByTestId('deferred-image').scrollIntoViewIfNeeded();
    await expect(page.getByTestId('deferred-image').locator('.tm-image__img')).toBeVisible();
    expect(deferredRequests).toBe(1);
  });
});

test.describe('cancellation', () => {
  /**
   * Holds every test-image response until released, and records which of
   * the held requests the page aborted. Holding is what keeps a request in
   * flight long enough for the page to give up on it.
   */
  async function holdImageRequests(
    page: Page,
  ): Promise<{ held: () => string[]; aborted: () => string[]; release: () => Promise<void> }> {
    const routes: Route[] = [];
    const aborted: string[] = [];
    await page.route('**/test-image.png*', (route) => {
      routes.push(route);
    });
    page.on('requestfailed', (request) => {
      if (request.url().includes('test-image.png')) {
        aborted.push(new URL(request.url()).search);
      }
    });
    return {
      held: () => routes.map((route) => new URL(route.request().url()).search),
      aborted: () => [...aborted],
      release: async () => {
        for (const route of routes.splice(0)) {
          // A request the page already aborted can no longer be continued.
          await route.continue().catch(() => undefined);
        }
      },
    };
  }

  test('navigating away aborts every image request still in flight', async ({ page }) => {
    const requests = await holdImageRequests(page);
    await page.goto(storyUrl('image'));
    // The three view instances share one rendition; the edit box has its own.
    await expect.poll(() => requests.held().sort()).toEqual(['?size=128', '?size=256']);
    expect(requests.aborted()).toEqual([]);

    // A client-side navigation destroys every box on the story.
    await page.getByRole('link', { name: 'Button (tmButton)' }).click();
    await expect(page).toHaveURL(/\/story\/button/);
    await expect.poll(() => requests.aborted().sort()).toEqual(['?size=128', '?size=256']);
    await requests.release();
  });

  test('a deferred box scrolled away before its bytes arrive aborts, and refetches on return', async ({
    page,
  }) => {
    const requests = await holdImageRequests(page);
    await page.goto(storyUrl('image'));
    await expect.poll(() => requests.held()).toContain('?size=128');
    const deferred = page.getByTestId('deferred-image');
    const isDeferred = (search: string): boolean => search.startsWith('?v=deferred');

    await deferred.scrollIntoViewIfNeeded();
    await expect.poll(() => requests.held().filter(isDeferred)).toHaveLength(1);
    // Back to the top: the box leaves the viewport with its request held.
    await page.evaluate(() => window.scrollTo(0, 0));
    await expect.poll(() => requests.aborted().filter(isDeferred)).toHaveLength(1);
    expect(requests.aborted().filter((search) => !isDeferred(search))).toEqual([]);

    // Coming back starts the request again, and this time it lands.
    await requests.release();
    await page.unroute('**/test-image.png*');
    await deferred.scrollIntoViewIfNeeded();
    await expect(deferred.locator('.tm-image__img')).toBeVisible();
    await expect(deferred.locator('.tm-image__glyph--error')).toHaveCount(0);
  });
});

test.describe('error + placeholder (DoD 10)', () => {
  test('a failing fetch shows the labelled glyph with zero layout shift', async ({ page }) => {
    await page.goto(storyUrl('image'));
    const box = page.getByTestId('error-image');
    await expect(box.locator('.tm-image__glyph--error')).toBeVisible();
    const rect = (await box.boundingBox())!;
    expect(rect.width).toBe(96);
    expect(rect.height).toBe(96);
  });

  test('an empty src renders the custom placeholder template', async ({ page }) => {
    await page.goto(storyUrl('image'));
    await expect(page.getByTestId('custom-placeholder')).toHaveText('LH');
  });
});

test.describe('edit mode (DoD 11)', () => {
  test('pick → cover-fit emission; slider zoom → adjusted fit; Done → local preview', async ({
    page,
  }) => {
    await page.goto(storyUrl('image'));
    const editBox = page.getByTestId('edit-image');
    await expect(editBox.locator('.tm-image__img')).toBeVisible();

    const chooser = page.waitForEvent('filechooser');
    await editBox.locator('[data-tm-image-action="replace"]').click();
    await (await chooser).setFiles(FIXTURE);

    // The pick itself is one committed edit: the file + the cover fit.
    await expect(page.getByTestId('image-change')).toHaveText(
      '{"blob":true,"rect":{"x":0,"y":0,"width":1,"height":1}}',
    );
    await expect(editBox.locator('.tm-image__crop')).toBeVisible();

    // The always-visible slider is the accessible zoom path.
    await editBox.locator('.tm-image__zoom').evaluate((el) => {
      const slider = el as HTMLInputElement;
      slider.value = '2';
      slider.dispatchEvent(new Event('input', { bubbles: true }));
      slider.dispatchEvent(new Event('change', { bubbles: true }));
    });
    await expect(page.getByTestId('image-change')).toHaveText(
      '{"blob":true,"rect":{"x":0.25,"y":0.25,"width":0.5,"height":0.5}}',
    );

    await editBox.locator('.tm-image__edit-bar button').click();
    await expect(editBox.locator('.tm-image__crop-view img')).toBeVisible();
  });

  test('re-fit of the existing image emits blob: null; delete returns the placeholder', async ({
    page,
  }) => {
    await page.goto(storyUrl('image'));
    const editBox = page.getByTestId('edit-image');
    await expect(editBox.locator('.tm-image__img')).toBeVisible();

    await editBox.locator('[data-tm-image-action="adjust"]').click();
    await expect(editBox.locator('.tm-image__crop')).toBeVisible();
    await editBox.locator('.tm-image__zoom').evaluate((el) => {
      const slider = el as HTMLInputElement;
      slider.value = '2';
      slider.dispatchEvent(new Event('input', { bubbles: true }));
      slider.dispatchEvent(new Event('change', { bubbles: true }));
    });
    await expect(page.getByTestId('image-change')).toHaveText(
      '{"blob":null,"rect":{"x":0.25,"y":0.25,"width":0.5,"height":0.5}}',
    );
    await editBox.locator('.tm-image__edit-bar button').click();

    await editBox.locator('[data-tm-image-action="remove"]').click();
    await expect(page.getByTestId('image-change')).toHaveText('deleted');
    await expect(editBox.locator('.tm-image__glyph')).toBeVisible();
  });

  test('keyboard on the crop surface: arrows pan, +/- zoom', async ({ page }) => {
    await page.goto(storyUrl('image'));
    const editBox = page.getByTestId('edit-image');
    await expect(editBox.locator('.tm-image__img')).toBeVisible();
    await editBox.locator('[data-tm-image-action="adjust"]').click();

    const surface = editBox.locator('.tm-image__crop');
    await surface.focus();
    await page.keyboard.press('+');
    await page.keyboard.press('ArrowRight');
    // Committed after the debounce: zoomed in and panned right of center.
    await expect
      .poll(async () => page.getByTestId('image-change').textContent())
      .toContain('"blob":null');
    const readout = JSON.parse((await page.getByTestId('image-change').textContent())!) as {
      rect: { x: number; width: number };
    };
    expect(readout.rect.width).toBeCloseTo(1 / 1.1, 2);
    expect(readout.rect.x).toBeGreaterThan((1 - readout.rect.width) / 2);
  });
});

test.describe('appearance gates', () => {
  for (const theme of ['light', 'dark'] as const) {
    test(`image story is axe-clean (${theme})`, async ({ page }) => {
      await page.goto(storyUrl('image', { theme }));
      await expect(page.getByTestId('view-1').locator('.tm-image__img')).toBeVisible();
      await expectNoAxeViolations(page);
    });
  }

  test('RTL: the story renders and loads', async ({ page }) => {
    await page.goto(storyUrl('image', { dir: 'rtl' }));
    await expectLoaded(page, 'view-1');
    await expectNoAxeViolations(page);
  });

  test('forced-colors: the box keeps a visible border', async ({ page }) => {
    await page.emulateMedia({ forcedColors: 'active' });
    await page.goto(storyUrl('image'));
    await expectLoaded(page, 'view-1');
    const border = await page
      .getByTestId('view-1')
      .evaluate((el) => getComputedStyle(el).borderTopStyle + '|' + getComputedStyle(el).borderTopWidth);
    expect(border).toMatch(/^solid\|[1-9]/);
  });
});

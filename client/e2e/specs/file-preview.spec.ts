// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { join } from 'node:path';

import { expect, test, type Page } from '@playwright/test';

import { expectNoAxeViolations } from '../support/axe';
import { storyUrl } from '../support/story-map';

/**
 * Browser battery for TmFilePreview (DoD 13): per-kind rendering (raster,
 * SVG-via-img, PDF's dual path, media, capped text), the HTML-is-never-
 * rendered policy pin, download on every kind, print for images only,
 * lazy-source loading/error states, the pick-and-preview flow over real
 * fixture bytes, and the axe gates.
 */

// Relative to the Playwright cwd (client/).
const FIXTURES = join('e2e', 'fixtures', 'preview');

function viewer(page: Page): ReturnType<Page['locator']> {
  return page.locator('tm-file-preview-content');
}

async function open(page: Page, testid: string): Promise<void> {
  await page.getByTestId(testid).click();
  await expect(viewer(page)).toBeVisible();
}

async function close(page: Page): Promise<void> {
  await page.keyboard.press('Escape');
  await expect(viewer(page)).toHaveCount(0);
}

test.describe('per-kind rendering (DoD 13)', () => {
  test('raster image: img renders, print offered, download named', async ({ page }) => {
    await page.goto(storyUrl('file-preview'));
    await open(page, 'preview-image');
    await expect(page.locator('.tm-modal__title')).toHaveText('pattern.png');
    await expect(viewer(page).locator('img.tm-preview__media')).toBeVisible();
    await expect(viewer(page).locator('[data-tm-preview-action="print"]')).toBeVisible();
    await expect(viewer(page).locator('[data-tm-preview-action="download"]')).toHaveAttribute(
      'download',
      'pattern.png',
    );
    await expectNoAxeViolations(page);
    await close(page);
  });

  test('SVG renders via img only — never inline', async ({ page }) => {
    await page.goto(storyUrl('file-preview'));
    await open(page, 'preview-svg');
    await expect(viewer(page).locator('img.tm-preview__media')).toBeVisible();
    expect(await viewer(page).locator('svg.tm-preview__media, iframe').count()).toBe(0);
    await expect(viewer(page).locator('[data-tm-preview-action="print"]')).toHaveCount(0);
    await close(page);
  });

  test('text renders escaped in a pre; 2 MB input is capped with a notice', async ({ page }) => {
    await page.goto(storyUrl('file-preview'));
    await open(page, 'preview-text');
    await expect(viewer(page).locator('.tm-preview__text')).toContainText('<not markup>');
    await close(page);

    await open(page, 'preview-big-text');
    await expect(viewer(page).locator('.tm-preview__notice')).toBeVisible();
    await expect(viewer(page).locator('.tm-preview__text')).toContainText('start-marker');
    const length = await viewer(page)
      .locator('.tm-preview__text')
      .evaluate((el) => el.textContent?.length ?? 0);
    expect(length).toBeLessThanOrEqual(1024 * 1024);
    await close(page);
  });

  test('audio renders native controls from a blob and streams from a url', async ({ page }) => {
    await page.goto(storyUrl('file-preview'));
    await open(page, 'preview-audio');
    await expect(viewer(page).locator('audio[controls]')).toBeVisible();
    await close(page);

    await open(page, 'preview-audio-url');
    const audio = viewer(page).locator('audio[controls]');
    await expect(audio).toBeVisible();
    await expect(audio).toHaveAttribute('src', '/test-audio.wav'); // direct — range requests
    await close(page);
  });

  test('unknown kinds get the download-only card', async ({ page }) => {
    await page.goto(storyUrl('file-preview'));
    await open(page, 'preview-unknown');
    await expect(viewer(page).locator('.tm-preview__card-title')).toHaveText(
      'Preview not available',
    );
    await expect(viewer(page).locator('[data-tm-preview-action="download"]')).toHaveAttribute(
      'download',
      'blob.bin',
    );
    await expectNoAxeViolations(page);
    await close(page);
  });
});

test.describe('the HTML policy pin (DoD 13)', () => {
  test('HTML is NEVER rendered — card only, no iframe, no script execution', async ({ page }) => {
    await page.goto(storyUrl('file-preview'));
    await open(page, 'preview-html');
    await expect(viewer(page).locator('.tm-preview__card-title')).toHaveText(
      'Preview not available',
    );
    expect(await viewer(page).locator('iframe').count()).toBe(0);
    expect(await viewer(page).locator('.tm-preview__text').count()).toBe(0);
    expect(await page.title()).not.toBe('pwned');
    await close(page);
  });
});

test.describe('PDF dual path (DoD 13)', () => {
  test('without a browser viewer: the honest unsupported card', async ({ page }) => {
    await page.addInitScript(() => {
      Object.defineProperty(Navigator.prototype, 'pdfViewerEnabled', { get: () => false });
    });
    await page.goto(storyUrl('file-preview'));
    await open(page, 'preview-pdf');
    await expect(viewer(page).locator('.tm-preview__card-title')).toHaveText(
      'Preview not available',
    );
    expect(await viewer(page).locator('iframe').count()).toBe(0);
    await close(page);
  });

  test('with a viewer: a NON-sandboxed iframe carries the blob URL', async ({ page }) => {
    await page.addInitScript(() => {
      Object.defineProperty(Navigator.prototype, 'pdfViewerEnabled', { get: () => true });
    });
    await page.goto(storyUrl('file-preview'));
    await open(page, 'preview-pdf');
    const frame = viewer(page).locator('iframe.tm-preview__frame');
    await expect(frame).toBeVisible();
    expect(await frame.getAttribute('sandbox')).toBeNull(); // sandboxed frames never render PDFs
    expect(await frame.getAttribute('src')).toMatch(/^blob:/);
    await close(page);
  });
});

test.describe('lazy sources (DoD 13)', () => {
  test('a slow loader shows the busy spinner, then the content', async ({ page }) => {
    await page.goto(storyUrl('file-preview'));
    await open(page, 'preview-lazy');
    await expect(viewer(page).locator('.tm-preview[aria-busy="true"]')).toBeVisible();
    await expect(viewer(page).locator('.tm-preview__text')).toHaveText('finally here');
    await close(page);
  });

  test('a failing loader shows the localized error state inside the modal', async ({ page }) => {
    await page.goto(storyUrl('file-preview'));
    await open(page, 'preview-failing');
    await expect(viewer(page).locator('.tm-preview__card-title')).toHaveText(
      'The file could not be loaded',
    );
    await expectNoAxeViolations(page);
    await close(page);
  });
});

test.describe('pick-and-preview over fixture bytes', () => {
  test('a picked PNG fixture opens and renders', async ({ page }) => {
    await page.goto(storyUrl('file-preview'));
    const chooser = page.waitForEvent('filechooser');
    await page.getByTestId('pick-and-preview').click();
    await (await chooser).setFiles(join(FIXTURES, 'tiny.png'));
    await expect(viewer(page)).toBeVisible();
    await expect(page.locator('.tm-modal__title')).toHaveText('tiny.png');
    await expect(viewer(page).locator('img.tm-preview__media')).toBeVisible();
    await close(page);
  });

  test('a picked HTML fixture stays download-only (policy over real bytes)', async ({ page }) => {
    await page.goto(storyUrl('file-preview'));
    const chooser = page.waitForEvent('filechooser');
    await page.getByTestId('pick-and-preview').click();
    await (await chooser).setFiles(join(FIXTURES, 'sample.html'));
    await expect(viewer(page).locator('.tm-preview__card-title')).toHaveText(
      'Preview not available',
    );
    expect(await page.title()).not.toBe('pwned');
    await close(page);
  });

  test('a picked SVG fixture renders through img — its script never runs', async ({ page }) => {
    await page.goto(storyUrl('file-preview'));
    const titleBefore = await page.title();
    const chooser = page.waitForEvent('filechooser');
    await page.getByTestId('pick-and-preview').click();
    await (await chooser).setFiles(join(FIXTURES, 'sample.svg'));
    await expect(page.locator('.tm-modal__title')).toHaveText('sample.svg');
    // The fixture carries <script>document.title="pwned"</script>. An <img>
    // sink is inert for SVG script content; inlining the markup (or framing
    // it same-origin) would run it, so both the sink and the title are pinned.
    await expect(viewer(page).locator('img.tm-preview__media')).toBeVisible();
    expect(await viewer(page).locator('svg.tm-preview__media, iframe').count()).toBe(0);
    expect(await page.title()).toBe(titleBefore);
    await close(page);
  });

  test('a picked CSV fixture renders as text', async ({ page }) => {
    await page.goto(storyUrl('file-preview'));
    const chooser = page.waitForEvent('filechooser');
    await page.getByTestId('pick-and-preview').click();
    await (await chooser).setFiles(join(FIXTURES, 'sample.csv'));
    await expect(viewer(page).locator('.tm-preview__text')).toContainText('account,debit,credit');
    await close(page);
  });
});

test.describe('appearance gates', () => {
  test('dark theme with an open viewer is axe-clean', async ({ page }) => {
    await page.goto(storyUrl('file-preview', { theme: 'dark' }));
    await open(page, 'preview-image');
    await expectNoAxeViolations(page);
  });

  test('RTL with an open viewer is axe-clean', async ({ page }) => {
    await page.goto(storyUrl('file-preview', { dir: 'rtl' }));
    await open(page, 'preview-text');
    await expectNoAxeViolations(page);
  });
});

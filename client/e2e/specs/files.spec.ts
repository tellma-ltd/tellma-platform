// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { expect, test, type Locator, type Page } from '@playwright/test';

import { expectNoAxeViolations } from '../support/axe';
import { storyUrl } from '../support/story-map';

/**
 * Browser battery for tmFilePicker + tm-dropzone (DoD 12): the OS-dialog
 * path from the button AND the dropzone keyboard path, synthetic
 * DataTransfer drops (`@cross-engine`), guardrail rejections with
 * LiveAnnouncer announcements, the focused-zone paste, the document-level
 * missed-drop guard, and the axe/RTL/forced-colors gates.
 */

/** Dispatches a synthetic file drop (or paste) at the target element. */
async function dispatchFiles(
  target: Locator,
  type: 'drop' | 'paste',
  files: { name: string; type: string; size: number }[],
): Promise<void> {
  await target.evaluate(
    (element, { type: eventType, files: specs }) => {
      const transfer = new DataTransfer();
      for (const spec of specs) {
        transfer.items.add(new File([new Uint8Array(spec.size)], spec.name, { type: spec.type }));
      }
      // Some engines construct the event but silently drop the payload
      // (Firefox's ClipboardEvent) — verify it landed, else shim it on.
      let event: Event;
      if (eventType === 'paste') {
        event = new ClipboardEvent('paste', { clipboardData: transfer, cancelable: true });
        if ((event as ClipboardEvent).clipboardData !== transfer) {
          event = new Event('paste', { cancelable: true });
          Object.defineProperty(event, 'clipboardData', { value: transfer });
        }
      } else {
        event = new DragEvent('drop', { dataTransfer: transfer, cancelable: true });
        if ((event as DragEvent).dataTransfer !== transfer) {
          event = new Event('drop', { cancelable: true });
          Object.defineProperty(event, 'dataTransfer', { value: transfer });
        }
      }
      element.dispatchEvent(event);
    },
    { type, files },
  );
}

async function readout(page: Page): Promise<unknown> {
  const text = await page.getByTestId('selection-readout').textContent();
  return JSON.parse(text ?? 'null');
}

test.describe('dialog paths (DoD 12)', () => {
  test('the button opens the OS dialog; the selection runs the guardrails', async ({ page }) => {
    await page.goto(storyUrl('files'));
    const chooser = page.waitForEvent('filechooser');
    await page.getByTestId('attach-button').click();
    await (await chooser).setFiles([
      { name: 'notes.txt', mimeType: 'text/plain', buffer: Buffer.from('hello') },
      { name: 'raw.bin', mimeType: 'application/octet-stream', buffer: Buffer.from('x') },
    ]);
    await expect(page.getByTestId('selection-readout')).toContainText('"accepted":["notes.txt"]');
    await expect(page.getByTestId('selection-readout')).toContainText(
      '{"name":"raw.bin","reason":"type"}',
    );
  });

  test('the dropzone is one focusable button: Enter opens the dialog', async ({ page }) => {
    await page.goto(storyUrl('files'));
    await page.getByTestId('dropzone').focus();
    const chooser = page.waitForEvent('filechooser');
    await page.keyboard.press('Enter');
    await (await chooser).setFiles({
      name: 'kbd.txt',
      mimeType: 'text/plain',
      buffer: Buffer.from('kbd'),
    });
    await expect(page.getByTestId('selection-readout')).toContainText('"accepted":["kbd.txt"]');
  });
});

test.describe('drops and paste', () => {
  test('@cross-engine a synthetic drop runs the shared guardrail pipeline', async ({ page }) => {
    await page.goto(storyUrl('files'));
    await dispatchFiles(page.getByTestId('dropzone'), 'drop', [
      { name: 'a.txt', type: 'text/plain', size: 10 },
      { name: 'big.txt', type: 'text/plain', size: 2 * 1024 * 1024 },
      { name: 'nope.bin', type: 'application/octet-stream', size: 10 },
    ]);
    const selection = (await readout(page)) as {
      accepted: string[];
      rejected: { name: string; reason: string }[];
    };
    expect(selection.accepted).toEqual(['a.txt']);
    expect(selection.rejected).toEqual([
      { name: 'big.txt', reason: 'size' },
      { name: 'nope.bin', reason: 'type' },
    ]);
  });

  test('@cross-engine a single-file zone takes the first and rejects the rest', async ({
    page,
  }) => {
    await page.goto(storyUrl('files'));
    await dispatchFiles(page.getByTestId('dropzone-single'), 'drop', [
      { name: 'first.txt', type: 'text/plain', size: 5 },
      { name: 'second.txt', type: 'text/plain', size: 5 },
    ]);
    const selection = (await readout(page)) as {
      accepted: string[];
      rejected: { name: string; reason: string }[];
    };
    expect(selection.accepted).toEqual(['first.txt']);
    expect(selection.rejected).toEqual([{ name: 'second.txt', reason: 'count' }]);
  });

  test('rejections are announced through the live announcer', async ({ page }) => {
    await page.goto(storyUrl('files'));
    await dispatchFiles(page.getByTestId('dropzone'), 'drop', [
      { name: 'huge.txt', type: 'text/plain', size: 2 * 1024 * 1024 },
    ]);
    const region = page.locator('.cdk-live-announcer-element');
    await expect(region).toContainText('No files added, 1 file rejected');
    await expect(region).toContainText('huge.txt is larger than 1 MB');
  });

  test('@cross-engine the focused zone accepts clipboard files', async ({ page }) => {
    await page.goto(storyUrl('files'));
    const zone = page.getByTestId('dropzone');
    await zone.focus();
    await dispatchFiles(zone, 'paste', [{ name: 'shot.png', type: 'image/png', size: 100 }]);
    await expect(page.getByTestId('selection-readout')).toContainText('"accepted":["shot.png"]');
  });

  test('the missed-drop guard cancels drops outside designated targets', async ({ page }) => {
    await page.goto(storyUrl('files'));
    const cancelled = await page.evaluate(() => {
      const transfer = new DataTransfer();
      transfer.items.add(new File(['x'], 'missed.txt', { type: 'text/plain' }));
      const over = new DragEvent('dragover', {
        bubbles: true,
        cancelable: true,
        dataTransfer: transfer,
      });
      document.body.dispatchEvent(over);
      const drop = new DragEvent('drop', {
        bubbles: true,
        cancelable: true,
        dataTransfer: transfer,
      });
      document.body.dispatchEvent(drop);
      return { over: over.defaultPrevented, drop: drop.defaultPrevented };
    });
    expect(cancelled).toEqual({ over: true, drop: true });
    // Nothing was selected, and the SPA never navigated.
    await expect(page.getByTestId('selection-readout')).toHaveText('none');
    expect(page.url()).toContain('/story/files');
  });
});

test.describe('appearance gates', () => {
  for (const theme of ['light', 'dark'] as const) {
    test(`files story is axe-clean (${theme})`, async ({ page }) => {
      await page.goto(storyUrl('files', { theme }));
      await expect(page.getByTestId('dropzone')).toBeVisible();
      await expectNoAxeViolations(page);
    });
  }

  test('RTL renders and stays axe-clean', async ({ page }) => {
    await page.goto(storyUrl('files', { dir: 'rtl' }));
    await expect(page.getByTestId('dropzone')).toBeVisible();
    await expectNoAxeViolations(page);
  });

  test('forced-colors keeps the dashed affordance visible', async ({ page }) => {
    await page.emulateMedia({ forcedColors: 'active' });
    await page.goto(storyUrl('files'));
    const border = await page
      .getByTestId('dropzone')
      .evaluate((el) => getComputedStyle(el).borderTopStyle);
    expect(border).toBe('dashed');
  });
});

// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { expect, test, type Page } from '@playwright/test';

import { axeViolations, expectNoAxeViolations } from '../support/axe';
import { storyUrl } from '../support/story-map';

/**
 * Self-checks of the axe scan the other specs rely on: what it reaches
 * inside frames, and how its scope bounds the frames it touches. Each test
 * adds a frame to an otherwise clean story, so whatever the scan reports
 * comes from that frame.
 */

/** Where the frames go: the story's own content, inside a scoped scan. */
const STORY = 'main.story-main';

/** How a test frame gets its document. */
type FrameSource =
  | { readonly kind: 'srcdoc'; readonly html: string }
  | { readonly kind: 'script'; readonly html: string }
  | { readonly kind: 'url'; readonly url: string };

/**
 * Adds a titled iframe inside `parent` and waits until its document is
 * ready: loaded from `srcdoc` or a URL, or an `about:blank` frame filled by
 * script, as a print helper does.
 */
async function addFrame(page: Page, parent: string, source: FrameSource): Promise<void> {
  await page.evaluate(
    async ({ parent, source }) => {
      const host = document.querySelector(parent);
      if (host === null) {
        throw new Error(`No element matches ${parent}.`);
      }
      const frame = document.createElement('iframe');
      frame.title = 'Test frame';
      const loaded = new Promise((resolve) =>
        frame.addEventListener('load', resolve, { once: true }),
      );
      if (source.kind === 'srcdoc') {
        frame.srcdoc = source.html;
      } else if (source.kind === 'url') {
        frame.src = source.url;
      }
      host.append(frame);
      await loaded;
      if (source.kind === 'script') {
        // The initial about:blank document, written in place.
        frame.contentDocument!.body.innerHTML = source.html;
      }
    },
    { parent, source },
  );
}

test.beforeEach(async ({ page }) => {
  await page.goto(storyUrl('button'));
  await expect(page.locator(STORY)).toBeVisible();
});

test('the story the frames are added to is clean on its own', async ({ page }) => {
  await expectNoAxeViolations(page);
});

test('a srcdoc frame is scanned: a violation inside it is reported', async ({ page }) => {
  await addFrame(page, STORY, { kind: 'srcdoc', html: '<button></button>' });

  const violations = await axeViolations(page);
  expect(violations.map((v) => v.id)).toEqual(['button-name']);
  expect(violations[0].targets).toEqual([expect.stringMatching(/iframe[^,]*,button$/)]);
});

test('an about:blank frame filled by script is scanned: a violation inside it is reported', async ({
  page,
}) => {
  await addFrame(page, STORY, {
    kind: 'script',
    html: '<img src="data:image/gif;base64,R0lGODlhAQABAAAAACw=">',
  });

  const violations = await axeViolations(page);
  expect(violations.map((v) => v.id)).toEqual(['image-alt']);
  expect(violations[0].targets).toEqual([expect.stringMatching(/iframe[^,]*,img$/)]);
});

test('a clean same-origin frame keeps the scan clean', async ({ page }) => {
  await addFrame(page, STORY, { kind: 'srcdoc', html: '<button>Save</button>' });

  await expectNoAxeViolations(page);
});

test('a cross-origin frame fails a scan it lies inside, and is ignored by one it lies outside', async ({
  page,
}) => {
  // A data: document has an opaque origin, so it is cross-origin to the page.
  await addFrame(page, 'body', {
    kind: 'url',
    url: 'data:text/html,<button>Save</button>',
  });

  await expect(axeViolations(page)).rejects.toThrow(/cross-origin/);
  await expectNoAxeViolations(page, STORY);
});
